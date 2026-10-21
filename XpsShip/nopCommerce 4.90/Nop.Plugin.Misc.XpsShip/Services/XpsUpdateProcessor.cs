using System.Collections.Concurrent;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Core.Events;
using Nop.Plugin.Misc.XpsShip.Domain;
using Nop.Plugin.Misc.XpsShip.Logic;
using Nop.Core.Domain.Localization;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
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
    /// <summary>Starts every XPS shipment's admin comment; marks shipments this plugin created</summary>
    public const string AdminCommentPrefix = "XPS:";

    // Per-order lock: two posts for one order at once could otherwise both see the same lines as unshipped
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> OrderLocks = new();

    private readonly IEventPublisher _eventPublisher;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger _logger;
    private readonly IOrderProcessingService _orderProcessingService;
    private readonly IOrderService _orderService;
    private readonly IProductService _productService;
    private readonly IPdfService _pdfService;
    private readonly IShipmentService _shipmentService;
    private readonly IWorkflowMessageService _workflowMessageService;
    private readonly LocalizationSettings _localizationSettings;
    private readonly OrderSettings _orderSettings;
    private readonly XpsOrderBuilder _orderBuilder;
    private readonly XpsShipDataService _dataService;
    private readonly XpsShipSettings _settings;

    public XpsUpdateProcessor(IEventPublisher eventPublisher,
        ILocalizationService localizationService,
        ILogger logger,
        IOrderProcessingService orderProcessingService,
        IOrderService orderService,
        IProductService productService,
        IPdfService pdfService,
        IShipmentService shipmentService,
        IWorkflowMessageService workflowMessageService,
        LocalizationSettings localizationSettings,
        OrderSettings orderSettings,
        XpsOrderBuilder orderBuilder,
        XpsShipDataService dataService,
        XpsShipSettings settings)
    {
        _eventPublisher = eventPublisher;
        _localizationService = localizationService;
        _logger = logger;
        _orderProcessingService = orderProcessingService;
        _orderService = orderService;
        _productService = productService;
        _pdfService = pdfService;
        _shipmentService = shipmentService;
        _workflowMessageService = workflowMessageService;
        _localizationSettings = localizationSettings;
        _orderSettings = orderSettings;
        _orderBuilder = orderBuilder;
        _dataService = dataService;
        _settings = settings;
    }

    /// <param name="log">The log row already written for this post; the order and outcome are filled in on it</param>
    public async Task<UpdateResult> ProcessAsync(UpdateRequest request, XpsShipWebhookLog log)
    {
        if (!XpsFormat.TryParseOrderId(request.OrderId, out var orderGuid))
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
        var map = await _dataService.FindMapAsync(order.OrderGuid, primary);
        var existing = (await _shipmentService.GetShipmentsByOrderIdAsync(order.Id))
            .FirstOrDefault(s => string.Equals(s.TrackingNumber?.Trim(), primary, StringComparison.OrdinalIgnoreCase));

        // XPS retries and staff re-sending an update must not ship twice
        if (existing?.ShippedDateUtc != null)
        {
            // An earlier attempt that stopped after shipping can leave no label row; add it so the label can be voided
            if (map == null)
                await _dataService.InsertMapAsync(NewMap(order, request));

            return new UpdateResult(200, $"duplicate: {primary} is already shipped on order {order.CustomOrderNumber}");
        }

        // Re-posting a voided label's number must not bring back a shipment staff deleted
        if (map?.Status == TrackingMapStatus.Voided)
            return new UpdateResult(200, $"duplicate: {primary} was voided on order {order.CustomOrderNumber}");

        if (existing != null)
        {
            // An unshipped shipment with this number is left over from a post that failed part way, so start again.
            // One staff made by hand is theirs to ship or delete.
            if (existing.AdminComment?.StartsWith(AdminCommentPrefix, StringComparison.Ordinal) != true)
                return new UpdateResult(422, $"order {order.CustomOrderNumber} already has an unshipped shipment with tracking number {primary}; ship or delete it in admin");

            await _shipmentService.DeleteShipmentAsync(existing);
        }

        // An active label row whose shipment never shipped is from the same kind of failed post
        if (map != null)
            await _dataService.DeleteMapAsync(map);

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

        // There's no transaction across these steps: nopCommerce opens a connection per call, and one scope over
        // several connections can escalate to a distributed transaction. Instead, anything that fails before the
        // shipment is marked shipped is undone, so XPS's retry starts clean.
        XpsShipTrackingMap? newMap = null;
        try
        {
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

            // Before ShipAsync: the shipped email's %Shipment.XpsTrackingUrl% reads the carrier from this row
            newMap = NewMap(order, request);
            await _dataService.InsertMapAsync(newMap);

            await _eventPublisher.PublishAsync(new ShipmentCreatedEvent(shipment));
            await _eventPublisher.PublishAsync(new ShipmentTrackingNumberSetEvent(shipment));

            // Sets ShippedDateUtc and the shipping status, adds nopCommerce's note, queues the "Shipment sent" email
            await _orderProcessingService.ShipAsync(shipment, _settings.NotifyCustomerOnShip);
        }
        catch (Exception ex)
        {
            if (shipment.ShippedDateUtc == null)
            {
                await UndoAsync(order, shipment, newMap);
                throw;
            }

            // ShipAsync failed after marking the shipment shipped: it stays, and a retry is answered as a duplicate.
            // What ShipAsync didn't get to (shipping status, email, its note) needs a look in admin.
            await _logger.ErrorAsync($"XPS Ship: order {order.CustomOrderNumber}: shipment {shipment.Id} ({primary}) is marked shipped, " +
                "but nopCommerce's ship step failed after that. Check the order's shipping status and the shipped email.", ex);
            throw;
        }

        // The shipment is shipped; a failed note must not make XPS retry it
        try
        {
            await _orderService.InsertOrderNoteAsync(new OrderNote
            {
                OrderId = order.Id,
                Note = $"XPS Ship: shipment {shipment.Id} added, {carrierName} {primary}",
                DisplayToCustomer = false,
                CreatedOnUtc = DateTime.UtcNow
            });

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
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"XPS Ship: order {order.CustomOrderNumber}: shipment {shipment.Id} shipped, but its order notes could not be added", ex);
        }

        if (_settings.CompleteWhenFullyShipped)
        {
            try
            {
                await CompleteIfFullyShippedAsync(order.Id);
            }
            catch (Exception ex)
            {
                await _logger.ErrorAsync($"XPS Ship: order {order.CustomOrderNumber}: shipment {shipment.Id} shipped, but the order could not be set to Complete", ex);
            }
        }

        var units = plan.Items.Sum(i => i.Quantity);
        return new UpdateResult(200, $"shipped: shipment {shipment.Id}, {plan.Items.Count} line(s), {units} unit(s), {carrierName} {primary}");
    }

    /// <summary>
    /// nopCommerce completes a fully shipped order only once it's paid, so agency POs stayed Processing. Its
    /// SetOrderStatusAsync isn't public, so this does what it does on Complete: the status-changed event, then the
    /// "Order completed" emails to the customer and store owner. Marking the order paid later sends nothing more,
    /// since nopCommerce skips Complete orders. Reward points and gift card activation on Complete are not repeated.
    /// </summary>
    private async Task CompleteIfFullyShippedAsync(int orderId)
    {
        // Reloaded: ShipAsync updated its own copy of the order
        var order = await _orderService.GetOrderByIdAsync(orderId);
        if (order == null || !OrderRules.CompleteAfterShip(order.OrderStatusId, order.ShippingStatusId))
            return;

        var previousStatus = order.OrderStatus;
        order.OrderStatusId = OrderRules.OrderComplete;
        await _orderService.UpdateOrderAsync(order);

        // As SetOrderStatusAsync: a handler may change the status back, and then nothing is sent
        await _eventPublisher.PublishAsync(new OrderStatusChangedEvent(order, previousStatus));
        if (order.OrderStatusId != OrderRules.OrderComplete)
            return;

        await AddNoteAsync(order.Id, "XPS Ship: fully shipped, order status set to Complete");

        // Resolved from DI, so a plugin overriding these emails (CustomerProfile's contact copies) still applies
        var attachmentPath = _orderSettings.AttachPdfInvoiceToOrderCompletedEmail ? await _pdfService.SaveOrderPdfToDiskAsync(order) : null;
        var attachmentName = _orderSettings.AttachPdfInvoiceToOrderCompletedEmail
            ? string.Format(await _localizationService.GetResourceAsync("PDFInvoice.FileName"), order.CustomOrderNumber) + ".pdf"
            : null;
        var emailIds = await _workflowMessageService.SendOrderCompletedCustomerNotificationAsync(order, order.CustomerLanguageId,
            attachmentPath, attachmentName);
        if (emailIds.Any())
            await AddNoteAsync(order.Id, $"\"Order completed\" email (to customer) has been queued. Queued email identifiers: {string.Join(", ", emailIds)}.");

        // nopCommerce doesn't note this one; the plugin does, so Void can find it while it's still queued
        var ownerEmailIds = await _workflowMessageService.SendOrderCompletedStoreOwnerNotificationAsync(order, _localizationSettings.DefaultAdminLanguageId);
        if (ownerEmailIds.Any())
            await AddNoteAsync(order.Id, $"\"Order completed\" email (to store owner) has been queued. Queued email identifiers: {string.Join(", ", ownerEmailIds)}.");
    }

    private Task AddNoteAsync(int orderId, string note) => _orderService.InsertOrderNoteAsync(new OrderNote
    {
        OrderId = orderId,
        Note = note,
        DisplayToCustomer = false,
        CreatedOnUtc = DateTime.UtcNow
    });

    private static XpsShipTrackingMap NewMap(Order order, UpdateRequest request) => new()
    {
        OrderGuid = order.OrderGuid,
        CustomOrderNumber = order.CustomOrderNumber,
        PrimaryTrackingNumber = request.TrackingNumbers[0],
        Carrier = request.Carrier ?? string.Empty,
        TrackingNumbers = string.Join(",", request.TrackingNumbers),
        PackageCount = request.TrackingNumbers.Count,
        LabelCost = request.LabelCost,
        ShipToAddress = request.ShipToAddress,
        BilledWeightLb = request.BilledWeightLb,
        Status = TrackingMapStatus.Active,
        CreatedOnUtc = DateTime.UtcNow
    };

    /// <summary>Removes what a failed post created. Deleting the shipment removes its items too (cascade).</summary>
    private async Task UndoAsync(Order order, Shipment shipment, XpsShipTrackingMap? map)
    {
        try
        {
            if (map is { Id: > 0 })
                await _dataService.DeleteMapAsync(map);
            if (shipment.Id > 0)
                await _shipmentService.DeleteShipmentAsync(shipment);
        }
        catch (Exception ex)
        {
            // The retry still recovers: it deletes an unshipped XPS shipment and a stale label row before starting again
            await _logger.ErrorAsync($"XPS Ship: order {order.CustomOrderNumber}: could not undo a failed shipment", ex);
        }
    }

    /// <summary>"XPS: UPS, 2 packages: 1Z..., 1Z...; label $14.82"</summary>
    public static string AdminComment(string carrierName, UpdateRequest request)
    {
        var count = request.TrackingNumbers.Count;
        var comment = $"{AdminCommentPrefix} {carrierName}, {count} package{(count == 1 ? "" : "s")}: {string.Join(", ", request.TrackingNumbers)}";
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
