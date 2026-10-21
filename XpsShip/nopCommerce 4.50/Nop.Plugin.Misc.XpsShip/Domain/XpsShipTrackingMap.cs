using Nop.Core;

namespace Nop.Plugin.Misc.XpsShip.Domain;

/// <summary>
/// One row per label XPS posts back. OrderGuid + PrimaryTrackingNumber is the key back to the nopCommerce shipment,
/// which has no GUID of its own; numeric ids are never stored so the rows survive a move to a new install.
/// </summary>
public class XpsShipTrackingMap : BaseEntity
{
    public Guid OrderGuid { get; set; }
    public string CustomOrderNumber { get; set; } = string.Empty;

    /// <summary>Matches Shipment.TrackingNumber</summary>
    public string PrimaryTrackingNumber { get; set; } = string.Empty;

    /// <summary>ups, usps, fedex or dhl</summary>
    public string Carrier { get; set; } = string.Empty;

    /// <summary>Every package's number, comma-separated, master first</summary>
    public string TrackingNumbers { get; set; } = string.Empty;

    public int PackageCount { get; set; }
    public decimal? LabelCost { get; set; }
    public decimal? BilledWeightLb { get; set; }
    public int StatusId { get; set; }
    public DateTime? VoidedOnUtc { get; set; }
    public DateTime CreatedOnUtc { get; set; }

    /// <summary>The address XPS printed on the label, one part per line; null on labels from before 1.02/2.02</summary>
    public string? ShipToAddress { get; set; }

    public TrackingMapStatus Status
    {
        get => (TrackingMapStatus)StatusId;
        set => StatusId = (int)value;
    }
}

public enum TrackingMapStatus
{
    Active = 0,
    Voided = 1
}
