using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;

namespace Nop.Plugin.Misc.HandlingFee
{
    public class HandlingFeePlugin : BasePlugin, IMiscPlugin
    {
        #region Fields

        private readonly ILocalizationService _localizationService;
        private readonly ISettingService _settingService;
        private readonly IWebHelper _webHelper;

        #endregion

        #region Ctor

        public HandlingFeePlugin(ILocalizationService localizationService,
            ISettingService settingService,
            IWebHelper webHelper)
        {
            _localizationService = localizationService;
            _settingService = settingService;
            _webHelper = webHelper;
        }

        #endregion

        #region Methods

        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/MiscHandlingFee/Configure";
        }

        public override async Task InstallAsync()
        {
            await _settingService.SaveSettingAsync(new HandlingFeeSettings
            {
                Enabled = false,
                ThresholdAmount = decimal.Zero,
                FeeAmount = decimal.Zero,
                SuppressWhenShippingCharged = true
            });

            await AddOrUpdateLocalesAsync();

            await base.InstallAsync();
        }

        public override async Task UpdateAsync(string currentVersion, string targetVersion)
        {
            //locale resources added in later versions are missing on sites that installed
            //an earlier one, so re-register them all on every upgrade
            await AddOrUpdateLocalesAsync();

            await base.UpdateAsync(currentVersion, targetVersion);
        }

        public override async Task UninstallAsync()
        {
            await _settingService.DeleteSettingAsync<HandlingFeeSettings>();
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Misc.HandlingFee");

            await base.UninstallAsync();
        }

        #endregion

        #region Utilities

        private async Task AddOrUpdateLocalesAsync()
        {
            await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
            {
                ["Plugins.Misc.HandlingFee.Fields.Enabled"] = "Enabled",
                ["Plugins.Misc.HandlingFee.Fields.Enabled.Hint"] = "Charge a handling fee on qualifying orders.",
                ["Plugins.Misc.HandlingFee.Fields.ThresholdAmount"] = "Order threshold",
                ["Plugins.Misc.HandlingFee.Fields.ThresholdAmount.Hint"] = "The fee applies when the goods subtotal, after discounts, is at or below this amount. Shipping, tax, gift cards and reward points are not counted.",
                ["Plugins.Misc.HandlingFee.Fields.ThresholdAmount.Negative"] = "The order threshold cannot be negative.",
                ["Plugins.Misc.HandlingFee.Fields.FeeAmount"] = "Handling fee",
                ["Plugins.Misc.HandlingFee.Fields.FeeAmount.Hint"] = "The amount charged, in the primary store currency.",
                ["Plugins.Misc.HandlingFee.Fields.FeeAmount.Negative"] = "The handling fee cannot be negative.",
                ["Plugins.Misc.HandlingFee.Fields.SuppressWhenShippingCharged"] = "No fee when shipping is charged",
                ["Plugins.Misc.HandlingFee.Fields.SuppressWhenShippingCharged.Hint"] = "When ticked, any shipping charge above zero removes the handling fee entirely. Orders that need no shipping at all never attract the fee.",
                ["Plugins.Misc.HandlingFee.Configuration.Saved"] = "The settings have been saved."
            });
        }

        #endregion
    }
}
