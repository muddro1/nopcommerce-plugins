namespace Nop.Plugin.Misc.HandlingFee.Services
{
    /// <summary>
    /// The 2.00 entry point, kept as a thin wrapper over <see cref="HandlingFeeRule"/>. With
    /// default settings for everything added in 2.10 it must give exactly the 2.00 answer,
    /// which is what its tests pin down.
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
            return HandlingFeeRule.Evaluate(settings, new HandlingFeeContext
            {
                SubtotalAfterDiscounts = goodsSubtotalAfterDiscounts,
                SubtotalBeforeOrderDiscounts = goodsSubtotalAfterDiscounts,
                ShippingTotal = shippingTotal,
                HasShippedItem = cartRequiresShipping
            }).Fee;
        }
    }
}
