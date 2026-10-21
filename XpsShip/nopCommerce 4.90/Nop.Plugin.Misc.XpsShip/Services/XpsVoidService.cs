using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Data;
using Nop.Plugin.Misc.XpsShip.Domain;
using Nop.Plugin.Misc.XpsShip.Logic;
using Nop.Services.Catalog;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Shipping;

namespace Nop.Plugin.Misc.XpsShip.Services;

/// <summary>
/// Undoes a label voided in XPS. XPS doesn't tell the store about voids, so staff click Void on the label's row.
/// </summary>
public class XpsVoidService
{
    private readonly ILocalizationService _localizationService;
    private readonly IOrderProcessingService _orderProcessingService;
    private readonly IOrderService _orderService;
    private readonly IProductService _productService;
    private readonly IShipmentService _shipmentService;
    private readonly IRepository<QueuedEmail> _queuedEmailRepository;
    private readonly XpsShipDataService _dataService;

    public XpsVoidService(ILocalizationService localizationService,
        IOrderProcessingService orderProcessingService,
        IOrderService orderService,
        IProductService productService,
        IShipmentService shipmentService,
        IRepository<QueuedEmail> queuedEmailRepository,
        XpsShipDataService dataService)
    {
        _localizationService = localizationService;
        _orderProcessingService = orderProcessingService;
        _orderService = orderService;
        _productService = productService;
        _shipmentService = shipmentService;
        _queuedEmailRepository = queuedEmailRepository;
        _dataService = dataService;
    }

    /// <summary>Deletes the label's shipment and marks its row voided. Returns an error message, or null on success.</summary>
    public async Task<string?> VoidAsync(int trackingMapId, string voidedBy)
    {
        var map = await _dataService.GetMapByIdAsync(trackingMapId);
        if (map == null)
            return "Label not found.";
        if (map.Status == TrackingMapStatus.Voided)
            return "This label is already voided.";

        var order = await _orderService.GetOrderByGuidAsync(map.OrderGuid);
        if (order == null)
            return $"Order {map.CustomOrderNumber} not found.";

        var shipment = (await _shipmentService.GetShipmentsByOrderIdAsync(order.Id))
            .FirstOrDefault(s => string.Equals(s.TrackingNumber?.Trim(), map.PrimaryTrackingNumber, StringComparison.OrdinalIgnoreCase));

        // A shipment already deleted by hand still lets the row be voided, so the order can be re-shipped cleanly
        if (shipment != null)
            await DeleteShipmentAsync(shipment);

        map.Status = TrackingMapStatus.Voided;
        map.VoidedOnUtc = DateTime.UtcNow;
        await _dataService.UpdateMapAsync(map);

        var wasComplete = order.OrderStatusId == OrderRules.OrderComplete;
        await RestoreShippingStatusAsync(order);
        var emailsDeleted = await DeleteUnsentShippedEmailsAsync(map);
        // Only when this void reopened the order: a Complete order that stays Complete keeps its email
        var completedEmailsDeleted = wasComplete && order.OrderStatusId != OrderRules.OrderComplete
            ? await DeleteUnsentCompletedEmailsAsync(order.Id)
            : 0;

        await _orderService.InsertOrderNoteAsync(new OrderNote
        {
            OrderId = order.Id,
            Note = $"XPS label {map.PrimaryTrackingNumber} voided by {voidedBy}" + (shipment == null ? " (its shipment was already deleted)" : "")
                + (emailsDeleted > 0 ? $". Deleted {emailsDeleted} unsent shipped email(s)." : "")
                + (completedEmailsDeleted > 0 ? $". Deleted {completedEmailsDeleted} unsent order completed email(s)." : ""),
            DisplayToCustomer = false,
            CreatedOnUtc = DateTime.UtcNow
        });

        await _dataService.InsertLogAsync(new XpsShipWebhookLog
        {
            Endpoint = XpsShipDefaults.EndpointVoid,
            HttpStatus = 200,
            OrderGuid = order.OrderGuid,
            CustomOrderNumber = order.CustomOrderNumber,
            Message = $"Label {map.PrimaryTrackingNumber} voided by {voidedBy}"
        });

        return null;
    }

    /// <summary>
    /// Deletes the label's shipped email while it still waits in the queue (the template's send delay).
    /// Found by the tracking number in the body, so the template must include %Shipment.TrackingNumber% or %Shipment.XpsTrackingUrl%.
    /// </summary>
    private async Task<int> DeleteUnsentShippedEmailsAsync(XpsShipTrackingMap map)
    {
        var tracking = map.PrimaryTrackingNumber;
        if (string.IsNullOrWhiteSpace(tracking))
            return 0;

        var postedFromUtc = map.CreatedOnUtc.AddMinutes(-5);
        var emails = await _queuedEmailRepository.Table
            .Where(e => e.SentOnUtc == null && e.CreatedOnUtc >= postedFromUtc && e.Body.Contains(tracking))
            .ToListAsync();

        foreach (var email in emails)
            await _queuedEmailRepository.DeleteAsync(email, false);

        return emails.Count;
    }

    /// <summary>
    /// Deletes the order's "Order completed" emails still waiting in the queue (the template's send delay), found by
    /// the queued email ids in the order notes. An email already sent stays sent.
    /// </summary>
    private async Task<int> DeleteUnsentCompletedEmailsAsync(int orderId)
    {
        var ids = OrderRules.CompletedEmailIds((await _orderService.GetOrderNotesByOrderIdAsync(orderId)).Select(n => n.Note));
        if (ids.Count == 0)
            return 0;

        var emails = await _queuedEmailRepository.Table.Where(e => e.SentOnUtc == null && ids.Contains(e.Id)).ToListAsync();
        foreach (var email in emails)
            await _queuedEmailRepository.DeleteAsync(email, false);

        return emails.Count;
    }

    /// <summary>What the admin Delete shipment button does: give back booked inventory, then delete</summary>
    private async Task DeleteShipmentAsync(Shipment shipment)
    {
        foreach (var shipmentItem in await _shipmentService.GetShipmentItemsByShipmentIdAsync(shipment.Id))
        {
            var orderItem = await _orderService.GetOrderItemByIdAsync(shipmentItem.OrderItemId);
            if (orderItem == null)
                continue;

            var product = await _productService.GetProductByIdAsync(orderItem.ProductId);
            if (product == null)
                continue;

            await _productService.ReverseBookedInventoryAsync(product, shipmentItem,
                string.Format(await _localizationService.GetResourceAsync("Admin.StockQuantityHistory.Messages.DeleteShipment"), shipment.OrderId));
        }

        await _shipmentService.DeleteShipmentAsync(shipment);
    }

    /// <summary>
    /// The admin Delete shipment leaves the order's shipping status alone, so a fully shipped order would stay
    /// Shipped and never come back to List Orders. Put it back to Partially shipped or Not yet shipped.
    /// </summary>
    private async Task RestoreShippingStatusAsync(Order order)
    {
        var otherShipped = (await _shipmentService.GetShipmentsByOrderIdAsync(order.Id, shipped: true)).Any();
        var status = OrderRules.ShippingStatusAfterVoid(order.ShippingStatusId, otherShipped);
        if (status == order.ShippingStatusId)
            return;

        order.ShippingStatusId = status;

        // CheckOrderStatusAsync only moves an order forward. If shipping completed it, step it back to
        // Processing so it reappears in List Orders (rule 1 takes only Pending and Processing).
        if (order.OrderStatus == OrderStatus.Complete)
            order.OrderStatus = OrderStatus.Processing;

        await _orderService.UpdateOrderAsync(order);
        await _orderProcessingService.CheckOrderStatusAsync(order);
    }
}
