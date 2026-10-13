using System.Text;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.XpsShip.Domain;
using Nop.Plugin.Misc.XpsShip.Logic;
using Nop.Plugin.Misc.XpsShip.Models;
using Nop.Plugin.Misc.XpsShip.Services;
using Nop.Services.Logging;
using Nop.Services.Orders;

namespace Nop.Plugin.Misc.XpsShip.Controllers;

/// <summary>
/// The URLs XPS calls. A plain MVC controller, not nopCommerce's BaseController: that one's filters redirect to
/// HTTPS, record the visitor's IP and last page, and create a guest customer, none of which a webhook should do.
/// </summary>
[IgnoreAntiforgeryToken]
public class XpsShipWebhookController : Controller
{
    private readonly ILogger _logger;
    private readonly IOrderService _orderService;
    private readonly XpsOrderBuilder _orderBuilder;
    private readonly XpsRequestGuard _guard;
    private readonly XpsShipDataService _dataService;
    private readonly XpsUpdateProcessor _updateProcessor;

    public XpsShipWebhookController(ILogger logger,
        IOrderService orderService,
        XpsOrderBuilder orderBuilder,
        XpsRequestGuard guard,
        XpsShipDataService dataService,
        XpsUpdateProcessor updateProcessor)
    {
        _logger = logger;
        _orderService = orderService;
        _orderBuilder = orderBuilder;
        _guard = guard;
        _dataService = dataService;
        _updateProcessor = updateProcessor;
    }

    [HttpGet]
    public async Task<IActionResult> ListOrders()
    {
        var guard = await GuardAsync(XpsShipDefaults.EndpointListOrders);
        if (guard.Result != null)
            return guard.Result;

        try
        {
            var list = await _orderBuilder.BuildListAsync();
            await LogAsync(XpsShipDefaults.EndpointListOrders, 200, guard.Ip,
                summary: $"{list.Orders.Count} order(s) sent, {list.Excluded.Count} left out");

            return Json(200, new XpsOrderListResponse { Orders = list.Orders });
        }
        catch (Exception ex)
        {
            return await FailAsync(XpsShipDefaults.EndpointListOrders, guard.Ip, ex);
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetOrder(string? orderId)
    {
        var guard = await GuardAsync(XpsShipDefaults.EndpointGetOrder);
        if (guard.Result != null)
            return guard.Result;

        try
        {
            var order = Guid.TryParse(orderId, out var guid) ? await _orderService.GetOrderByGuidAsync(guid) : null;
            if (order == null || order.Deleted)
            {
                await LogAsync(XpsShipDefaults.EndpointGetOrder, 404, guard.Ip, summary: $"order '{orderId}' not found");
                return Json(404, new { error = "order not found" });
            }

            var xps = await _orderBuilder.BuildOrderAsync(order);
            await LogAsync(XpsShipDefaults.EndpointGetOrder, 200, guard.Ip, order.OrderGuid, order.CustomOrderNumber,
                summary: $"{xps.Items.Count} line(s) left to ship");

            return Json(200, new XpsOrderResponse { Order = xps });
        }
        catch (Exception ex)
        {
            return await FailAsync(XpsShipDefaults.EndpointGetOrder, guard.Ip, ex);
        }
    }

    [HttpPost]
    public async Task<IActionResult> UpdateOrder()
    {
        var guard = await GuardAsync(XpsShipDefaults.EndpointUpdateOrder);
        if (guard.Result != null)
            return guard.Result;

        if (!Request.HasFormContentType)
        {
            await LogAsync(XpsShipDefaults.EndpointUpdateOrder, 415, guard.Ip, summary: "body is not form-encoded");
            return Json(415, new { error = "expected application/x-www-form-urlencoded" });
        }

        var form = await Request.ReadFormAsync();
        var fields = form.SelectMany(f => f.Value.Select(v => new KeyValuePair<string, string>(f.Key, v ?? string.Empty))).ToList();

        // Logged before anything else, so every label is traceable even if processing fails
        var log = new XpsShipWebhookLog
        {
            Endpoint = XpsShipDefaults.EndpointUpdateOrder,
            RemoteIp = guard.Ip,
            RequestBody = FormBody(fields)
        };
        await _dataService.InsertLogAsync(log);

        UpdateResult result;
        try
        {
            result = await _updateProcessor.ProcessAsync(UpdateRequest.Parse(fields), log);
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"XPS Ship: Update Order failed for order {log.CustomOrderNumber ?? "?"}", ex);
            result = new UpdateResult(500, $"error: {ex.Message}");
        }

        log.HttpStatus = result.HttpStatus;
        log.ResponseSummary = result.Summary;
        await _dataService.UpdateLogAsync(log);

        if (result.HttpStatus >= 400)
            await _logger.WarningAsync($"XPS Ship: Update Order for order {log.CustomOrderNumber ?? "?"} answered {result.HttpStatus}: {result.Summary}");

        return result.HttpStatus < 400
            ? Json(result.HttpStatus, new { ok = true, message = result.Summary })
            : Json(result.HttpStatus, new { error = result.Summary });
    }

    #region Utilities

    private async Task<(IActionResult? Result, string Ip)> GuardAsync(string endpoint)
    {
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Response.Headers["Cache-Control"] = "no-store";

        var guard = await _guard.CheckAsync(Request.Headers[XpsShipDefaults.KeyHeader].FirstOrDefault());
        if (guard.Allowed)
            return (null, guard.Ip);

        // A disabled integration is polled every few minutes; logging each of those would bury the real entries
        if (guard.HttpStatus != 503)
            await LogAsync(endpoint, guard.HttpStatus, guard.Ip, message: guard.Message);

        return (Json(guard.HttpStatus, new { error = guard.Message }), guard.Ip);
    }

    private async Task<IActionResult> FailAsync(string endpoint, string ip, Exception ex)
    {
        await _logger.ErrorAsync($"XPS Ship: {endpoint} failed", ex);
        await LogAsync(endpoint, 500, ip, message: ex.Message);
        return Json(500, new { error = "internal error, see the store log" });
    }

    private Task LogAsync(string endpoint, int status, string ip, Guid? orderGuid = null, string? orderNumber = null,
        string? summary = null, string? message = null) =>
        _dataService.InsertLogAsync(new XpsShipWebhookLog
        {
            Endpoint = endpoint,
            HttpStatus = status,
            RemoteIp = ip,
            OrderGuid = orderGuid,
            CustomOrderNumber = orderNumber,
            ResponseSummary = summary,
            Message = message
        });

    private ContentResult Json<T>(int status, T body) => new()
    {
        StatusCode = status,
        ContentType = "application/json; charset=utf-8",
        Content = XpsJson.Serialize(body)
    };

    /// <summary>The post as XPS sent it, one field per line for reading in the log</summary>
    private static string FormBody(IEnumerable<KeyValuePair<string, string>> fields)
    {
        var body = new StringBuilder();
        foreach (var field in fields)
            body.Append(field.Key).Append('=').Append(field.Value).Append('\n');

        return body.ToString();
    }

    #endregion
}
