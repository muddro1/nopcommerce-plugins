using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Core.Domain.Cms;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Plugin.Misc.CustomerProfile.Components;
using Nop.Plugin.Misc.CustomerProfile.Services;
using Nop.Services.Cms;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;
using Nop.Web.Framework.Infrastructure;

namespace Nop.Plugin.Misc.CustomerProfile;

/// <summary>
/// A widget plugin (admin customer card, My Account pages, registration field, order due date, expiry banner)
/// and a payment method (Net 30, visible only to customers it is switched on for).
/// </summary>
public class CustomerProfilePlugin : BasePlugin, IWidgetPlugin, IPaymentMethod
{
    private readonly CustomerProfileSettings _settings;
    private readonly IBusinessProfileService _profileService;
    private readonly ICustomerService _customerService;
    private readonly ILocalizationService _localizationService;
    private readonly IScheduleTaskService _scheduleTaskService;
    private readonly ISettingService _settingService;
    private readonly IWebHelper _webHelper;
    private readonly IWorkContext _workContext;
    private readonly MessageTemplateInstaller _templateInstaller;
    private readonly PaymentSettings _paymentSettings;
    private readonly WidgetSettings _widgetSettings;

    public CustomerProfilePlugin(CustomerProfileSettings settings,
        IBusinessProfileService profileService,
        ICustomerService customerService,
        ILocalizationService localizationService,
        IScheduleTaskService scheduleTaskService,
        ISettingService settingService,
        IWebHelper webHelper,
        IWorkContext workContext,
        MessageTemplateInstaller templateInstaller,
        PaymentSettings paymentSettings,
        WidgetSettings widgetSettings)
    {
        _settings = settings;
        _profileService = profileService;
        _customerService = customerService;
        _localizationService = localizationService;
        _scheduleTaskService = scheduleTaskService;
        _settingService = settingService;
        _webHelper = webHelper;
        _workContext = workContext;
        _templateInstaller = templateInstaller;
        _paymentSettings = paymentSettings;
        _widgetSettings = widgetSettings;
    }

    #region Plugin

    public override string GetConfigurationPageUrl() =>
        _webHelper.GetStoreLocation() + "Admin/CustomerProfileAdmin/Configure";

    public override async Task InstallAsync()
    {
        await _settingService.SaveSettingAsync(new CustomerProfileSettings());
        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>(Locales.All));
        await _templateInstaller.InstallAsync();

        if (await _scheduleTaskService.GetTaskByTypeAsync(CustomerProfileDefaults.DailyTaskType) == null)
        {
            await _scheduleTaskService.InsertTaskAsync(new ScheduleTask
            {
                Name = CustomerProfileDefaults.DailyTaskName,
                Type = CustomerProfileDefaults.DailyTaskType,
                Seconds = CustomerProfileDefaults.DailyTaskSeconds,
                Enabled = true,
                LastEnabledUtc = DateTime.UtcNow,
                StopOnError = false
            });
        }

        // Both need activating. Net 30 stays invisible at checkout until a customer is switched on.
        if (!_widgetSettings.ActiveWidgetSystemNames.Contains(CustomerProfileDefaults.SystemName))
        {
            _widgetSettings.ActiveWidgetSystemNames.Add(CustomerProfileDefaults.SystemName);
            await _settingService.SaveSettingAsync(_widgetSettings);
        }

        if (!_paymentSettings.ActivePaymentMethodSystemNames.Contains(CustomerProfileDefaults.SystemName))
        {
            _paymentSettings.ActivePaymentMethodSystemNames.Add(CustomerProfileDefaults.SystemName);
            await _settingService.SaveSettingAsync(_paymentSettings);
        }

        await base.InstallAsync();
    }

    public override async Task UpdateAsync(string currentVersion, string targetVersion)
    {
        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>(Locales.All));
        await base.UpdateAsync(currentVersion, targetVersion);
    }

    public override async Task UninstallAsync()
    {
        // The plugin's tables and certificate files are kept (SchemaMigration.Down) so a reinstall picks them up again.
        // Settings > "Delete all plugin data" is the way to remove them.
        var task = await _scheduleTaskService.GetTaskByTypeAsync(CustomerProfileDefaults.DailyTaskType);
        if (task != null)
            await _scheduleTaskService.DeleteTaskAsync(task);

        await _templateInstaller.UninstallAsync();
        await _settingService.DeleteSettingAsync<CustomerProfileSettings>();
        await _localizationService.DeleteLocaleResourcesAsync(CustomerProfileDefaults.LocalePrefix);
        await _localizationService.DeleteLocaleResourceAsync($"Plugins.FriendlyName.{CustomerProfileDefaults.SystemName}");

        if (_widgetSettings.ActiveWidgetSystemNames.Remove(CustomerProfileDefaults.SystemName))
            await _settingService.SaveSettingAsync(_widgetSettings);
        if (_paymentSettings.ActivePaymentMethodSystemNames.Remove(CustomerProfileDefaults.SystemName))
            await _settingService.SaveSettingAsync(_paymentSettings);

        await base.UninstallAsync();
    }

    #endregion

    #region Widgets

    public bool HideInWidgetList => false;

    public Task<IList<string>> GetWidgetZonesAsync() => Task.FromResult<IList<string>>(new List<string>
    {
        AdminWidgetZones.CustomerDetailsBlock,
        PublicWidgetZones.AccountNavigationAfter,
        PublicWidgetZones.AccountNavigationBefore,
        PublicWidgetZones.RegisterBottom,
        PublicWidgetZones.OrderDetailsPageOverview
    });

    // Widget zone names are static properties, not constants, so they can't be switch cases
    public Type GetWidgetViewComponent(string widgetZone)
    {
        if (widgetZone == AdminWidgetZones.CustomerDetailsBlock)
            return typeof(AdminCustomerCardViewComponent);
        if (widgetZone == PublicWidgetZones.AccountNavigationAfter)
            return typeof(AccountNavigationViewComponent);
        if (widgetZone == PublicWidgetZones.AccountNavigationBefore)
            return typeof(ExpiryBannerViewComponent);
        if (widgetZone == PublicWidgetZones.RegisterBottom)
            return typeof(RegisterDealershipViewComponent);

        return typeof(OrderDueDateViewComponent);
    }

    #endregion

    #region Net 30 payment method

    public async Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (!await _customerService.IsRegisteredAsync(customer))
            return true;

        var profile = await _profileService.GetAsync(customer.Id);
        return profile is not { Net30Enabled: true };
    }

    public async Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
    {
        // Checkout doesn't re-run HidePaymentMethodAsync when the order is placed, so check again here
        var profile = await _profileService.GetAsync(processPaymentRequest.CustomerId);
        if (profile is not { Net30Enabled: true })
        {
            var result = new ProcessPaymentResult();
            result.AddError(await _localizationService.GetResourceAsync(CustomerProfileDefaults.LocalePrefix + "Net30.NotAvailable"));
            return result;
        }

        return new ProcessPaymentResult { NewPaymentStatus = PaymentStatus.Pending };
    }

    public Task PostProcessPaymentAsync(PostProcessPaymentRequest postProcessPaymentRequest) => Task.CompletedTask;

    public Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart) => Task.FromResult(decimal.Zero);

    public Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest capturePaymentRequest) =>
        Task.FromResult(new CapturePaymentResult { Errors = new[] { "Capture method not supported" } });

    public Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest refundPaymentRequest) =>
        Task.FromResult(new RefundPaymentResult { Errors = new[] { "Refund method not supported" } });

    public Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest voidPaymentRequest) =>
        Task.FromResult(new VoidPaymentResult { Errors = new[] { "Void method not supported" } });

    public Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest processPaymentRequest) =>
        Task.FromResult(new ProcessPaymentResult { Errors = new[] { "Recurring payment not supported" } });

    public Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest cancelPaymentRequest) =>
        Task.FromResult(new CancelRecurringPaymentResult { Errors = new[] { "Recurring payment not supported" } });

    public Task<bool> CanRePostProcessPaymentAsync(Order order) => Task.FromResult(false);

    public Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form) => Task.FromResult<IList<string>>(new List<string>());

    public Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form) => Task.FromResult(new ProcessPaymentRequest());

    public Type GetPublicViewComponent() => typeof(Net30PaymentInfoViewComponent);

    public async Task<string> GetPaymentMethodDescriptionAsync()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var profile = await _profileService.GetAsync(customer.Id);
        var days = profile?.Net30TermsDays ?? _settings.Net30DefaultTermsDays;

        return string.Format(await _localizationService.GetResourceAsync(CustomerProfileDefaults.LocalePrefix + "Net30.Description"), days);
    }

    public bool SupportCapture => false;
    public bool SupportPartiallyRefund => false;
    public bool SupportRefund => false;
    public bool SupportVoid => false;
    public RecurringPaymentType RecurringPaymentType => RecurringPaymentType.NotSupported;
    public PaymentMethodType PaymentMethodType => PaymentMethodType.Standard;

    /// <summary>Nothing to collect, so one-page checkout goes straight from method to confirm</summary>
    public bool SkipPaymentInfo => true;

    #endregion
}
