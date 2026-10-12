namespace Nop.Plugin.Misc.CustomerProfile.Domain;

/// <summary>Groups of customer emails a secondary contact can subscribe to</summary>
[Flags]
public enum MessageCategory
{
    None = 0,

    /// <summary>Order placed, order processing</summary>
    OrderConfirmations = 1,

    /// <summary>Order paid, order completed, order refunded, Net 30 overdue</summary>
    InvoicesPayments = 2,

    /// <summary>Shipment sent, ready for pickup, delivered</summary>
    Shipping = 4,

    Cancellations = 8,
    OrderNotes = 16,

    /// <summary>Return request submitted, return status changed</summary>
    Returns = 32,

    /// <summary>Certificate reviewed, expiring, expired</summary>
    TaxExemption = 64
}

public static class MessageCategories
{
    public static readonly MessageCategory[] All =
    {
        MessageCategory.OrderConfirmations,
        MessageCategory.InvoicesPayments,
        MessageCategory.Shipping,
        MessageCategory.Cancellations,
        MessageCategory.OrderNotes,
        MessageCategory.Returns,
        MessageCategory.TaxExemption
    };
}
