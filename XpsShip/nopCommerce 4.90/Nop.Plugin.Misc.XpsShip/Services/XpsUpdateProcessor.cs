using System.Collections.Concurrent;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Core.Events;
using Nop.Plugin.Misc.XpsShip.Domain;
using Nop.Plugin.Misc.XpsShip.Logic;
using Nop.Services.Catalog;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Shipping;

namespace Nop.Plugin.Misc.XpsShip.Services;

public record UpdateResult(int HttpStatus, string Summary);

/// <summary>
/// Turns one XPS Update Order post into one nopCommerce shipment, created the way the admin Add shipment page
/// creates it and marked shipped with ShipAsync, or into nothing at all if any part of the post is invalid.
/// </summary>
public class XpsUpdateProcessor
{
    // Per-order lock: two posts for one order at once could otherwise both see the same lines as unshipped
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> OrderLocks = new();

    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger _logger;
    private readonly IOrderProcessingService _orderProcessingService;
    private readonly IOrderService _orderService;
    private readonly IProductService _productService;
    private readonly IShipmentService _shipmentService;
    private readonly XpsOrderBuilder _orderBuilder;
    private readonly XpsShipDataService _dataService;
    private readonly XpsShipSettings _settings;

    public XpsUpdateProcessor(IEventPublisher eventPublisher,
        ILogger logger,
        IOrderProcessingService orderProcessingService,
        IOrderService orderService,
        IProductService productService,
        IShipmentService shipmentService,
        XpsOrderBuilder orderBuilder,
        XpsShipDataService dataService,
        XpsShipSettings settings)
    {
        _eventPublisher = eventPublisher;
        _logger = logger;
        _orderProcessingService = orderProcessingService;
        _orderService = orderService;
        _productService = productService;
        _shipmentService = shipmentService;
        _orderBuilder = orderBuilder;
        _dataService = dataService;
        _settings = settings;
    }

    /// <param name="log">The log row already written for this post; the order and outcome are filled in on it</param>
    public async Task<UpdateResult> ProcessAsync(UpdateRequest request, XpsShipWebhookLog log)
    {
        if (!Guid.TryParse(request.OrderId, out var orderGuid))
            return new UpdateResult(404, $"order '{request.OrderId}' not found");

        var order = await _orderService.GetOrderByGuidAsync(orderGuid);
        if (order == null || order.Deleted)
            return new UpdateResult(404, $"order {orderGuid} not found");

        log.OrderGuid = order.OrderGuid;
        log.CustomOrderNumber = order.CustomOrderNumber;

        if (request.Errors.Count > 0)
            return new UpdateResult(422, string.Join("; ", request.Errors));

        var orderLock = OrderLocks.GetOrAdd(order.OrderGuid, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync();
        try
        {
            return await ShipAsync(order, request);
        }
        finally
        {
            orderLock.Release();
        }
    }

    private async Task<UpdateResult> ShipAsync(Order order, UpdateRequest request)
    {
        var primary = request.TrackingNumbers[0];

        // XPS retries and staff re-sending an update must not ship twice. A voided label's number counts too:
        // re-posting it must not bring back a shipment staff deleted.
        if (await _dataService.FindMapAsync(order.OrderGuid, primary) != null
            || (await _shipmentService.GetShipmentsByOrderIdAsync(order.Id))
                .Any(s => string.Equals(s.TrackingNumber?.Trim(), primary, StringComparison.OrdinalIgnoreCase)))
        {
            return new UpdateResult(200, $"duplicate: {primary} is already recorded on order {order.CustomOrderNumber}");
        }

        if (order.PickupInStore)
            return new UpdateResult(422, "the order is pickup in store");

        var lines = await _orderBuilder.LoadLinesAsync(order);
        var plan = ShipmentPlanner.Plan(request.Items,
            lines.Select(l => new ShippableLine(l.Item.OrderItemGuid, l.Item.Id, l.Remaining, l.UnitWeightLb)).ToList());
        if (plan.Errors.Count > 0)
            return new UpdateResult(422, string.Join("; ", plan.Errors));

        foreach (var warning in plan.Warnings)
        {
            await _logger.WarningAsync($"XPS Ship: order {order.CustomOrderNumber}: {warning}");
            await _dataService.InsertLogAsync(new XpsShipWebhookLog
            {
                Endpoint = XpsShipDefaults.EndpointWarning,
                OrderGuid = order.OrderGuid,
                CustomOrderNumber = order.CustomOrderNumber,
                Message = warning
            });
        }

        var carrierName = TrackingNumbers.DisplayName(request.Carrier);
        var itemWeight = plan.ItemWeight;
        var shipment = new Shipment
        {
            OrderId = order.Id,
            TrackingNumber = primary,
            TotalWeight = request.BilledWeightLb ?? (itemWeight > 0 ? itemWeight : null),
            AdminComment = AdminComment(carrierName, request),
            CreatedOnUtc = DateTime.UtcNow
        };
        await _shipmentService.InsertShipmentAsync(shipment);

        foreach (var planned in plan.Items)
        {
            var line = lines.First(l => l.Item.OrderItemGuid == planned.Line.OrderItemGuid);
            await _shipmentService.InsertShipmentItemAsync(new ShipmentItem
            {
                ShipmentId = shipment.Id,
                OrderItemId = line.Item.Id,
                Quantity = planned.Quantity,
                WarehouseId = await WarehouseIdAsync(line.Product)
            });
        }

        await _orderService.InsertOrderNoteAsync(new OrderNote
        {
            OrderId = order.Id,
            Note = $"XPS Ship: shipment added, {carrierName} {primary}",
            DisplayToCustomer = false,
            CreatedOnUtc = DateTime.UtcNow
        });

        await _eventPublisher.PublishAsync(new ShipmentCreatedEvent(shipment));
        await _eventPublisher.PublishAsync(new ShipmentTrackingNumberSetEvent(shipment));

        // Sets ShippedDateUtc and the shipping status, adds nopCommerce's note, queues the "Shipment sent" email
        await _orderProcessingService.ShipAsync(shipment, _settings.NotifyCustomerOnShip);

        if (request.TrackingNumbers.Count > 1)
        {
            // Customer-visible, so every number shows on their order page. Inserted directly rather than through
            // the admin's add-note path, so no "New order note" email goes out on top of the shipped email.
            await _orderService.InsertOrderNoteAsync(new OrderNote
            {
                OrderId = order.Id,
                Note = $"Shipped via {carrierName} in {request.TrackingNumbers.Count} packages: {string.Join(", ", request.TrackingNumbers)}",
                DisplayToCustomer = true,
                CreatedOnUtc = DateTime.UtcNow
            });
        }

        await _dataService.InsertMapAsync(new XpsShipTrackingMap
        {
            OrderGuid = order.OrderGuid,
            CustomOrderNumber = order.CustomOrderNumber,
            PrimaryTrackingNumber = primary,
            Carrier = request.Carrier ?? string.Empty,
            TrackingNumbers = string.Join(",", request.TrackingNumbers),
            PackageCount = request.TrackingNumbers.Count,
            LabelCost = request.LabelCost,
            BilledWeightLb = request.BilledWeightLb,
            Status = TrackingMapStatus.Active,
            CreatedOnUtc = DateTime.UtcNow
        });

        var units = plan.Items.Sum(i => i.Quantity);
        return new UpdateResult(200, $"shipped: shipment {shipment.Id}, {plan.Items.Count} line(s), {units} unit(s), {carrierName} {primary}");
    }

    /// <summary>"XPS: UPS, 2 packages: 1Z..., 1Z...; label $14.82"</summary>
    public static string AdminComment(string carrierName, UpdateRequest request)
    {
        var count = request.TrackingNumbers.Count;
        var comment = $"XPS: {carrierName}, {count} package{(count == 1 ? "" : "s")}: {string.Join(", ", request.TrackingNumbers)}";
        if (request.LabelCost.HasValue)
            comment += $"; label ${XpsFormat.Money(request.LabelCost.Value)}";

        return comment;
    }

    /// <summary>
    /// The warehouse the admin Add shipment page would use. With multiple warehouses the admin asks the user, so
    /// this picks the one with the most stock available.
    /// </summary>
    private async Task<int> WarehouseIdAsync(Product product)
    {
        if (product.ManageInventoryMethod != ManageInventoryMethod.ManageStock || !product.UseMultipleWarehouses)
            return product.WarehouseId;

        var best = (await _productService.GetAllProductWarehouseInventoryRecordsAsync(product.Id))
            .OrderByDescending(w => w.StockQuantity - w.ReservedQuantity)
            .FirstOrDefault();

        return best?.WarehouseId ?? product.WarehouseId;
    }
}
