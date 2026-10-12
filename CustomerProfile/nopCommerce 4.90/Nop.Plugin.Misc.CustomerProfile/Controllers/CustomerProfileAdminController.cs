using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.CustomerProfile.Logic;
using Nop.Plugin.Misc.CustomerProfile.Models;
using Nop.Plugin.Misc.CustomerProfile.Services;
using Nop.Services.Customers;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.CustomerProfile.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public partial class CustomerProfileAdminController : BasePluginController
{
    private const string ConfigureView = CustomerProfileDefaults.ViewsPath + "/Admin/Configure.cshtml";

    private readonly ContactEditor _contactEditor;
    private readonly IBusinessProfileService _profileService;
    private readonly ICustomerContactService _contactService;
    private readonly ICustomerProfileDataService _dataService;
    private readonly ICustomerService _customerService;
    private readonly ILocalizationService _localizationService;
    private readonly INet30Service _net30Service;
    private readonly INotificationService _notificationService;
    private readonly IPermissionService _permissionService;
    private readonly ISettingService _settingService;
    private readonly IStoreClock _clock;
    private readonly ITaxExemptionService _taxExemptionService;
    private readonly IWorkContext _workContext;

    public CustomerProfileAdminController(ContactEditor contactEditor,
        IBusinessProfileService profileService,
        ICustomerContactService contactService,
        ICustomerProfileDataService dataService,
        ICustomerService customerService,
        ILocalizationService localizationService,
        INet30Service net30Service,
        INotificationService notificationService,
        IPermissionService permissionService,
        ISettingService settingService,
        IStoreClock clock,
        ITaxExemptionService taxExemptionService,
        IWorkContext workContext)
    {
        _contactEditor = contactEditor;
        _profileService = profileService;
        _contactService = contactService;
        _dataService = dataService;
        _customerService = customerService;
        _localizationService = localizationService;
        _net30Service = net30Service;
        _notificationService = notificationService;
        _permissionService = permissionService;
        _settingService = settingService;
        _clock = clock;
        _taxExemptionService = taxExemptionService;
        _workContext = workContext;
    }

    #region Access

    private async Task<bool> IsVendorAsync() => await _workContext.GetCurrentVendorAsync() != null;

    private async Task<bool> CanViewCustomersAsync() =>
        !await IsVendorAsync() && await _permissionService.AuthorizeAsync(StandardPermission.Customers.CUSTOMERS_VIEW);

    private async Task<bool> CanEditCustomersAsync() =>
        !await IsVendorAsync() && await _permissionService.AuthorizeAsync(StandardPermission.Customers.CUSTOMERS_CREATE_EDIT_DELETE);

    private async Task<bool> CanViewOrdersAsync() =>
        !await IsVendorAsync() && await _permissionService.AuthorizeAsync(StandardPermission.Orders.ORDERS_VIEW);

    private async Task<bool> CanConfigureAsync() =>
        !await IsVendorAsync() && await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS);

    private Task<string> TAsync(string key) => _localizationService.GetResourceAsync(CustomerProfileDefaults.LocalePrefix + key);

    #endregion

    #region Settings

    [HttpGet]
    public async Task<IActionResult> Configure()
    {
        if (!await CanConfigureAsync())
            return AccessDeniedView();

        var settings = await _settingService.LoadSettingAsync<CustomerProfileSettings>();
        return View(ConfigureView, new SettingsModel
        {
            ReminderDaysBeforeExpiry = settings.ReminderDaysBeforeExpiry,
            PendingReviewNudgeDays = settings.PendingReviewNudgeDays,
            ApproveMarksTaxExempt = settings.ApproveMarksTaxExempt,
            RemoveExemptionOnExpiry = settings.RemoveExemptionOnExpiry,
            NotificationCcEmails = settings.NotificationCcEmails,
            MaxUploadSizeMb = settings.MaxUploadSizeMb,
            Net30DefaultTermsDays = settings.Net30DefaultTermsDays,
            Net30OverdueReminderEnabled = settings.Net30OverdueReminderEnabled
        });
    }

    [HttpPost]
    public async Task<IActionResult> Configure(SettingsModel model)
    {
        if (!await CanConfigureAsync())
            return AccessDeniedView();

        async Task RangeAsync(string field, int value, int min, int max)
        {
            if (value < min || value > max)
                ModelState.AddModelError(field, string.Format(await TAsync("Settings.Error.Range"), await TAsync("Settings." + field), min, max));
        }

        await RangeAsync(nameof(model.ReminderDaysBeforeExpiry), model.ReminderDaysBeforeExpiry, 0, 365);
        await RangeAsync(nameof(model.PendingReviewNudgeDays), model.PendingReviewNudgeDays, 0, 60);
        await RangeAsync(nameof(model.MaxUploadSizeMb), model.MaxUploadSizeMb, 1, 25);
        await RangeAsync(nameof(model.Net30DefaultTermsDays), model.Net30DefaultTermsDays, 1, 365);

        foreach (var address in EmailList.Split(model.NotificationCcEmails).Where(a => !CommonHelper.IsValidEmail(a)))
            ModelState.AddModelError(nameof(model.NotificationCcEmails), string.Format(await TAsync("Settings.Error.CcInvalid"), address));

        if (!ModelState.IsValid)
            return View(ConfigureView, model);

        var settings = await _settingService.LoadSettingAsync<CustomerProfileSettings>();
        settings.ReminderDaysBeforeExpiry = model.ReminderDaysBeforeExpiry;
        settings.PendingReviewNudgeDays = model.PendingReviewNudgeDays;
        settings.ApproveMarksTaxExempt = model.ApproveMarksTaxExempt;
        settings.RemoveExemptionOnExpiry = model.RemoveExemptionOnExpiry;
        settings.NotificationCcEmails = string.Join(", ", EmailList.Split(model.NotificationCcEmails));
        settings.MaxUploadSizeMb = model.MaxUploadSizeMb;
        settings.Net30DefaultTermsDays = model.Net30DefaultTermsDays;
        settings.Net30OverdueReminderEnabled = model.Net30OverdueReminderEnabled;
        await _settingService.SaveSettingAsync(settings);

        _notificationService.SuccessNotification(await TAsync("Settings.Saved"));
        return RedirectToAction(nameof(Configure));
    }

    #endregion
}
