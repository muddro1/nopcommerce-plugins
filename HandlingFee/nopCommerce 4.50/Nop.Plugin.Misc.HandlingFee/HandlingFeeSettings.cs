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
        /// Gets or sets a value indicating whether a shipping charge suppresses the fee
        /// </summary>
        public bool SuppressWhenShippingCharged { get; set; }
    }
}
