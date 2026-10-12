using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.HandlingFee.Models
{
    public record ConfigurationModel : BaseNopModel
    {
        public ConfigurationModel()
        {
            SelectedShippingMethods = new List<string>();
            SelectedCountryIds = new List<int>();
            SelectedExemptCustomerRoleIds = new List<int>();
            SelectedExcludedCategoryIds = new List<int>();
            AvailableShippingMethods = new List<SelectListItem>();
            AvailableCountries = new List<SelectListItem>();
            AvailableCustomerRoles = new List<SelectListItem>();
            AvailableCategories = new List<SelectListItem>();
        }

        public int ActiveStoreScopeConfiguration { get; set; }

        #region General

        //deliberately has no _OverrideForStore companion: locale resources are per language,
        //not per store, so a per-store label is not something nopCommerce can deliver
        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.Label")]
        public string Label { get; set; }

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.Enabled")]
        public bool Enabled { get; set; }
        public bool Enabled_OverrideForStore { get; set; }

        #endregion

        #region Fee amount

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.FeeType")]
        public int FeeType { get; set; }
        public bool FeeType_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.ThresholdAmount")]
        public decimal ThresholdAmount { get; set; }
        public bool ThresholdAmount_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.FeeAmount")]
        public decimal FeeAmount { get; set; }
        public bool FeeAmount_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.FeePercent")]
        public decimal FeePercent { get; set; }
        public bool FeePercent_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.FeeMinimum")]
        public decimal FeeMinimum { get; set; }
        public bool FeeMinimum_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.FeeMaximum")]
        public decimal FeeMaximum { get; set; }
        public bool FeeMaximum_OverrideForStore { get; set; }

        //edited by the band table on the page, which keeps this hidden field in step
        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.Tiers")]
        public string TiersJson { get; set; }
        public bool TiersJson_OverrideForStore { get; set; }

        #endregion

        #region When the fee applies

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.SubtotalBasis")]
        public int SubtotalBasis { get; set; }
        public bool SubtotalBasis_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.RequireShippedItem")]
        public bool RequireShippedItem { get; set; }
        public bool RequireShippedItem_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.SuppressWhenShippingCharged")]
        public bool SuppressWhenShippingCharged { get; set; }
        public bool SuppressWhenShippingCharged_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.ShippingMethods")]
        public IList<string> SelectedShippingMethods { get; set; }
        public bool SelectedShippingMethods_OverrideForStore { get; set; }
        public IList<SelectListItem> AvailableShippingMethods { get; set; }

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.Countries")]
        public IList<int> SelectedCountryIds { get; set; }
        public bool SelectedCountryIds_OverrideForStore { get; set; }
        public IList<SelectListItem> AvailableCountries { get; set; }

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.ExemptCustomerRoles")]
        public IList<int> SelectedExemptCustomerRoleIds { get; set; }
        public bool SelectedExemptCustomerRoleIds_OverrideForStore { get; set; }
        public IList<SelectListItem> AvailableCustomerRoles { get; set; }

        #endregion

        #region Exclusions

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.ExclusionMode")]
        public int ExclusionMode { get; set; }
        public bool ExclusionMode_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.ExcludedCategories")]
        public IList<int> SelectedExcludedCategoryIds { get; set; }
        public bool SelectedExcludedCategoryIds_OverrideForStore { get; set; }
        public IList<SelectListItem> AvailableCategories { get; set; }

        //comma-separated, filled by the product picker popup
        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.ExcludedProducts")]
        public string ExcludedProductIds { get; set; }
        public bool ExcludedProductIds_OverrideForStore { get; set; }

        #endregion
    }

    /// <summary>
    /// What the "try a cart" tester posts alongside the unsaved form
    /// </summary>
    public record RulePreviewCartModel
    {
        public decimal Subtotal { get; set; }

        //blank on the form means "after discounts equals this"
        public decimal? SubtotalBeforeOrderDiscounts { get; set; }

        public decimal Shipping { get; set; }

        public bool HasShippedItem { get; set; } = true;

        public bool HasExcludedItem { get; set; }

        public int RoleId { get; set; }

        public int CountryId { get; set; }

        //a shipping token, or blank for "not chosen yet"
        public string ShippingMethod { get; set; }
    }
}
