namespace Nop.Plugin.Misc.XpsShip;

/// <summary>Every locale resource the plugin uses. Registered on install and on every update.</summary>
public static class Locales
{
    private const string P = XpsShipDefaults.LocalePrefix + "Settings.";

    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        [P + "Enabled"] = "Enabled",
        [P + "Enabled.Hint"] = "While off, the webhooks answer 503 and XPS sees no orders. Check the Preview before switching on.",
        [P + "ShipBeforePaymentMethods"] = "Ship before payment",
        [P + "ShipBeforePaymentMethods.Hint"] = "Payment methods whose orders go to XPS before they are paid (agency POs), comma-separated. Matched on the payment method's system name, ignoring case, spaces and the \"Payments.\" prefix. The Preview shows the system name of every unpaid order it leaves out.",
        [P + "HideFullyBackorderedOrders"] = "Hide fully backordered orders",
        [P + "HideFullyBackorderedOrders.Hint"] = "Leave out orders where every line still to ship is a tracked product with stock at or below 0. Lines on other orders are still sent, titled BACKORDERED.",
        [P + "ExchangeSkuSuffix"] = "Exchange SKU suffix",
        [P + "ExchangeSkuSuffix.Hint"] = "Orders with a line whose SKU ends in this are held until released on the Hold & release tab.",
        [P + "NotifyCustomerOnShip"] = "Notify customer on ship",
        [P + "NotifyCustomerOnShip.Hint"] = "Queue nopCommerce's \"Shipment sent\" email when a label posts. It only goes out if that message template is active.",
        [P + "LogRetentionDays"] = "Keep log (days)",
        [P + "LogRetentionDays.Hint"] = "Webhook log rows hold customer addresses and are deleted after this many days.",
        [P + "OrdersPlacedFrom"] = "Only orders placed on or after",
        [P + "OrdersPlacedFrom.Hint"] = "Older orders are never sent to XPS and are not listed in the Preview. Leave empty to send every open order.",
        [P + "MaxOrdersPerList"] = "Most orders per list",
        [P + "MaxOrdersPerList.Hint"] = "Safety cap on how many orders one List Orders call returns, oldest first.",
        [P + "ServiceKeyMap"] = "Service keys",
        [P + "ServiceKeyMap.Hint"] = "One \"customer shipping method = service key\" per line. Map each key to a carrier service once in XPS's service mapping.",
        [P + "DefaultServiceKey"] = "Default service key",
        [P + "DefaultServiceKey.Hint"] = "Sent for shipping methods not listed above.",
        [P + "IpAllowlist"] = "IP allowlist",
        [P + "IpAllowlist.Hint"] = "XPS's outbound IP addresses, comma-separated. Empty allows any IP that has the key.",
        [P + "SenderName"] = "Name",
        [P + "SenderCompany"] = "Company",
        [P + "SenderAddress1"] = "Address 1",
        [P + "SenderAddress2"] = "Address 2",
        [P + "SenderCity"] = "City",
        [P + "SenderState"] = "State (2 letters)",
        [P + "SenderZip"] = "Zip",
        [P + "SenderCountry"] = "Country (2 letters)",
        [P + "SenderPhone"] = "Phone",
        [P + "SenderEmail"] = "Email"
    };
}
