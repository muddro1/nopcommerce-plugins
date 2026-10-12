using FluentAssertions;
using Nop.Plugin.Misc.CustomerProfile.Domain;
using Nop.Plugin.Misc.CustomerProfile.Logic;
using NUnit.Framework;

namespace Nop.Plugin.Misc.CustomerProfile.Tests;

public class ExpiryPlannerTests
{
    private static readonly DateTime Today = new(2026, 10, 12);
    private static readonly DateTime UtcNow = new(2026, 10, 12, 15, 0, 0, DateTimeKind.Utc);

    private static CertificateSnapshot Approved(int id, DateTime? expires, int customerId = 1, DateTime? reminderSent = null) =>
        new(id, customerId, CertificateState.Approved, expires, UtcNow.AddDays(-200), reminderSent, null, null);

    private static CertificateSnapshot Pending(int id, double daysAgo, DateTime? nudgeSent = null) =>
        new(id, 1, CertificateState.PendingReview, null, UtcNow.AddDays(-daysAgo), null, null, nudgeSent);

    private static IReadOnlyList<ExpiryAction> Plan(params CertificateSnapshot[] certs) =>
        ExpiryPlanner.Plan(certs, Today, UtcNow, reminderDays: 30, pendingNudgeDays: 3, removeExemptionOnExpiry: true);

    [Test]
    public void Expiring_today_is_still_valid_and_gets_a_reminder()
    {
        Plan(Approved(1, Today)).Should().Equal(new ExpiryAction(ExpiryActionKind.SendReminder, 1, 1));
    }

    [Test]
    public void Expired_yesterday_is_expired_and_removes_exemption()
    {
        Plan(Approved(1, Today.AddDays(-1))).Should().Equal(new ExpiryAction(ExpiryActionKind.Expire, 1, 1, true));
    }

    [Test]
    public void Reminder_window_is_inclusive_of_the_last_day()
    {
        Plan(Approved(1, Today.AddDays(30))).Should().ContainSingle(a => a.Kind == ExpiryActionKind.SendReminder);
        Plan(Approved(1, Today.AddDays(31))).Should().BeEmpty();
    }

    [Test]
    public void Reminder_is_sent_once()
    {
        Plan(Approved(1, Today.AddDays(10), reminderSent: UtcNow.AddDays(-5))).Should().BeEmpty();
    }

    [Test]
    public void Zero_reminder_days_disables_reminders()
    {
        ExpiryPlanner.Plan(new[] { Approved(1, Today.AddDays(1)) }, Today, UtcNow, 0, 3, true).Should().BeEmpty();
    }

    [Test]
    public void Expired_with_another_valid_certificate_keeps_exemption()
    {
        Plan(Approved(1, Today.AddDays(-1)), Approved(2, Today.AddDays(300)))
            .Should().Contain(new ExpiryAction(ExpiryActionKind.Expire, 1, 1, false));
    }

    [Test]
    public void Another_customers_valid_certificate_does_not_count()
    {
        Plan(Approved(1, Today.AddDays(-1)), Approved(2, Today.AddDays(300), customerId: 2))
            .Should().Contain(new ExpiryAction(ExpiryActionKind.Expire, 1, 1, true));
    }

    [Test]
    public void Setting_off_expires_without_removing_exemption()
    {
        ExpiryPlanner.Plan(new[] { Approved(1, Today.AddDays(-1)) }, Today, UtcNow, 30, 3, removeExemptionOnExpiry: false)
            .Should().Equal(new ExpiryAction(ExpiryActionKind.Expire, 1, 1, false));
    }

    [Test]
    public void No_reminder_when_a_later_approved_certificate_exists()
    {
        Plan(Approved(1, Today.AddDays(10)), Approved(2, Today.AddDays(400))).Should().BeEmpty();
    }

    [Test]
    public void Approved_without_expiry_date_is_left_alone()
    {
        Plan(Approved(1, null)).Should().BeEmpty();
    }

    [Test]
    public void Pending_review_is_nudged_after_the_configured_days()
    {
        Plan(Pending(1, 3)).Should().Equal(new ExpiryAction(ExpiryActionKind.NudgePendingReview, 1, 1));
        Plan(Pending(1, 2)).Should().BeEmpty();
        Plan(Pending(1, 10, nudgeSent: UtcNow.AddDays(-1))).Should().BeEmpty();
    }

    [Test]
    public void Zero_nudge_days_disables_nudges()
    {
        ExpiryPlanner.Plan(new[] { Pending(1, 30) }, Today, UtcNow, 30, 0, true).Should().BeEmpty();
    }

    [TestCase(CertificateState.Rejected)]
    [TestCase(CertificateState.Superseded)]
    public void Closed_states_are_ignored(CertificateState state)
    {
        Plan(new CertificateSnapshot(1, 1, state, Today.AddDays(-5), UtcNow.AddDays(-100), null, null, null)).Should().BeEmpty();
    }

    [Test]
    public void Expired_with_notice_sent_is_ignored()
    {
        Plan(new CertificateSnapshot(1, 1, CertificateState.Expired, Today.AddDays(-5), UtcNow.AddDays(-100), null, UtcNow.AddDays(-4), null))
            .Should().BeEmpty();
    }

    [Test]
    public void Expired_whose_notice_never_went_out_is_resent()
    {
        // ExpireAsync saved the state, then the email failed: the notice must not be lost
        Plan(new CertificateSnapshot(1, 1, CertificateState.Expired, Today.AddDays(-5), UtcNow.AddDays(-100), null, null, null))
            .Should().Equal(new ExpiryAction(ExpiryActionKind.SendExpiredNotice, 1, 1));
    }

    [Test]
    public void Time_of_day_on_expiry_date_is_ignored()
    {
        Plan(Approved(1, Today.AddHours(1))).Should().ContainSingle(a => a.Kind == ExpiryActionKind.SendReminder);
    }
}
