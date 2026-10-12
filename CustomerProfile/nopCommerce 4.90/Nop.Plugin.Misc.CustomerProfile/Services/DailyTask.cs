using Nop.Plugin.Misc.CustomerProfile.Logic;
using Nop.Services.Logging;
using Nop.Services.ScheduleTasks;

namespace Nop.Plugin.Misc.CustomerProfile.Services;

/// <summary>
/// Certificate reminders, expiry and pending-review nudges, then Net 30 overdue reminders.
/// Runs every few hours; every action is stamped, so running again does nothing new.
/// </summary>
public class DailyTask : IScheduleTask
{
    private readonly CustomerProfileSettings _settings;
    private readonly ILogger _logger;
    private readonly INet30Service _net30Service;
    private readonly IStoreClock _clock;
    private readonly ITaxExemptionService _taxExemptionService;

    public DailyTask(CustomerProfileSettings settings,
        ILogger logger,
        INet30Service net30Service,
        IStoreClock clock,
        ITaxExemptionService taxExemptionService)
    {
        _settings = settings;
        _logger = logger;
        _net30Service = net30Service;
        _clock = clock;
        _taxExemptionService = taxExemptionService;
    }

    public async Task ExecuteAsync()
    {
        var certificates = (await _taxExemptionService.GetForPlanningAsync())
            .Select(c => new CertificateSnapshot(c.Id, c.CustomerId, c.State, c.ExpiresOn, c.UploadedOnUtc,
                c.ReminderSentOnUtc, c.ExpiredNoticeSentOnUtc, c.PendingNudgeSentOnUtc))
            .ToList();

        var actions = ExpiryPlanner.Plan(certificates, _clock.Today, _clock.UtcNow, _settings.ReminderDaysBeforeExpiry,
            _settings.PendingReviewNudgeDays, _settings.RemoveExemptionOnExpiry);

        foreach (var action in actions)
        {
            try
            {
                switch (action.Kind)
                {
                    case ExpiryActionKind.SendReminder:
                        await _taxExemptionService.SendReminderAsync(action.CertificateId);
                        break;
                    case ExpiryActionKind.Expire:
                        await _taxExemptionService.ExpireAsync(action.CertificateId, action.RemoveExemption);
                        break;
                    case ExpiryActionKind.SendExpiredNotice:
                        await _taxExemptionService.SendExpiredNoticeAsync(action.CertificateId);
                        break;
                    case ExpiryActionKind.NudgePendingReview:
                        await _taxExemptionService.SendPendingNudgeAsync(action.CertificateId);
                        break;
                }
            }
            catch (Exception ex)
            {
                // One bad row mustn't stop the rest; it isn't stamped, so the next run retries it
                await _logger.ErrorAsync($"Customer profile: {action.Kind} failed for tax exemption certificate {action.CertificateId}", ex);
            }
        }

        var orders = await _net30Service.GetUnpaidSnapshotsAsync();
        foreach (var orderId in OverduePlanner.OrdersToRemind(orders, _clock.Today, _settings.Net30OverdueReminderEnabled))
        {
            try
            {
                await _net30Service.SendOverdueAsync(orderId);
            }
            catch (Exception ex)
            {
                await _logger.ErrorAsync($"Customer profile: Net 30 overdue reminder failed for order {orderId}", ex);
            }
        }
    }
}
