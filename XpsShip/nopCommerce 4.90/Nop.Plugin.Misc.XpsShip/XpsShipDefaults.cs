namespace Nop.Plugin.Misc.XpsShip;

public static class XpsShipDefaults
{
    public const string SystemName = "Misc.XpsShip";

    // Plugin tables (see Data/NameCompatibility)
    public const string TrackingMapTable = "XpsShip_TrackingMap";
    public const string OrderFlagTable = "XpsShip_OrderFlag";
    public const string WebhookLogTable = "XpsShip_WebhookLog";

    /// <summary>The header XPS puts the secret key in on every webhook call</summary>
    public const string KeyHeader = "x-rsis-key";

    public const int SecretKeyLength = 32;
    public const int RequestBodyMaxLength = 8 * 1024;
    public const int ShipperReferenceMaxLength = 35;

    // Failed-key lockout: more than this many failures from one IP within the window blocks it for BlockMinutes
    public const int FailedKeyLimit = 30;
    public const int FailedKeyWindowMinutes = 10;
    public const int BlockMinutes = 60;

    /// <summary>How far back the admin warnings panel looks</summary>
    public const int WarningsDays = 30;

    // Stock at one of these is the store's placeholder for "unlimited" and counts as in stock
    public static readonly int[] UnlimitedStockPlaceholders = { 9998, 9999, 10000 };

    public const string PurgeTaskName = "XPS Ship: purge webhook log";
    public const string PurgeTaskType = "Nop.Plugin.Misc.XpsShip.Services.PurgeWebhookLogTask, Nop.Plugin.Misc.XpsShip";
    public const int PurgeTaskSeconds = 24 * 60 * 60;

    // Webhook routes, under /xps/ so they don't depend on the theme or custom controllers
    public const string RouteListOrders = "XpsShip.ListOrders";
    public const string RouteGetOrder = "XpsShip.GetOrder";
    public const string RouteUpdateOrder = "XpsShip.UpdateOrder";
    public const string PathListOrders = "xps/orders";
    public const string PathGetOrder = "xps/order";
    public const string PathUpdateOrder = "xps/update";

    // Webhook log endpoint names
    public const string EndpointListOrders = "ListOrders";
    public const string EndpointGetOrder = "GetOrder";
    public const string EndpointUpdateOrder = "UpdateOrder";
    public const string EndpointWarning = "Warning";
    public const string EndpointVoid = "Void";

    // Admin menu
    public const string MenuSales = "XpsShip";
    public const string MenuFms = "FmsPlugins.XpsShip";
    public const string MenuTitle = "XPS Ship";

    public const string ViewsPath = "~/Plugins/Misc.XpsShip/Views";
    public const string LocalePrefix = "Plugins.Misc.XpsShip.";
}
