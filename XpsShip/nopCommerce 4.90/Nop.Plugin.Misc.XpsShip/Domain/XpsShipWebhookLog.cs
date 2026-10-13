using Nop.Core;

namespace Nop.Plugin.Misc.XpsShip.Domain;

/// <summary>Every webhook call, plus catalog warnings and voids. Purged after LogRetentionDays.</summary>
public class XpsShipWebhookLog : BaseEntity
{
    /// <summary>One of the XpsShipDefaults.Endpoint* names</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>0 for rows that aren't a request (warnings)</summary>
    public int HttpStatus { get; set; }

    public Guid? OrderGuid { get; set; }
    public string? CustomOrderNumber { get; set; }
    public string? RemoteIp { get; set; }

    /// <summary>The form body of an Update Order post, capped at 8 KB. Never holds the key, which travels in a header.</summary>
    public string? RequestBody { get; set; }

    public string? ResponseSummary { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedOnUtc { get; set; }
}
