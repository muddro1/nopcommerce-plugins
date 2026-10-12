using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Nop.Plugin.Misc.HandlingFee.Services
{
    /// <summary>
    /// Display names the describer needs for identifiers stored in settings. Anything missing
    /// from a map is shown by its identifier, so a deleted role or category is still visible.
    /// </summary>
    public record HandlingFeeRuleNames
    {
        public IReadOnlyDictionary<int, string> Roles { get; init; } = new Dictionary<int, string>();

        public IReadOnlyDictionary<int, string> Countries { get; init; } = new Dictionary<int, string>();

        public IReadOnlyDictionary<int, string> Categories { get; init; } = new Dictionary<int, string>();

        public IReadOnlyDictionary<int, string> Products { get; init; } = new Dictionary<int, string>();

        public IReadOnlyDictionary<string, string> ShippingMethods { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Turns the settings, and a <see cref="HandlingFeeDecision"/>, into the plain-English text
    /// the configuration page shows. Pure, like the rule it describes. The text is admin-only
    /// and composed from parts, so it is English rather than locale resources.
    /// </summary>
    public static class HandlingFeeRuleDescriber
    {
        #region Methods

        /// <summary>
        /// Describe the rule the settings define, one sentence per entry
        /// </summary>
        /// <param name="settings">Settings (possibly unsaved form values)</param>
        /// <param name="names">Display names for stored identifiers</param>
        /// <param name="money">Formats an amount in the primary store currency</param>
        public static IList<string> Describe(HandlingFeeSettings settings, HandlingFeeRuleNames names, Func<decimal, string> money)
        {
            ArgumentNullException.ThrowIfNull(settings);
            names ??= new HandlingFeeRuleNames();

            var sentences = new List<string>();

            if (!settings.Enabled)
                sentences.Add("The handling fee is switched off, so nothing is charged. When it is enabled:");

            var basis = settings.SubtotalBasis == HandlingFeeSubtotalBasis.BeforeOrderDiscounts
                ? "before order discounts"
                : "after discounts";
            var order = settings.RequireShippedItem ? "an order with at least one shipped item" : "any order";
            var freeShipping = settings.SuppressWhenShippingCharged ? " and ships for free" : string.Empty;

            switch (settings.FeeType)
            {
                case HandlingFeeType.Tiered:
                    var tiers = HandlingFeeRule.ParseTiers(settings.TiersJson);
                    if (tiers.Count == 0)
                    {
                        sentences.Add("No fee bands are set up, so nothing is charged.");
                        break;
                    }

                    var bands = string.Join(", ", tiers.Select(tier => $"{money(tier.Fee)} up to {money(tier.UpTo)}"));
                    sentences.Add($"Charge {bands} when {order} has a subtotal ({basis}) in that band{freeShipping}. " +
                        $"Nothing above {money(tiers.Last().UpTo)}.");
                    break;

                case HandlingFeeType.Percentage:
                    var clamp = (settings.FeeMinimum > decimal.Zero, settings.FeeMaximum > decimal.Zero) switch
                    {
                        (true, true) => $" (at least {money(settings.FeeMinimum)}, at most {money(settings.FeeMaximum)})",
                        (true, false) => $" (at least {money(settings.FeeMinimum)})",
                        (false, true) => $" (at most {money(settings.FeeMaximum)})",
                        _ => string.Empty
                    };
                    sentences.Add($"Charge {settings.FeePercent.ToString("0.##", CultureInfo.InvariantCulture)}% of the subtotal{clamp} " +
                        $"when {order} has a subtotal ({basis}) of {money(settings.ThresholdAmount)} or less{freeShipping}.");
                    break;

                default:
                    sentences.Add($"Charge {money(settings.FeeAmount)} when {order} has a subtotal ({basis}) " +
                        $"of {money(settings.ThresholdAmount)} or less{freeShipping}.");
                    break;
            }

            var restricted = false;

            var methods = HandlingFeeRule.ParseTokens(settings.ShippingMethodsJson);
            if (methods.Count > 0)
            {
                sentences.Add($"Only with these shipping methods: {List(methods, names.ShippingMethods)}.");
                restricted = true;
            }

            var countries = HandlingFeeRule.ParseIds(settings.CountryIds);
            if (countries.Count > 0)
            {
                sentences.Add($"Only when shipping to: {List(countries, names.Countries)}.");
                restricted = true;
            }

            var roles = HandlingFeeRule.ParseIds(settings.ExemptCustomerRoleIds);
            if (roles.Count > 0)
            {
                sentences.Add($"Never charged to: {List(roles, names.Roles)}.");
                restricted = true;
            }

            var excluded = HandlingFeeRule.ParseIds(settings.ExcludedCategoryIds)
                .Select(id => Name(id, names.Categories) + " (and its subcategories)")
                .Concat(HandlingFeeRule.ParseIds(settings.ExcludedProductIds).Select(id => Name(id, names.Products)))
                .ToList();
            if (excluded.Count > 0)
            {
                sentences.Add(settings.ExclusionMode == HandlingFeeExclusionMode.LeaveOut
                    ? $"Left out of the subtotal, and not counted as shipped items: {string.Join(", ", excluded)}."
                    : $"Not charged when the cart contains: {string.Join(", ", excluded)}.");
                restricted = true;
            }

            if (!restricted)
                sentences.Add("Any shipping method, any destination, any customer, any products.");

            return sentences;
        }

        /// <summary>
        /// Explain a decision, for the "try a cart" tester
        /// </summary>
        public static string Explain(HandlingFeeDecision decision, Func<decimal, string> money)
        {
            ArgumentNullException.ThrowIfNull(decision);

            return decision.Reason switch
            {
                HandlingFeeReason.Applied => $"Charged {money(decision.Fee)} (measured subtotal {money(decision.MeasuredSubtotal)}).",
                HandlingFeeReason.Disabled => "No fee: the handling fee is switched off.",
                HandlingFeeReason.ExemptRole => "No fee: the customer has an exempt role.",
                HandlingFeeReason.ExcludedItem => "No fee: the cart contains excluded items.",
                HandlingFeeReason.NoShippedItem => "No fee: nothing in the cart ships.",
                HandlingFeeReason.CountryNotIncluded => "No fee: the order ships to a country the fee does not apply to.",
                HandlingFeeReason.ShippingMethodNotIncluded => "No fee: the chosen shipping method is not one the fee applies to.",
                HandlingFeeReason.ShippingCharged => "No fee: shipping is being charged.",
                HandlingFeeReason.AboveThreshold => $"No fee: the measured subtotal of {money(decision.MeasuredSubtotal)} is above the limit.",
                HandlingFeeReason.ZeroFee => "No fee: the amount works out to zero.",
                _ => "No fee."
            };
        }

        #endregion

        #region Utilities

        private static string List<T>(IEnumerable<T> ids, IReadOnlyDictionary<T, string> map)
        {
            return string.Join(", ", ids.Select(id => Name(id, map)));
        }

        private static string Name<T>(T id, IReadOnlyDictionary<T, string> map)
        {
            return map.TryGetValue(id, out var name) && !string.IsNullOrEmpty(name) ? name : $"#{id}";
        }

        #endregion
    }
}
