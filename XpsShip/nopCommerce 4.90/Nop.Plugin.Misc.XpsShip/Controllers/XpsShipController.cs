using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.XpsShip.Domain;
using Nop.Plugin.Misc.XpsShip.Logic;
using Nop.Plugin.Misc.XpsShip.Models;
using Nop.Plugin.Misc.XpsShip.Services;
using Nop.Services.Configuration;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.XpsShip.Controllers;

/// <summary>The XPS Ship admin page: settings, preview, hold and release, labels, log</summary>
[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class XpsShipController : BasePluginController
{
    private const int PageSize = 50;

    private readonly IDateTimeHelper _dateTimeHelper;
    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly IOrderService _orderService;
    private readonly IPermissionService _permissionService;
    private readonly ISettingService _settingService;
    private readonly IWebHelper _webHelper;
    private readonly IWorkContext _workContext;
    private readonly XpsOrderBuilder _orderBuilder;
    private readonly XpsShipDataService _dataService;
    private readonly XpsVoidService _voidService;

    public XpsShipController(IDateTimeHelper dateTimeHelper,
        ILocalizationService localizationService,
        INotificationService notificationService,
        IOrderService orderService,
        IPermissionService permissionService,
        ISettingService settingService,
        IWebHelper webHelper,
        IWorkContext workContext,
        XpsOrderBuilder orderBuilder,
        XpsShipDataService dataService,
        XpsVoidService voidService)
    {
        _dateTimeHelper = dateTimeHelper;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _orderService = orderService;
        _permissionService = permissionService;
        _settingService = settingService;
        _webHelper = webHelper;
        _workContext = workContext;
        _orderBuilder = orderBuilder;
        _dataService = dataService;
        _voidService = voidService;
    }

    #region Access

    // Vendors never see this page: it lists every customer's order
    private async Task<bool> CanViewAsync() =>
        await _workContext.GetCurrentVendorAsync() == null && await _permissionService.AuthorizeAsync(StandardPermission.Orders.ORDERS_VIEW);

    private async Task<bool> CanEditAsync() =>
        await _workContext.GetCurrentVendorAsync() == null && await _permissionService.AuthorizeAsync(StandardPermission.Orders.ORDERS_CREATE_EDIT_DELETE);

    private async Task<string> CurrentUserAsync() => (await _workContext.GetCurrentCustomerAsync()).Email ?? "admin";

    private static string ViewPath(string name) => $"{XpsShipDefaults.ViewsPath}/Admin/{name}.cshtml";

    #endregion

    #region Settings

    [HttpGet]
    public async Task<IActionResult> Configure()
    {
        if (!await CanViewAsync())
            return AccessDeniedView();

        var settings = await _settingService.LoadSettingAsync<XpsShipSettings>();
        return View(ViewPath("Configure"), await PrepareConfigurationModelAsync(settings));
    }

    [HttpPost]
    public async Task<IActionResult> Configure(ConfigurationModel model)
    {
        if (!await CanEditAsync())
            return AccessDeniedView();

        var settings = await _settingService.LoadSettingAsync<XpsShipSettings>();
        settings.Enabled = model.Enabled;
        settings.ShipBeforePaymentMethods = model.ShipBeforePaymentMethods?.Trim() ?? string.Empty;
        settings.HideFullyBackorderedOrders = model.HideFullyBackorderedOrders;
        settings.MarkBackorderedLines = model.MarkBackorderedLines;
        settings.CompleteWhenFullyShipped = model.CompleteWhenFullyShipped;
        settings.ExchangeSkuSuffix = model.ExchangeSkuSuffix?.Trim() ?? string.Empty;
        settings.NotifyCustomerOnShip = model.NotifyCustomerOnShip;
        settings.LogRetentionDays = Math.Max(model.LogRetentionDays, 1);
        // The date picker gives midnight in the admin's time zone
        settings.OrdersPlacedFromUtc = model.OrdersPlacedFrom.HasValue
            ? _dateTimeHelper.ConvertToUtcTime(model.OrdersPlacedFrom.Value.Date, await _dateTimeHelper.GetCurrentTimeZoneAsync())
            : null;
        settings.MaxOrdersPerList = Math.Max(model.MaxOrdersPerList, 1);
        settings.ServiceKeyMap = (model.ServiceKeyMap ?? string.Empty).Replace("\r", string.Empty).Trim();
        settings.DefaultServiceKey = model.DefaultServiceKey?.Trim() ?? string.Empty;
        settings.RoleServiceKeyMap = (model.RoleServiceKeyMap ?? string.Empty).Replace("\r", string.Empty).Trim();
        settings.IpAllowlist = model.IpAllowlist?.Trim() ?? string.Empty;
        settings.SenderName = model.SenderName?.Trim() ?? string.Empty;
        settings.SenderCompany = model.SenderCompany?.Trim() ?? string.Empty;
        settings.SenderAddress1 = model.SenderAddress1?.Trim() ?? string.Empty;
        settings.SenderAddress2 = model.SenderAddress2?.Trim() ?? string.Empty;
        settings.SenderCity = model.SenderCity?.Trim() ?? string.Empty;
        settings.SenderState = model.SenderState?.Trim().ToUpperInvariant() ?? string.Empty;
        settings.SenderZip = model.SenderZip?.Trim() ?? string.Empty;
        settings.SenderCountry = model.SenderCountry?.Trim().ToUpperInvariant() ?? string.Empty;
        settings.SenderPhone = model.SenderPhone?.Trim() ?? string.Empty;
        settings.SenderEmail = model.SenderEmail?.Trim() ?? string.Empty;

        await _settingService.SaveSettingAsync(settings);
        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

        return RedirectToAction(nameof(Configure));
    }

    [HttpPost]
    public async Task<IActionResult> RegenerateKey()
    {
        if (!await CanEditAsync())
            return AccessDeniedView();

        var settings = await _settingService.LoadSettingAsync<XpsShipSettings>();
        settings.SecretKey = XpsFormat.NewSecretKey();
        await _settingService.SaveSettingAsync(settings, s => s.SecretKey, clearCache: true);

        _notificationService.WarningNotification("New key generated. XPS gets 401 until the new key is entered in its integration settings.");
        return RedirectToAction(nameof(Configure));
    }

    private async Task<ConfigurationModel> PrepareConfigurationModelAsync(XpsShipSettings settings)
    {
        // Always https: the webhooks refuse plain http
        var store = _webHelper.GetStoreLocation(true);

        return new ConfigurationModel
        {
            Enabled = settings.Enabled,
            ShipBeforePaymentMethods = settings.ShipBeforePaymentMethods,
            HideFullyBackorderedOrders = settings.HideFullyBackorderedOrders,
            MarkBackorderedLines = settings.MarkBackorderedLines,
            CompleteWhenFullyShipped = settings.CompleteWhenFullyShipped,
            ExchangeSkuSuffix = settings.ExchangeSkuSuffix,
            NotifyCustomerOnShip = settings.NotifyCustomerOnShip,
            LogRetentionDays = settings.LogRetentionDays,
            OrdersPlacedFrom = settings.OrdersPlacedFromUtc.HasValue
                ? await _dateTimeHelper.ConvertToUserTimeAsync(settings.OrdersPlacedFromUtc.Value, DateTimeKind.Utc)
                : null,
            MaxOrdersPerList = settings.MaxOrdersPerList,
            ServiceKeyMap = settings.ServiceKeyMap,
            DefaultServiceKey = settings.DefaultServiceKey,
            RoleServiceKeyMap = settings.RoleServiceKeyMap,
            IpAllowlist = settings.IpAllowlist,
            SenderName = settings.SenderName,
            SenderCompany = settings.SenderCompany,
            SenderAddress1 = settings.SenderAddress1,
            SenderAddress2 = settings.SenderAddress2,
            SenderCity = settings.SenderCity,
            SenderState = settings.SenderState,
            SenderZip = settings.SenderZip,
            SenderCountry = settings.SenderCountry,
            SenderPhone = settings.SenderPhone,
            SenderEmail = settings.SenderEmail,
            SecretKey = settings.SecretKey,
            ListOrdersUrl = store + XpsShipDefaults.PathListOrders,
            GetOrderUrl = store + XpsShipDefaults.PathGetOrder,
            UpdateOrderUrl = store + XpsShipDefaults.PathUpdateOrder,
            CanEdit = await CanEditAsync()
        };
    }

    #endregion

    #region Preview

    /// <summary>Exactly what List Orders would hand XPS right now, plus the open orders it leaves out and why</summary>
    [HttpGet]
    public async Task<IActionResult> Preview()
    {
        if (!await CanViewAsync())
            return AccessDeniedView();

        var list = await _orderBuilder.BuildListAsync();
        return View(ViewPath("Preview"), new PreviewModel
        {
            OrderCount = list.Orders.Count,
            Json = XpsJson.Serialize(new XpsOrderListResponse { Orders = list.Orders }, indented: true),
            Excluded = list.Excluded
        });
    }

    #endregion

    #region Hold and release

    [HttpGet]
    public async Task<IActionResult> Flags(string? flag, string? orderNumber, int page = 1)
    {
        if (!await CanViewAsync())
            return AccessDeniedView();

        var rows = await _dataService.SearchFlagsAsync(flag, orderNumber, Math.Max(page, 1) - 1, PageSize);
        var model = new FlagsModel { Flag = flag, OrderNumber = orderNumber, Rows = rows, CanEdit = await CanEditAsync() };
        model.SetPaging(rows);

        return View(ViewPath("Flags"), model);
    }

    /// <summary>Holds an order, or releases an exchange order to ship</summary>
    [HttpPost]
    public async Task<IActionResult> AddFlag(string? orderNumber, string? flag, string? reason)
    {
        if (!await CanEditAsync())
            return AccessDeniedView();

        if (flag != OrderFlags.Hold && flag != OrderFlags.ReleaseExchange)
            return RedirectToAction(nameof(Flags));

        var order = string.IsNullOrWhiteSpace(orderNumber) ? null : await _orderService.GetOrderByCustomOrderNumberAsync(orderNumber.Trim());
        if (order == null || order.Deleted)
        {
            _notificationService.ErrorNotification($"Order {orderNumber} not found.");
            return RedirectToAction(nameof(Flags));
        }

        if (await _dataService.GetFlagAsync(order.OrderGuid, flag) != null)
        {
            _notificationService.WarningNotification($"Order {order.CustomOrderNumber} already has that entry.");
            return RedirectToAction(nameof(Flags));
        }

        await _dataService.InsertFlagAsync(new XpsShipOrderFlag
        {
            OrderGuid = order.OrderGuid,
            CustomOrderNumber = order.CustomOrderNumber,
            Flag = flag,
            Reason = reason?.Trim(),
            CreatedBy = await CurrentUserAsync(),
            CreatedOnUtc = DateTime.UtcNow
        });

        _notificationService.SuccessNotification(flag == OrderFlags.Hold
            ? $"Order {order.CustomOrderNumber} is held: XPS won't see it until it is taken off hold."
            : $"Exchange order {order.CustomOrderNumber} is released and goes to XPS on its next poll.");

        return RedirectToAction(nameof(Flags));
    }

    [HttpPost]
    public async Task<IActionResult> RemoveFlag(int id)
    {
        if (!await CanEditAsync())
            return AccessDeniedView();

        var flag = await _dataService.GetFlagByIdAsync(id);
        if (flag == null)
            return RedirectToAction(nameof(Flags));

        await _dataService.DeleteFlagAsync(flag);

        // Remember the decision, so the do-not-ship rule doesn't put the order straight back on hold
        if (flag.Flag == OrderFlags.Hold && await _dataService.GetFlagAsync(flag.OrderGuid, OrderFlags.HoldCleared) == null)
        {
            await _dataService.InsertFlagAsync(new XpsShipOrderFlag
            {
                OrderGuid = flag.OrderGuid,
                CustomOrderNumber = flag.CustomOrderNumber,
                Flag = OrderFlags.HoldCleared,
                Reason = flag.Reason,
                CreatedBy = await CurrentUserAsync(),
                CreatedOnUtc = DateTime.UtcNow
            });
        }

        _notificationService.SuccessNotification($"Removed {flag.Flag} for order {flag.CustomOrderNumber}.");
        return RedirectToAction(nameof(Flags));
    }

    #endregion

    #region Labels

    [HttpGet]
    public async Task<IActionResult> Labels(string? orderNumber, int page = 1)
    {
        if (!await CanViewAsync())
            return AccessDeniedView();

        var rows = await _dataService.SearchMapsAsync(orderNumber, Math.Max(page, 1) - 1, PageSize);
        var model = new LabelsModel { OrderNumber = orderNumber, Rows = rows, CanEdit = await CanEditAsync() };
        model.SetPaging(rows);

        return View(ViewPath("Labels"), model);
    }

    /// <summary>After voiding a label in XPS: delete its shipment so the lines come back to List Orders</summary>
    [HttpPost]
    public async Task<IActionResult> Void(int id, string? orderNumber)
    {
        if (!await CanEditAsync())
            return AccessDeniedView();

        var error = await _voidService.VoidAsync(id, await CurrentUserAsync());
        if (error == null)
            _notificationService.SuccessNotification("Shipment deleted and the items are back in List Orders. The label is NOT voided in XPS: void it there if you haven't.");
        else
            _notificationService.ErrorNotification(error);

        return RedirectToAction(nameof(Labels), new { orderNumber });
    }

    #endregion

    #region Log

    [HttpGet]
    public async Task<IActionResult> Log(string? endpoint, string? orderNumber, bool errorsOnly = false, int page = 1)
    {
        if (!await CanViewAsync())
            return AccessDeniedView();

        var rows = await _dataService.SearchLogAsync(endpoint, orderNumber, errorsOnly, Math.Max(page, 1) - 1, PageSize);
        var model = new LogModel { Endpoint = endpoint, OrderNumber = orderNumber, ErrorsOnly = errorsOnly, Rows = rows };
        model.SetPaging(rows);

        return View(ViewPath("Log"), model);
    }

    #endregion
}
