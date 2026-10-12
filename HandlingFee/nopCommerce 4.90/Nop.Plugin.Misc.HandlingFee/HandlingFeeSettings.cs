using Nop.Core.Configuration;

namespace Nop.Plugin.Misc.HandlingFee
{
    /// <summary>
    /// Represents the handling fee settings
    /// </summary>
    public class HandlingFeeSettings : ISettings
    {
        /// <summary>
        /// Gets or sets a value indicating whether the handling fee is active
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Gets or sets the goods subtotal at or below which the fee applies
        /// </summary>
        public decimal ThresholdAmount { get; set; }

        /// <summary>
        /// Gets or sets the fee charged
        /// </summary>
        public decimal FeeAmount { get; set; }

        /// <summary>
        /// Gets or sets a custom label for the fee. Blank leaves nopCommerce's own wording alone.
        /// Applies store-wide: the underlying locale resources are per language, not per store.
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// Gets or sets the captured original locale resource values, so they can be restored
        /// when the label is cleared or the plugin is uninstalled. Internal; not shown in the UI.
        /// </summary>
        public string LabelBackupJson { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a shipping charge suppresses the fee.
        /// Defaults to true so that a missing stored setting row fails closed (no fee charged
        /// on top of paid shipping) rather than open, matching the documented default.
        /// </summary>
        public bool SuppressWhenShippingCharged { get; set; } = true;

        //every setting below was added in 2.10. Each default reproduces the 2.00 rule exactly,
        //so a store that upgrades charges the same fee until someone changes a setting.

        /// <summary>
        /// Gets or sets how the fee amount is worked out
        /// </summary>
        public HandlingFeeType FeeType { get; set; } = HandlingFeeType.Flat;

        /// <summary>
        /// Gets or sets the percentage of the measured subtotal charged when <see cref="FeeType"/> is Percentage
        /// </summary>
        public decimal FeePercent { get; set; }

        /// <summary>
        /// Gets or sets the smallest percentage fee charged; zero for no minimum
        /// </summary>
        public decimal FeeMinimum { get; set; }

        /// <summary>
        /// Gets or sets the largest percentage fee charged; zero for no maximum
        /// </summary>
        public decimal FeeMaximum { get; set; }

        /// <summary>
        /// Gets or sets the tier bands as JSON, a list of { "UpTo": .., "Fee": .. }, used when
        /// <see cref="FeeType"/> is Tiered
        /// </summary>
        public string TiersJson { get; set; }

        /// <summary>
        /// Gets or sets which subtotal is measured against the threshold or tiers
        /// </summary>
        public HandlingFeeSubtotalBasis SubtotalBasis { get; set; } = HandlingFeeSubtotalBasis.AfterDiscounts;

        /// <summary>
        /// Gets or sets a value indicating whether the cart must contain at least one shipped item
        /// </summary>
        public bool RequireShippedItem { get; set; } = true;

        /// <summary>
        /// Gets or sets the shipping methods the fee is limited to, as a JSON list of tokens:
        /// "provider:{system name}" for any option of a shipping provider, "method:{name}" for
        /// a named option. Empty means every method.
        /// </summary>
        public string ShippingMethodsJson { get; set; }

        /// <summary>
        /// Gets or sets the comma-separated shipping country identifiers the fee is limited to; empty means every country
        /// </summary>
        public string CountryIds { get; set; }

        /// <summary>
        /// Gets or sets the comma-separated customer role identifiers that never pay the fee
        /// </summary>
        public string ExemptCustomerRoleIds { get; set; }

        /// <summary>
        /// Gets or sets the comma-separated excluded product identifiers
        /// </summary>
        public string ExcludedProductIds { get; set; }

        /// <summary>
        /// Gets or sets the comma-separated excluded category identifiers
        /// </summary>
        public string ExcludedCategoryIds { get; set; }

        /// <summary>
        /// Gets or sets what excluded items do to the fee
        /// </summary>
        public HandlingFeeExclusionMode ExclusionMode { get; set; } = HandlingFeeExclusionMode.WaiveFee;
    }

    /// <summary>
    /// How the handling fee amount is worked out
    /// </summary>
    public enum HandlingFeeType
    {
        /// <summary>A fixed amount</summary>
        Flat = 0,

        /// <summary>A percentage of the measured subtotal, optionally clamped</summary>
        Percentage = 1,

        /// <summary>A fee per subtotal band</summary>
        Tiered = 2
    }

    /// <summary>
    /// Which goods subtotal the threshold or tiers are measured against
    /// </summary>
    public enum HandlingFeeSubtotalBasis
    {
        /// <summary>After item-level and order-subtotal discounts</summary>
        AfterDiscounts = 0,

        /// <summary>After item-level discounts but before order-subtotal discounts</summary>
        BeforeOrderDiscounts = 1
    }

    /// <summary>
    /// What excluded products and categories do to the fee
    /// </summary>
    public enum HandlingFeeExclusionMode
    {
        /// <summary>Any excluded item in the cart waives the fee</summary>
        WaiveFee = 0,

        /// <summary>Excluded items are left out of the subtotal and do not count as shipped items</summary>
        LeaveOut = 1
    }
}
