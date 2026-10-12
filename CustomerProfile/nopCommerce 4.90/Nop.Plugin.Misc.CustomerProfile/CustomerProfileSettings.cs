using Nop.Core.Configuration;

namespace Nop.Plugin.Misc.CustomerProfile;

public class CustomerProfileSettings : ISettings
{
    public int ReminderDaysBeforeExpiry { get; set; } = 30;
    public int PendingReviewNudgeDays { get; set; } = 3;
    public bool ApproveMarksTaxExempt { get; set; } = true;
    public bool RemoveExemptionOnExpiry { get; set; } = true;

    /// <summary>Comma- or semicolon-separated; copied on every store-owner notification from this plugin</summary>
    public string NotificationCcEmails { get; set; } = string.Empty;

    public int MaxUploadSizeMb { get; set; } = 10;
    public int Net30DefaultTermsDays { get; set; } = 30;
    public bool Net30OverdueReminderEnabled { get; set; }
}
