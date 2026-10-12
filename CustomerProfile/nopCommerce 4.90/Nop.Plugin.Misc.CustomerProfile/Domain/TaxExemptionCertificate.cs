using Nop.Core;

namespace Nop.Plugin.Misc.CustomerProfile.Domain;

/// <summary>One uploaded tax exemption form. Rows are kept as history, never overwritten.</summary>
public class TaxExemptionCertificate : BaseEntity
{
    public int CustomerId { get; set; }

    /// <summary>The file, in nopCommerce's Download table</summary>
    public int DownloadId { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public int StateId { get; set; }

    /// <summary>Date only; valid through the end of this day in the store time zone</summary>
    public DateTime? ExpiresOn { get; set; }

    public string? CertificateNumber { get; set; }
    public string? IssuingState { get; set; }
    public int UploadedByCustomerId { get; set; }
    public DateTime UploadedOnUtc { get; set; }
    public int? ReviewedByCustomerId { get; set; }
    public DateTime? ReviewedOnUtc { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime? ReminderSentOnUtc { get; set; }
    public DateTime? ExpiredNoticeSentOnUtc { get; set; }
    public DateTime? PendingNudgeSentOnUtc { get; set; }

    public CertificateState State
    {
        get => (CertificateState)StateId;
        set => StateId = (int)value;
    }
}
