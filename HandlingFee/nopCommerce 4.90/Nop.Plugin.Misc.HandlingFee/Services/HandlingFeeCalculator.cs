namespace Nop.Plugin.Misc.HandlingFee.Services
{
    /// <summary>
    /// Decides whether a handling fee applies, and how much it is.
    /// Deliberately pure: it takes every figure it needs as a parameter so that it
    /// has no dependency on nopCommerce services and cannot take part in a DI cycle.
    /// </summary>
    public static class HandlingFeeCalculator
    {
        /// <summary>
        /// Calculate the handling fee
        /// </summary>
        /// <param name="settings">Handling fee settings</param>
        /// <param name="goodsSubtotalAfterDiscounts">Goods subtotal once item and subtotal discounts are applied, excluding shipping and tax</param>
        /// <param name="shippingTotal">Shipping charge; null when no shipping method has been selected yet, which counts as zero</param>
        /// <param name="cartRequiresShipping">Whether any item in the cart is ship-enabled</param>
        /// <returns>The fee, or zero</returns>
        public static decimal Calculate(HandlingFeeSettings settings,
            decimal goodsSubtotalAfterDiscounts,
            decimal? shippingTotal,
            bool cartRequiresShipping)
        {
            //a disabled or absent configuration must be a complete no-op
            if (settings == null || !settings.Enabled)
                return decimal.Zero;

            //the fee pays for physical handling, so downloadable and virtual orders are exempt
            if (!cartRequiresShipping)
                return decimal.Zero;

            //"at or below" the threshold
            if (goodsSubtotalAfterDiscounts > settings.ThresholdAmount)
                return decimal.Zero;

            //a shipping charge of any size absorbs the fee entirely
            //a null shipping total means no method chosen yet, which counts as no charge
            if (settings.SuppressWhenShippingCharged && (shippingTotal ?? decimal.Zero) > decimal.Zero)
                return decimal.Zero;

            //defence in depth: a misconfigured (e.g. negative) FeeAmount must never make an
            //order cheaper. Validation on the settings form should already stop this at the
            //source, but the calculator must not trust that it always will.
            if (settings.FeeAmount <= decimal.Zero)
                return decimal.Zero;

            return settings.FeeAmount;
        }
    }
}
