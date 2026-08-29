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
    }
}
