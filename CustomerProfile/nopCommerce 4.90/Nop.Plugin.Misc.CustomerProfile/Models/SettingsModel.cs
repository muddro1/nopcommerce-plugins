using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.CustomerProfile.Models;

public record SettingsModel : BaseNopModel
{
    [NopResourceDisplayName(CustomerProfileDefaults.LocalePrefix + "Settings.ReminderDaysBeforeExpiry")]
    public int ReminderDaysBeforeExpiry { get; set; }

    [NopResourceDisplayName(CustomerProfileDefaults.LocalePrefix + "Settings.PendingReviewNudgeDays")]
    public int PendingReviewNudgeDays { get; set; }

    [NopResourceDisplayName(CustomerProfileDefaults.LocalePrefix + "Settings.ApproveMarksTaxExempt")]
    public bool ApproveMarksTaxExempt { get; set; }

    [NopResourceDisplayName(CustomerProfileDefaults.LocalePrefix + "Settings.RemoveExemptionOnExpiry")]
    public bool RemoveExemptionOnExpiry { get; set; }

    [NopResourceDisplayName(CustomerProfileDefaults.LocalePrefix + "Settings.NotificationCcEmails")]
    public string? NotificationCcEmails { get; set; }

    [NopResourceDisplayName(CustomerProfileDefaults.LocalePrefix + "Settings.MaxUploadSizeMb")]
    public int MaxUploadSizeMb { get; set; }

    [NopResourceDisplayName(CustomerProfileDefaults.LocalePrefix + "Settings.Net30DefaultTermsDays")]
    public int Net30DefaultTermsDays { get; set; }

    [NopResourceDisplayName(CustomerProfileDefaults.LocalePrefix + "Settings.Net30OverdueReminderEnabled")]
    public bool Net30OverdueReminderEnabled { get; set; }
}
