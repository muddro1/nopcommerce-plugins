namespace Nop.Plugin.Misc.XpsShip.Logic;

public enum StockState
{
    /// <summary>Inventory isn't tracked for the product</summary>
    NotTracked,
    InStock,

    /// <summary>Tracked, with stock at or below 0</summary>
    Backordered
}

/// <summary>One ship-enabled order line, as the selection rules see it</summary>
public record LineSnapshot(Guid OrderItemGuid, string? Sku, int Remaining, StockState Stock);

/// <summary>What the selection rules need to know about an order. Built by the order builder from nopCommerce entities.</summary>
public record OrderSnapshot(
    int OrderStatusId,
    int ShippingStatusId,
    int PaymentStatusId,
    string? PaymentMethodSystemName,
    bool PickupInStore,
    bool HasShippingAddress,
    string Instructions,
    IReadOnlyList<LineSnapshot> Lines,
    string? HoldReason,
    bool IsHeld,
    bool HoldCleared,
    bool ExchangeReleased);

public record RuleSettings(IReadOnlyList<string> ShipBeforePaymentMethods, bool HideFullyBackordered, string ExchangeSkuSuffix);

public record RuleDecision(bool Include, string? Reason, bool AutoHold)
{
    public static readonly RuleDecision Included = new(true, null, false);
    public static RuleDecision Excluded(string reason) => new(false, reason, false);
}

/// <summary>The spec's order selection rules 1-7, in order</summary>
public static class OrderRules
{
    // nopCommerce enum values (OrderStatus, ShippingStatus, PaymentStatus)
    public const int OrderPending = 10;
    public const int OrderProcessing = 20;
    public const int NotYetShipped = 20;
    public const int PartiallyShipped = 25;
    public const int Shipped = 30;
    public const int Paid = 30;

    public static RuleDecision Evaluate(OrderSnapshot order, RuleSettings settings)
    {
        if (order.OrderStatusId != OrderPending && order.OrderStatusId != OrderProcessing)
            return RuleDecision.Excluded("order status");

        if (order.ShippingStatusId != NotYetShipped && order.ShippingStatusId != PartiallyShipped)
            return RuleDecision.Excluded("shipping status");

        if (order.PickupInStore)
            return RuleDecision.Excluded("pickup in store");

        if (!order.HasShippingAddress)
            return RuleDecision.Excluded("no shipping address");

        if (order.PaymentStatusId != Paid && !ShipsBeforePayment(order.PaymentMethodSystemName, settings.ShipBeforePaymentMethods))
            return RuleDecision.Excluded($"unpaid ({order.PaymentMethodSystemName})");

        var remaining = order.Lines.Where(l => l.Remaining > 0).ToList();
        if (remaining.Count == 0)
            return RuleDecision.Excluded("nothing left to ship");

        if (order.IsHeld)
            return RuleDecision.Excluded(string.IsNullOrEmpty(order.HoldReason) ? "on hold" : $"on hold: {order.HoldReason}");

        if (!order.HoldCleared && Instructions.SaysDoNotShip(order.Instructions))
            return new RuleDecision(false, "on hold: special instruction says do not ship", true);

        if (!order.ExchangeReleased && IsExchange(order.Lines, settings.ExchangeSkuSuffix))
            return RuleDecision.Excluded("exchange order, not released");

        if (settings.HideFullyBackordered && remaining.All(l => l.Stock == StockState.Backordered))
            return RuleDecision.Excluded("backordered");

        return RuleDecision.Included;
    }

    public static bool IsExchange(IEnumerable<LineSnapshot> lines, string suffix) =>
        !string.IsNullOrWhiteSpace(suffix)
        && lines.Any(l => l.Sku?.Trim().EndsWith(suffix.Trim(), StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>
    /// Whether the order's payment method is in the ship-before-payment list. Matches the system name ignoring case
    /// and spaces, with or without nopCommerce's "Payments." prefix, so "Open Payment" matches "Payments.OpenPayment".
    /// </summary>
    public static bool ShipsBeforePayment(string? paymentMethodSystemName, IReadOnlyList<string> methods)
    {
        if (string.IsNullOrWhiteSpace(paymentMethodSystemName))
            return false;

        var name = Squash(paymentMethodSystemName);
        var bare = name.StartsWith("payments.") ? name.Substring("payments.".Length) : name;

        return methods.Select(Squash).Any(m => m == name || m == bare);
    }

    private static string Squash(string value) => new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();

    /// <summary>Stock at 9998, 9999 or 10000 is the store's placeholder for unlimited</summary>
    public static StockState Classify(bool tracked, int stock)
    {
        if (!tracked)
            return StockState.NotTracked;
        if (XpsShipDefaults.UnlimitedStockPlaceholders.Contains(stock))
            return StockState.InStock;

        return stock <= 0 ? StockState.Backordered : StockState.InStock;
    }

    /// <summary>
    /// The order's shipping status after a shipment is deleted. nopCommerce's own Delete shipment leaves it unchanged,
    /// which would keep a voided order out of List Orders.
    /// </summary>
    public static int ShippingStatusAfterVoid(int currentShippingStatusId, bool otherShippedShipmentsRemain)
    {
        if (currentShippingStatusId != Shipped && currentShippingStatusId != PartiallyShipped)
            return currentShippingStatusId;

        return otherShippedShipmentsRemain ? PartiallyShipped : NotYetShipped;
    }
}
