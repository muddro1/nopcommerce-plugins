namespace Nop.Plugin.DiscountRules.HasOnlyProducts
{
    /// <summary>
    /// Represents constants for the discount requirement rule
    /// </summary>
    public static class DiscountRequirementDefaults
    {
        /// <summary>
        /// The system name of the discount requirement rule
        /// </summary>
        public const string SYSTEM_NAME = "DiscountRequirement.HasOnlyProducts";

        /// <summary>
        /// The key of the settings to save restricted products
        /// </summary>
        public const string SETTINGS_KEY = "DiscountRequirement.HasOnlyProducts-RestrictedProductIds-{0}";

        /// <summary>
        /// The key of the settings to save whether the restricted products should be the only products in the cart
        /// </summary>
        public const string EXCLUSIVE_SETTINGS_KEY = "DiscountRequirement.HasOnlyProducts-Exclusive-{0}";

        /// <summary>
        /// The key of the settings to save whether any of the restricted products is enough (instead of all of them)
        /// </summary>
        public const string MATCH_ANY_SETTINGS_KEY = "DiscountRequirement.HasOnlyProducts-MatchAny-{0}";

        /// <summary>
        /// The HTML field prefix for discount requirements
        /// </summary>
        public const string HTML_FIELD_PREFIX = "DiscountRulesHasOnlyProducts{0}";
    }
}
