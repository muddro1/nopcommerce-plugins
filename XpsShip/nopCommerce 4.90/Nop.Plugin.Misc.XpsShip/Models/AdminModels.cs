using Nop.Core;
using Nop.Plugin.Misc.XpsShip.Domain;
using Nop.Plugin.Misc.XpsShip.Services;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.XpsShip.Models;

public class ConfigurationModel
{
    private const string P = XpsShipDefaults.LocalePrefix + "Settings.";

    [NopResourceDisplayName(P + "Enabled")] public bool Enabled { get; set; }
    [NopResourceDisplayName(P + "ShipBeforePaymentMethods")] public string? ShipBeforePaymentMethods { get; set; }
    [NopResourceDisplayName(P + "HideFullyBackorderedOrders")] public bool HideFullyBackorderedOrders { get; set; }
    [NopResourceDisplayName(P + "MarkBackorderedLines")] public bool MarkBackorderedLines { get; set; }
    [NopResourceDisplayName(P + "CompleteWhenFullyShipped")] public bool CompleteWhenFullyShipped { get; set; }
    [NopResourceDisplayName(P + "ExchangeSkuSuffix")] public string? ExchangeSkuSuffix { get; set; }
    [NopResourceDisplayName(P + "NotifyCustomerOnShip")] public bool NotifyCustomerOnShip { get; set; }
    [NopResourceDisplayName(P + "LogRetentionDays")] public int LogRetentionDays { get; set; }
    [NopResourceDisplayName(P + "OrdersPlacedFrom")] public DateTime? OrdersPlacedFrom { get; set; }
    [NopResourceDisplayName(P + "MaxOrdersPerList")] public int MaxOrdersPerList { get; set; }
    [NopResourceDisplayName(P + "ServiceKeyMap")] public string? ServiceKeyMap { get; set; }
    [NopResourceDisplayName(P + "DefaultServiceKey")] public string? DefaultServiceKey { get; set; }
    [NopResourceDisplayName(P + "RoleServiceKeyMap")] public string? RoleServiceKeyMap { get; set; }
    [NopResourceDisplayName(P + "IpAllowlist")] public string? IpAllowlist { get; set; }

    [NopResourceDisplayName(P + "SenderName")] public string? SenderName { get; set; }
    [NopResourceDisplayName(P + "SenderCompany")] public string? SenderCompany { get; set; }
    [NopResourceDisplayName(P + "SenderAddress1")] public string? SenderAddress1 { get; set; }
    [NopResourceDisplayName(P + "SenderAddress2")] public string? SenderAddress2 { get; set; }
    [NopResourceDisplayName(P + "SenderCity")] public string? SenderCity { get; set; }
    [NopResourceDisplayName(P + "SenderState")] public string? SenderState { get; set; }
    [NopResourceDisplayName(P + "SenderZip")] public string? SenderZip { get; set; }
    [NopResourceDisplayName(P + "SenderCountry")] public string? SenderCountry { get; set; }
    [NopResourceDisplayName(P + "SenderPhone")] public string? SenderPhone { get; set; }
    [NopResourceDisplayName(P + "SenderEmail")] public string? SenderEmail { get; set; }

    // Read-only, for copying into XPS
    public string SecretKey { get; set; } = string.Empty;
    public string ListOrdersUrl { get; set; } = string.Empty;
    public string GetOrderUrl { get; set; } = string.Empty;
    public string UpdateOrderUrl { get; set; } = string.Empty;
    public bool CanEdit { get; set; }
}

public class PreviewModel
{
    public int OrderCount { get; set; }
    public string Json { get; set; } = string.Empty;
    public IList<ExcludedOrder> Excluded { get; set; } = new List<ExcludedOrder>();
}

public abstract class PagedListModel
{
    public int PageIndex { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }

    public void SetPaging<T>(IPagedList<T> list)
    {
        PageIndex = list.PageIndex;
        TotalPages = list.TotalPages;
        TotalCount = list.TotalCount;
    }
}

public class FlagsModel : PagedListModel
{
    public string? Flag { get; set; }
    public string? OrderNumber { get; set; }
    public IList<XpsShipOrderFlag> Rows { get; set; } = new List<XpsShipOrderFlag>();
    public bool CanEdit { get; set; }
}

public class LabelsModel : PagedListModel
{
    public string? OrderNumber { get; set; }
    public IList<XpsShipTrackingMap> Rows { get; set; } = new List<XpsShipTrackingMap>();
    public bool CanEdit { get; set; }
}

public class LogModel : PagedListModel
{
    public string? Endpoint { get; set; }
    public string? OrderNumber { get; set; }
    public bool ErrorsOnly { get; set; }
    public IList<XpsShipWebhookLog> Rows { get; set; } = new List<XpsShipWebhookLog>();
}
