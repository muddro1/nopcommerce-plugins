using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Services.Common;
using Nop.Plugin.Misc.HandlingFee.Services;
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
        private readonly HandlingFeeLabelService _labelService;
        private readonly IWebHelper _webHelper;

        #endregion

        #region Ctor

        public HandlingFeePlugin(ILocalizationService localizationService,
            ISettingService settingService,
            IWebHelper webHelper,
            HandlingFeeLabelService labelService)
        {
            _localizationService = localizationService;
            _settingService = settingService;
            _webHelper = webHelper;
            _labelService = labelService;
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

            //a label set under an earlier build was written into the resources that build
            //managed. Migrate the backup first, so the originals of any newly managed resource
            //(the 4.90 PDF invoice label) are captured before the label is applied over them
            var settings = await _settingService.LoadSettingAsync<HandlingFeeSettings>();
            if (!string.IsNullOrEmpty(settings?.LabelBackupJson))
            {
                settings.LabelBackupJson = await _labelService.MigrateBackupAsync(settings.LabelBackupJson);
                await _settingService.SaveSettingAsync(settings, x => x.LabelBackupJson, 0, false);
                await _settingService.ClearCacheAsync();

                if (!string.IsNullOrEmpty(settings.Label))
                    await _labelService.ApplyAsync(settings.Label);
            }

            await base.UpdateAsync(currentVersion, targetVersion);
        }

        public override async Task UninstallAsync()
        {
            //put back any core locale resources this plugin overwrote, before the
            //backup of them is deleted along with the settings
            var settings = await _settingService.LoadSettingAsync<HandlingFeeSettings>();
            if (!string.IsNullOrEmpty(settings?.LabelBackupJson))
                await _labelService.RestoreAsync(settings.LabelBackupJson);

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
                ["Plugins.Misc.HandlingFee.Fields.Label"] = "Label shown to customers",
                ["Plugins.Misc.HandlingFee.Fields.Label.Hint"] = "Optional. The wording the fee appears under on the cart, checkout, order details, invoices, emails and admin order screens - for example \"Small Order Handling Charge\". Leave blank to keep nopCommerce's own wording. This applies store-wide and to every language, because the underlying text is a shared nopCommerce resource rather than a per-store setting. Clearing this box restores the original wording.",
                ["Plugins.Misc.HandlingFee.Fields.Enabled"] = "Enabled",
                ["Plugins.Misc.HandlingFee.Fields.Enabled.Hint"] = "Charge a handling fee on qualifying orders.",
                ["Plugins.Misc.HandlingFee.Fields.ThresholdAmount"] = "Order threshold",
                ["Plugins.Misc.HandlingFee.Fields.ThresholdAmount.Hint"] = "The fee applies when the measured goods subtotal is at or below this amount. Shipping, tax, gift cards and reward points are not counted. Not used by tiered fees, whose highest band is the limit.",
                ["Plugins.Misc.HandlingFee.Fields.ThresholdAmount.Negative"] = "The order threshold cannot be negative.",
                ["Plugins.Misc.HandlingFee.Fields.FeeAmount"] = "Handling fee",
                ["Plugins.Misc.HandlingFee.Fields.FeeAmount.Hint"] = "The amount charged, in the primary store currency.",
                ["Plugins.Misc.HandlingFee.Fields.FeeAmount.Negative"] = "The handling fee cannot be negative.",
                ["Plugins.Misc.HandlingFee.Fields.SuppressWhenShippingCharged"] = "No fee when shipping is charged",
                ["Plugins.Misc.HandlingFee.Fields.SuppressWhenShippingCharged.Hint"] = "When ticked, any shipping charge above zero removes the handling fee entirely.",
                ["Plugins.Misc.HandlingFee.Fields.FeeType"] = "Fee type",
                ["Plugins.Misc.HandlingFee.Fields.FeeType.Hint"] = "Flat: one fixed amount. Percentage: a share of the subtotal, optionally kept between a minimum and a maximum. Tiered: a different fee for each subtotal band.",
                ["Plugins.Misc.HandlingFee.Fields.FeeType.Flat"] = "Flat amount",
                ["Plugins.Misc.HandlingFee.Fields.FeeType.Percentage"] = "Percentage of subtotal",
                ["Plugins.Misc.HandlingFee.Fields.FeeType.Tiered"] = "Tiered bands",
                ["Plugins.Misc.HandlingFee.Fields.FeePercent"] = "Percentage",
                ["Plugins.Misc.HandlingFee.Fields.FeePercent.Hint"] = "The share of the measured subtotal charged, for example 10 for 10%.",
                ["Plugins.Misc.HandlingFee.Fields.FeePercent.Range"] = "The percentage must be between 0 and 100.",
                ["Plugins.Misc.HandlingFee.Fields.FeeMinimum"] = "Minimum fee",
                ["Plugins.Misc.HandlingFee.Fields.FeeMinimum.Hint"] = "The percentage fee is never less than this. 0 for no minimum.",
                ["Plugins.Misc.HandlingFee.Fields.FeeMinimum.Negative"] = "The minimum fee cannot be negative.",
                ["Plugins.Misc.HandlingFee.Fields.FeeMaximum"] = "Maximum fee",
                ["Plugins.Misc.HandlingFee.Fields.FeeMaximum.Hint"] = "The percentage fee is never more than this. 0 for no maximum.",
                ["Plugins.Misc.HandlingFee.Fields.FeeMaximum.Negative"] = "The maximum fee cannot be negative.",
                ["Plugins.Misc.HandlingFee.Fields.FeeMaximum.BelowMinimum"] = "The maximum fee cannot be below the minimum fee.",
                ["Plugins.Misc.HandlingFee.Fields.Tiers"] = "Fee bands",
                ["Plugins.Misc.HandlingFee.Fields.Tiers.Hint"] = "Each band charges its fee when the measured subtotal is at or below its limit and above the previous band's. Above the highest limit there is no fee.",
                ["Plugins.Misc.HandlingFee.Fields.Tiers.Invalid"] = "Add at least one band. Every band needs a limit of 0 or more and a fee above 0, and no two bands can share a limit.",
                ["Plugins.Misc.HandlingFee.Fields.Tiers.UpTo"] = "Subtotal up to",
                ["Plugins.Misc.HandlingFee.Fields.Tiers.Fee"] = "Fee",
                ["Plugins.Misc.HandlingFee.Fields.Tiers.Add"] = "Add band",
                ["Plugins.Misc.HandlingFee.Fields.SubtotalBasis"] = "Measure subtotal",
                ["Plugins.Misc.HandlingFee.Fields.SubtotalBasis.Hint"] = "Which goods subtotal is compared with the threshold or bands, and used for a percentage fee. Product-level discounts are always taken off first; this chooses whether order-subtotal discounts are too.",
                ["Plugins.Misc.HandlingFee.Fields.SubtotalBasis.AfterDiscounts"] = "After discounts",
                ["Plugins.Misc.HandlingFee.Fields.SubtotalBasis.BeforeOrderDiscounts"] = "Before order discounts",
                ["Plugins.Misc.HandlingFee.Fields.RequireShippedItem"] = "Require a shipped item",
                ["Plugins.Misc.HandlingFee.Fields.RequireShippedItem.Hint"] = "When ticked, orders with nothing to ship (downloads, gift cards) never pay the fee.",
                ["Plugins.Misc.HandlingFee.Fields.ShippingMethods"] = "Only these shipping methods",
                ["Plugins.Misc.HandlingFee.Fields.ShippingMethods.Hint"] = "Leave empty for every method. Pick a whole provider (\"UPS (any service)\") or a shipping method; a method is recognised in every language and survives renaming. Before the customer chooses a method, the fee is shown.",
                ["Plugins.Misc.HandlingFee.Fields.Countries"] = "Only these countries",
                ["Plugins.Misc.HandlingFee.Fields.Countries.Hint"] = "Leave empty for every country. Matched against the shipping address, or the pickup point's country for pickup in store; before either is known, the fee is shown.",
                ["Plugins.Misc.HandlingFee.Fields.ExemptCustomerRoles"] = "Exempt customer roles",
                ["Plugins.Misc.HandlingFee.Fields.ExemptCustomerRoles.Hint"] = "Customers in any of these roles never pay the fee.",
                ["Plugins.Misc.HandlingFee.Fields.ExclusionMode"] = "When the cart has excluded items",
                ["Plugins.Misc.HandlingFee.Fields.ExclusionMode.Hint"] = "Waive the fee: any excluded item means no fee. Leave them out: excluded items do not count toward the subtotal and do not count as shipped items.",
                ["Plugins.Misc.HandlingFee.Fields.ExclusionMode.WaiveFee"] = "Waive the fee",
                ["Plugins.Misc.HandlingFee.Fields.ExclusionMode.LeaveOut"] = "Leave them out of the subtotal",
                ["Plugins.Misc.HandlingFee.Fields.ExcludedCategories"] = "Excluded categories",
                ["Plugins.Misc.HandlingFee.Fields.ExcludedCategories.Hint"] = "Products in these categories, or any of their subcategories, are excluded.",
                ["Plugins.Misc.HandlingFee.Fields.ExcludedProducts"] = "Excluded products",
                ["Plugins.Misc.HandlingFee.Fields.ExcludedProducts.Hint"] = "Comma-separated product IDs. Use the button to pick products.",
                ["Plugins.Misc.HandlingFee.Fields.ExcludedProducts.Choose"] = "Choose products",
                ["Plugins.Misc.HandlingFee.Cards.General"] = "General",
                ["Plugins.Misc.HandlingFee.Cards.FeeAmount"] = "Fee amount",
                ["Plugins.Misc.HandlingFee.Cards.Conditions"] = "When the fee applies",
                ["Plugins.Misc.HandlingFee.Cards.Exclusions"] = "Excluded products & categories",
                ["Plugins.Misc.HandlingFee.Cards.Summary"] = "Rule summary",
                ["Plugins.Misc.HandlingFee.Tester.Title"] = "Try a cart",
                ["Plugins.Misc.HandlingFee.Tester.Hint"] = "Checks the settings as they are on this page, before saving. In \"leave them out\" mode, enter the subtotal of the items that are not excluded.",
                ["Plugins.Misc.HandlingFee.Tester.Subtotal"] = "Subtotal after discounts",
                ["Plugins.Misc.HandlingFee.Tester.SubtotalBefore"] = "Before order discounts (blank = same)",
                ["Plugins.Misc.HandlingFee.Tester.Shipping"] = "Shipping charge",
                ["Plugins.Misc.HandlingFee.Tester.HasShippedItem"] = "Has a shipped item",
                ["Plugins.Misc.HandlingFee.Tester.HasExcludedItem"] = "Contains an excluded item",
                ["Plugins.Misc.HandlingFee.Tester.OnlyExcludedItems"] = "Contains only excluded items",
                ["Plugins.Misc.HandlingFee.Tester.Role"] = "Customer role",
                ["Plugins.Misc.HandlingFee.Tester.Country"] = "Ships to",
                ["Plugins.Misc.HandlingFee.Tester.ShippingMethod"] = "Shipping method",
                ["Plugins.Misc.HandlingFee.Tester.NotKnownYet"] = "Not known yet",
                ["Plugins.Misc.HandlingFee.Configuration.Saved"] = "The settings have been saved."
            });
        }

        #endregion
    }
}
