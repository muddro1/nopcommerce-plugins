using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.HandlingFee.Models;
using Nop.Plugin.Misc.HandlingFee.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.HandlingFee.Controllers
{
    [AuthorizeAdmin]
    [Area(AreaNames.Admin)]
    [AutoValidateAntiforgeryToken]
    public class MiscHandlingFeeController : BasePluginController
    {
        private readonly ILocalizationService _localizationService;
        private readonly INotificationService _notificationService;
        private readonly IPermissionService _permissionService;
        private readonly ISettingService _settingService;
        private readonly HandlingFeeLabelService _labelService;
        private readonly IStoreContext _storeContext;

        public MiscHandlingFeeController(ILocalizationService localizationService,
            INotificationService notificationService,
            IPermissionService permissionService,
            ISettingService settingService,
            IStoreContext storeContext,
            HandlingFeeLabelService labelService)
        {
            _localizationService = localizationService;
            _notificationService = notificationService;
            _permissionService = permissionService;
            _settingService = settingService;
            _storeContext = storeContext;
            _labelService = labelService;
        }

        public async Task<IActionResult> Configure()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePlugins))
                return AccessDeniedView();

            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var settings = await _settingService.LoadSettingAsync<HandlingFeeSettings>(storeScope);

            var model = new ConfigurationModel
            {
                ActiveStoreScopeConfiguration = storeScope,
                Enabled = settings.Enabled,
                Label = settings.Label,
                ThresholdAmount = settings.ThresholdAmount,
                FeeAmount = settings.FeeAmount,
                SuppressWhenShippingCharged = settings.SuppressWhenShippingCharged
            };

            if (storeScope > 0)
            {
                model.Enabled_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.Enabled, storeScope);
                model.ThresholdAmount_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.ThresholdAmount, storeScope);
                model.FeeAmount_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.FeeAmount, storeScope);
                model.SuppressWhenShippingCharged_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.SuppressWhenShippingCharged, storeScope);
            }

            return View("~/Plugins/Misc.HandlingFee/Views/Configure.cshtml", model);
        }

        [HttpPost]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePlugins))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return await Configure();

            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var settings = await _settingService.LoadSettingAsync<HandlingFeeSettings>(storeScope);

            var newLabel = (model.Label ?? string.Empty).Trim();

            if (!string.IsNullOrEmpty(newLabel))
            {
                //capture the originals once, before the first overwrite, so they can be put back
                if (string.IsNullOrEmpty(settings.LabelBackupJson))
                    settings.LabelBackupJson = await _labelService.CaptureOriginalsAsync();

                await _labelService.ApplyAsync(newLabel);
            }
            else if (!string.IsNullOrEmpty(settings.LabelBackupJson))
            {
                //label cleared: put nopCommerce's own wording back
                await _labelService.RestoreAsync(settings.LabelBackupJson);
                settings.LabelBackupJson = string.Empty;
            }

            settings.Label = newLabel;

            settings.Enabled = model.Enabled;
            settings.ThresholdAmount = model.ThresholdAmount;
            settings.FeeAmount = model.FeeAmount;
            settings.SuppressWhenShippingCharged = model.SuppressWhenShippingCharged;

            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.Enabled, model.Enabled_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.ThresholdAmount, model.ThresholdAmount_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.FeeAmount, model.FeeAmount_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.SuppressWhenShippingCharged, model.SuppressWhenShippingCharged_OverrideForStore, storeScope, false);

            //Label and LabelBackupJson are global, not per store, because the locale
            //resources they drive are per language
            await _settingService.SaveSettingAsync(settings, x => x.Label, 0, false);
            await _settingService.SaveSettingAsync(settings, x => x.LabelBackupJson, 0, false);

            await _settingService.ClearCacheAsync();

            _notificationService.SuccessNotification(
                await _localizationService.GetResourceAsync("Plugins.Misc.HandlingFee.Configuration.Saved"));

            return await Configure();
        }
    }
}
