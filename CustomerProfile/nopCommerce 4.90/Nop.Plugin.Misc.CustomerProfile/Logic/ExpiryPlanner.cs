using Nop.Plugin.Misc.CustomerProfile.Domain;

namespace Nop.Plugin.Misc.CustomerProfile.Logic;

public sealed record CertificateSnapshot(int Id, int CustomerId, CertificateState State, DateTime? ExpiresOn,
    DateTime UploadedOnUtc, DateTime? ReminderSentOnUtc, DateTime? ExpiredNoticeSentOnUtc, DateTime? PendingNudgeSentOnUtc);

public enum ExpiryActionKind
{
    SendReminder,
    Expire,
    NudgePendingReview
}

/// <param name="RemoveExemption">Only for <see cref="ExpiryActionKind.Expire"/>: untick the customer's IsTaxExempt</param>
public sealed record ExpiryAction(ExpiryActionKind Kind, int CertificateId, int CustomerId, bool RemoveExemption = false);

public static class ExpiryPlanner
{
    /// <summary>
    /// Decides what the daily task does with each certificate.
    /// A certificate is valid through the whole of its expiry date, so it expires the day after.
    /// </summary>
    /// <param name="today">Today in the store time zone; any time part is ignored</param>
    public static IReadOnlyList<ExpiryAction> Plan(IReadOnlyCollection<CertificateSnapshot> certificates, DateTime today,
        DateTime utcNow, int reminderDays, int pendingNudgeDays, bool removeExemptionOnExpiry)
    {
        today = today.Date;
        var actions = new List<ExpiryAction>();

        foreach (var cert in certificates)
        {
            switch (cert.State)
            {
                case CertificateState.Approved when cert.ExpiresOn.HasValue:
                    var expires = cert.ExpiresOn.Value.Date;
                    if (expires < today)
                    {
                        actions.Add(new ExpiryAction(ExpiryActionKind.Expire, cert.Id, cert.CustomerId,
                            removeExemptionOnExpiry && !HasOtherValid(certificates, cert, today)));
                    }
                    else if (cert.ReminderSentOnUtc == null && reminderDays > 0 && (expires - today).TotalDays <= reminderDays
                             && !HasLaterValid(certificates, cert))
                    {
                        actions.Add(new ExpiryAction(ExpiryActionKind.SendReminder, cert.Id, cert.CustomerId));
                    }

                    break;

                case CertificateState.PendingReview when pendingNudgeDays > 0 && cert.PendingNudgeSentOnUtc == null
                                                        && (utcNow - cert.UploadedOnUtc).TotalDays >= pendingNudgeDays:
                    actions.Add(new ExpiryAction(ExpiryActionKind.NudgePendingReview, cert.Id, cert.CustomerId));
                    break;
            }
        }

        return actions;
    }

    // The customer keeps exemption if another approved certificate is still valid today
    private static bool HasOtherValid(IEnumerable<CertificateSnapshot> all, CertificateSnapshot cert, DateTime today) => all.Any(c =>
        c.Id != cert.Id && c.CustomerId == cert.CustomerId && c.State == CertificateState.Approved
        && c.ExpiresOn.HasValue && c.ExpiresOn.Value.Date >= today);

    // No reminder if the customer already has an approved certificate expiring later than this one
    private static bool HasLaterValid(IEnumerable<CertificateSnapshot> all, CertificateSnapshot cert) => all.Any(c =>
        c.Id != cert.Id && c.CustomerId == cert.CustomerId && c.State == CertificateState.Approved
        && c.ExpiresOn.HasValue && c.ExpiresOn.Value.Date > cert.ExpiresOn!.Value.Date);
}
