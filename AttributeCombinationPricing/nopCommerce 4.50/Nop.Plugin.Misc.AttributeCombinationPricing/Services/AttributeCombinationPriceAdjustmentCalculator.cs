namespace Nop.Plugin.Misc.AttributeCombinationPricing.Services
{
    /// <summary>
    /// Decides the corrected price-adjustment for an "Associated to product" attribute value, when its
    /// attribute combination has an Overridden price. Deliberately pure: it takes every figure it needs
    /// as a parameter, so it has no dependency on nopCommerce services.
    /// </summary>
    public static class AttributeCombinationPriceAdjustmentCalculator
    {
        /// <summary>
        /// Calculate the corrected price adjustment
        /// </summary>
        /// <param name="basePrice">The product's own final price with no attributes selected</param>
        /// <param name="combinationOverriddenPrice">
        /// The Overridden price of the attribute combination matching this value plus the product's
        /// other attributes at their default state, or null if no such combination exists or it has no
        /// Overridden price set
        /// </param>
        /// <returns>
        /// The corrected adjustment (<paramref name="combinationOverriddenPrice"/> minus
        /// <paramref name="basePrice"/>), or null when there is nothing to correct and the caller should
        /// keep core's original label
        /// </returns>
        public static decimal? TryGetCorrectedAdjustment(decimal basePrice, decimal? combinationOverriddenPrice)
        {
            if (!combinationOverriddenPrice.HasValue)
                return null;

            return combinationOverriddenPrice.Value - basePrice;
        }
    }
}
