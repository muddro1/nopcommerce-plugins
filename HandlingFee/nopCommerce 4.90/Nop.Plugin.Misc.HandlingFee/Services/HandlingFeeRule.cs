using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Nop.Plugin.Misc.HandlingFee.Services
{
    /// <summary>
    /// Every fact about a cart that the handling fee rule needs. Built from a real cart by
    /// <see cref="HandlingFeeContextFactory"/>, or from the admin page's "try a cart" tester.
    /// A null country or shipping option means "not known yet", which never rules the fee out.
    /// </summary>
    public record HandlingFeeContext
    {
        /// <summary>Goods subtotal after item-level and order-subtotal discounts, excluding shipping and tax</summary>
        public decimal SubtotalAfterDiscounts { get; init; }

        /// <summary>Goods subtotal after item-level discounts but before order-subtotal discounts</summary>
        public decimal SubtotalBeforeOrderDiscounts { get; init; }

        /// <summary>Shipping charge; null when no shipping method has been selected yet, which counts as zero</summary>
        public decimal? ShippingTotal { get; init; }

        /// <summary>Whether any (counted) item in the cart is ship-enabled</summary>
        public bool HasShippedItem { get; init; }

        /// <summary>Whether the cart holds an excluded product, or a product in an excluded category</summary>
        public bool HasExcludedItem { get; init; }

        /// <summary>The customer's role identifiers</summary>
        public IReadOnlyCollection<int> CustomerRoleIds { get; init; } = Array.Empty<int>();

        /// <summary>The shipping address country; null when not known yet</summary>
        public int? CountryId { get; init; }

        /// <summary>The selected shipping option's provider system name; null when not chosen yet</summary>
        public string ShippingProviderSystemName { get; init; }

        /// <summary>The selected shipping option's name; null when not chosen yet</summary>
        public string ShippingOptionName { get; init; }
    }

    /// <summary>
    /// Which condition decided the outcome
    /// </summary>
    public enum HandlingFeeReason
    {
        Applied,
        Disabled,
        ExemptRole,
        ExcludedItem,
        NoShippedItem,
        CountryNotIncluded,
        ShippingMethodNotIncluded,
        ShippingCharged,
        AboveThreshold,
        ZeroFee
    }

    /// <summary>
    /// The outcome of the rule: the fee (zero unless <see cref="Reason"/> is Applied), the
    /// condition that decided it, and the subtotal that was measured
    /// </summary>
    public record HandlingFeeDecision(decimal Fee, HandlingFeeReason Reason, decimal MeasuredSubtotal);

    /// <summary>
    /// One tier band: a measured subtotal at or below <see cref="UpTo"/> pays <see cref="Fee"/>
    /// </summary>
    public record HandlingFeeTier
    {
        public decimal UpTo { get; init; }

        public decimal Fee { get; init; }
    }

    /// <summary>
    /// Decides whether a handling fee applies, and how much it is.
    /// Deliberately pure: it takes every fact it needs as a parameter so that it has no
    /// dependency on nopCommerce services, cannot take part in a DI cycle, and gives the
    /// admin page's live preview exactly the answer checkout would give.
    /// </summary>
    public static class HandlingFeeRule
    {
        #region Methods

        /// <summary>
        /// Evaluate the rule against a cart
        /// </summary>
        /// <param name="settings">Handling fee settings</param>
        /// <param name="context">Facts about the cart</param>
        /// <returns>The decision</returns>
        public static HandlingFeeDecision Evaluate(HandlingFeeSettings settings, HandlingFeeContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            var measured = settings?.SubtotalBasis == HandlingFeeSubtotalBasis.BeforeOrderDiscounts
                ? context.SubtotalBeforeOrderDiscounts
                : context.SubtotalAfterDiscounts;

            HandlingFeeDecision No(HandlingFeeReason reason) => new(decimal.Zero, reason, measured);

            //a disabled or absent configuration must be a complete no-op
            if (settings == null || !settings.Enabled)
                return No(HandlingFeeReason.Disabled);

            var exemptRoles = ParseIds(settings.ExemptCustomerRoleIds);
            if (exemptRoles.Count > 0 && context.CustomerRoleIds.Any(exemptRoles.Contains))
                return No(HandlingFeeReason.ExemptRole);

            if (settings.ExclusionMode == HandlingFeeExclusionMode.WaiveFee && context.HasExcludedItem)
                return No(HandlingFeeReason.ExcludedItem);

            //the fee pays for physical handling, so by default downloadable and virtual orders are exempt
            if (settings.RequireShippedItem && !context.HasShippedItem)
                return No(HandlingFeeReason.NoShippedItem);

            //an unknown country passes: the fee may then drop off once the address is known,
            //but it never appears for the first time at the last step of checkout
            var countries = ParseIds(settings.CountryIds);
            if (countries.Count > 0 && context.CountryId.HasValue && !countries.Contains(context.CountryId.Value))
                return No(HandlingFeeReason.CountryNotIncluded);

            //same for a shipping method not chosen yet
            var methods = ParseTokens(settings.ShippingMethodsJson);
            if (methods.Count > 0 && (context.ShippingProviderSystemName != null || context.ShippingOptionName != null)
                && !methods.Any(token => MatchesShippingToken(token, context)))
                return No(HandlingFeeReason.ShippingMethodNotIncluded);

            //a shipping charge of any size absorbs the fee entirely
            //a null shipping total means no method chosen yet, which counts as no charge
            if (settings.SuppressWhenShippingCharged && (context.ShippingTotal ?? decimal.Zero) > decimal.Zero)
                return No(HandlingFeeReason.ShippingCharged);

            decimal fee;
            switch (settings.FeeType)
            {
                case HandlingFeeType.Tiered:
                    var band = ParseTiers(settings.TiersJson).FirstOrDefault(tier => measured <= tier.UpTo);
                    if (band == null)
                        return No(HandlingFeeReason.AboveThreshold);

                    fee = band.Fee;
                    break;

                case HandlingFeeType.Percentage:
                    //"at or below" the threshold
                    if (measured > settings.ThresholdAmount)
                        return No(HandlingFeeReason.AboveThreshold);

                    fee = Math.Round(measured * settings.FeePercent / 100m, 2, MidpointRounding.AwayFromZero);
                    if (settings.FeeMinimum > decimal.Zero && fee < settings.FeeMinimum)
                        fee = settings.FeeMinimum;
                    if (settings.FeeMaximum > decimal.Zero && fee > settings.FeeMaximum)
                        fee = settings.FeeMaximum;
                    break;

                default:
                    //"at or below" the threshold
                    if (measured > settings.ThresholdAmount)
                        return No(HandlingFeeReason.AboveThreshold);

                    fee = settings.FeeAmount;
                    break;
            }

            //defence in depth: a misconfigured (e.g. negative) amount must never make an
            //order cheaper. Validation on the settings form should already stop this at the
            //source, but the rule must not trust that it always will.
            if (fee <= decimal.Zero)
                return No(HandlingFeeReason.ZeroFee);

            return new HandlingFeeDecision(fee, HandlingFeeReason.Applied, measured);
        }

        /// <summary>
        /// Parse a comma-separated list of identifiers, ignoring anything that is not a positive integer
        /// </summary>
        public static ISet<int> ParseIds(string ids)
        {
            return new HashSet<int>((ids ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(id => int.TryParse(id, out var value) ? value : 0)
                .Where(id => id > 0));
        }

        /// <summary>
        /// Join identifiers into the comma-separated form stored in settings
        /// </summary>
        public static string JoinIds(IEnumerable<int> ids)
        {
            return string.Join(",", (ids ?? Enumerable.Empty<int>()).Where(id => id > 0).Distinct().OrderBy(id => id));
        }

        /// <summary>
        /// Parse the shipping method tokens; malformed JSON reads as "no filter" rather than throwing into checkout
        /// </summary>
        public static IList<string> ParseTokens(string json)
        {
            return Deserialise<List<string>>(json)?.Where(token => !string.IsNullOrWhiteSpace(token)).ToList()
                ?? new List<string>();
        }

        /// <summary>
        /// Parse the tier bands, sorted by upper limit, dropping bands that could never apply
        /// </summary>
        public static IList<HandlingFeeTier> ParseTiers(string json)
        {
            return (Deserialise<List<HandlingFeeTier>>(json) ?? new List<HandlingFeeTier>())
                .Where(tier => tier != null && tier.UpTo >= decimal.Zero)
                .OrderBy(tier => tier.UpTo)
                .ToList();
        }

        /// <summary>
        /// Serialise tier bands into the stored form
        /// </summary>
        public static string SerialiseTiers(IEnumerable<HandlingFeeTier> tiers)
        {
            return JsonSerializer.Serialize((tiers ?? Enumerable.Empty<HandlingFeeTier>()).OrderBy(tier => tier.UpTo).ToList());
        }

        /// <summary>
        /// Serialise shipping method tokens into the stored form
        /// </summary>
        public static string SerialiseTokens(IEnumerable<string> tokens)
        {
            return JsonSerializer.Serialize((tokens ?? Enumerable.Empty<string>())
                .Where(token => !string.IsNullOrWhiteSpace(token)).Distinct().ToList());
        }

        #endregion

        #region Utilities

        private static bool MatchesShippingToken(string token, HandlingFeeContext context)
        {
            const string providerPrefix = "provider:";
            const string methodPrefix = "method:";

            if (token.StartsWith(providerPrefix, StringComparison.OrdinalIgnoreCase))
                return string.Equals(token[providerPrefix.Length..], context.ShippingProviderSystemName, StringComparison.OrdinalIgnoreCase);

            if (token.StartsWith(methodPrefix, StringComparison.OrdinalIgnoreCase))
                return string.Equals(token[methodPrefix.Length..], context.ShippingOptionName, StringComparison.OrdinalIgnoreCase);

            return false;
        }

        private static T Deserialise<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonSerializer.Deserialize<T>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion
    }
}
