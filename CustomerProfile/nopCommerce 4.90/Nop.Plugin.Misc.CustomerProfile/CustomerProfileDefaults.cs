namespace Nop.Plugin.Misc.CustomerProfile;

public static class CustomerProfileDefaults
{
    public const string SystemName = "Misc.CustomerProfile";

    public const string ProfileTable = "CustomerBusinessProfile";
    public const string ContactTable = "CustomerContact";
    public const string CertificateTable = "TaxExemptionCertificate";
    public const string Net30OrderTable = "Net30Order";

    public const int DealershipMaxLength = 200;
    public const int ContactNameMaxLength = 200;
    public const int ContactEmailMaxLength = 254;
    public const int ContactTitleMaxLength = 100;
    public const int NoteMaxLength = 1000;
    public const int MaxContactsPerCustomer = 20;

    public const string DailyTaskName = "Customer profile: tax exemption and Net 30 reminders";
    public const string DailyTaskType = "Nop.Plugin.Misc.CustomerProfile.Services.DailyTask, Nop.Plugin.Misc.CustomerProfile";

    /// <summary>The task is idempotent, so this only sets how soon a reminder goes out after it falls due</summary>
    public const int DailyTaskSeconds = 4 * 60 * 60;

    // Form field names posted from widgets that sit inside core forms
    public const string FieldDealership = "CustomerProfile.BmwDealership";
    public const string FieldNet30Enabled = "CustomerProfile.Net30Enabled";
    public const string FieldNet30TermsDays = "CustomerProfile.Net30TermsDays";

    // Message templates
    public const string TemplatePrefix = "CustomerProfile.";
    public const string TplSubmittedOwner = "CustomerProfile.TaxExemption.Submitted.StoreOwnerNotification";
    public const string TplReviewedCustomer = "CustomerProfile.TaxExemption.Reviewed.CustomerNotification";
    public const string TplExpiringCustomer = "CustomerProfile.TaxExemption.Expiring.CustomerNotification";
    public const string TplExpiringOwner = "CustomerProfile.TaxExemption.Expiring.StoreOwnerNotification";
    public const string TplExpiredCustomer = "CustomerProfile.TaxExemption.Expired.CustomerNotification";
    public const string TplExpiredOwner = "CustomerProfile.TaxExemption.Expired.StoreOwnerNotification";
    public const string TplPendingOwner = "CustomerProfile.TaxExemption.PendingReview.StoreOwnerNotification";
    public const string TplOverdueCustomer = "CustomerProfile.Net30.Overdue.CustomerNotification";

    // Storefront routes
    public const string RouteBusinessDetails = "CustomerProfile.BusinessDetails";
    public const string RouteContactEdit = "CustomerProfile.ContactEdit";
    public const string RouteTaxExemption = "CustomerProfile.TaxExemption";
    public const string RouteCertificateFile = "CustomerProfile.CertificateFile";

    // Admin menu entries under "FMS Plugins"
    public const string MenuGroup = "FmsPlugins.CustomerProfile";
    public const string MenuTaxExemptions = "FmsPlugins.CustomerProfile.TaxExemptions";
    public const string MenuNet30Orders = "FmsPlugins.CustomerProfile.Net30Orders";
    public const string MenuSettings = "FmsPlugins.CustomerProfile.Settings";

    public const string LocalePrefix = "Plugins.Misc.CustomerProfile.";
    public const string ViewsPath = "~/Plugins/Misc.CustomerProfile/Views";
    public const string AdminScriptPath = "~/Plugins/Misc.CustomerProfile/Content/customer-profile-admin.js";
}
