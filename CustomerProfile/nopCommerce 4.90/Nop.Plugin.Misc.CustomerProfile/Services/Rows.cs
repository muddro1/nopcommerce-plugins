using Nop.Plugin.Misc.CustomerProfile.Domain;

namespace Nop.Plugin.Misc.CustomerProfile.Services;

/// <summary>One line of the admin Tax exemptions grid</summary>
public record CertificateRow(TaxExemptionCertificate Certificate, string? CustomerEmail, string? CustomerName,
    string? Company, string? Dealership, bool IsTaxExempt);

/// <summary>One line of the admin Net 30 orders grid</summary>
public record Net30Row(Net30Order Net30, string CustomOrderNumber, decimal OrderTotal, DateTime OrderCreatedOnUtc,
    string? CustomerEmail, string? CustomerName, string? Company, string? Dealership);

/// <summary>Certificate grid filter. "Expiring" means approved and expiring within the reminder window.</summary>
public enum CertificateFilter
{
    PendingReview,
    Approved,
    Expiring,
    Expired,
    Rejected,
    All
}
