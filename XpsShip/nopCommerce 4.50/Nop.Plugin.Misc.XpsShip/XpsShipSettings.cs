using Nop.Core.Configuration;

namespace Nop.Plugin.Misc.XpsShip;

public class XpsShipSettings : ISettings
{
    /// <summary>Webhooks answer 503 while this is off</summary>
    public bool Enabled { get; set; }

    /// <summary>Sent by XPS in the x-rsis-key header. Generated on install.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Comma-separated payment method system names whose orders ship before they're paid (agency POs)</summary>
    public string ShipBeforePaymentMethods { get; set; } = "Open Payment";

    public bool HideFullyBackorderedOrders { get; set; } = true;

    /// <summary>Puts "BACKORDERED: " in front of lines with stock at or below 0</summary>
    public bool MarkBackorderedLines { get; set; } = true;

    /// <summary>The line's own bin (attribute combination's, else the product's) in the item title</summary>
    public bool SendItemBins { get; set; } = true;

    /// <summary>The bin of each "Associated to product" component part in the item title</summary>
    public bool SendComponentBins { get; set; } = true;

    /// <summary>
    /// Set the order to Complete once a label leaves nothing to ship, even when unpaid (agency POs).
    /// nopCommerce's own rule only completes paid orders. Sends the "Order completed" emails. Void puts it back to Processing.
    /// </summary>
    public bool CompleteWhenFullyShipped { get; set; } = true;

    /// <summary>Orders with a line whose SKU ends in this are held until released on the admin page</summary>
    public string ExchangeSkuSuffix { get; set; } = "-EXCH";

    /// <summary>Passed to ShipAsync, which queues the "Shipment sent" email when its template is active</summary>
    public bool NotifyCustomerOnShip { get; set; } = true;

    public int LogRetentionDays { get; set; } = 90;

    /// <summary>Orders placed before this are never sent, so turning the plugin on doesn't send years of unshipped orders. Null sends all.</summary>
    public DateTime? OrdersPlacedFromUtc { get; set; }

    /// <summary>Safety cap on how many orders one List Orders call returns</summary>
    public int MaxOrdersPerList { get; set; } = 250;

    /// <summary>
    /// One "customer shipping method = service key" pair per line. The key is what XPS's service mapping
    /// turns into a carrier service; methods not listed get <see cref="DefaultServiceKey"/>.
    /// </summary>
    public string ServiceKeyMap { get; set; } = DefaultServiceKeyMap;

    public string DefaultServiceKey { get; set; } = "FREE";

    /// <summary>
    /// One "customer role = service key" pair per line. When the customer has one of the roles, its key wins over
    /// the shipping method's; the first matching line wins.
    /// </summary>
    public string RoleServiceKeyMap { get; set; } = string.Empty;

    /// <summary>Comma-separated IPs allowed to call the webhooks. Empty allows any.</summary>
    public string IpAllowlist { get; set; } = string.Empty;

    // Sender (ship-from) address sent with every order
    public string SenderName { get; set; } = "FMS Solutions";
    public string SenderCompany { get; set; } = "FMS Solutions";
    public string SenderAddress1 { get; set; } = "566 Falcon Fork Way";
    public string SenderAddress2 { get; set; } = string.Empty;
    public string SenderCity { get; set; } = "Saint Johns";
    public string SenderState { get; set; } = "FL";
    public string SenderZip { get; set; } = "32259";
    public string SenderCountry { get; set; } = "US";
    public string SenderPhone { get; set; } = "2012648365";
    public string SenderEmail { get; set; } = "operations@fmsaccessories.com";

    public const string DefaultServiceKeyMap =
        "Free = FREE\n" +
        "FedEx Priority Overnight = EXP-UPS-NEXTDAY\n" +
        "FedEx Standard Overnight = EXP-UPS-NEXTDAY\n" +
        "FedEx 2Day = EXP-UPS-2DAY\n" +
        "FedEx International Priority = INTL-UPS\n" +
        "FedEx International Economy = INTL-UPS";
}
