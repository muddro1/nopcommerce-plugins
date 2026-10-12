using System.Collections.Generic;
using FluentAssertions;
using Nop.Plugin.Misc.HandlingFee;
using Nop.Plugin.Misc.HandlingFee.Services;
using Nop.Plugin.Misc.HandlingFee.Validators;
using NUnit.Framework;

namespace Nop.Plugin.Misc.HandlingFee.Tests
{
    [TestFixture]
    public class HandlingFeeRuleDescriberTests
    {
        private static string Money(decimal amount) => "$" + amount.ToString("0.00");

        private static HandlingFeeSettings Settings()
        {
            return new HandlingFeeSettings { Enabled = true, ThresholdAmount = 50m, FeeAmount = 4.95m };
        }

        [Test]
        public void Describes_the_200_rule_in_one_sentence_plus_no_restrictions()
        {
            HandlingFeeRuleDescriber.Describe(Settings(), null, Money).Should().Equal(
                "Charge $4.95 when an order with at least one shipped item has a subtotal (after discounts) of $50.00 or less and ships for free.",
                "Any shipping method, any destination, any customer, any products.");
        }

        [Test]
        public void Says_when_the_fee_is_switched_off()
        {
            var settings = Settings();
            settings.Enabled = false;

            HandlingFeeRuleDescriber.Describe(settings, null, Money)[0].Should().StartWith("The handling fee is switched off");
        }

        [Test]
        public void Describes_percentage_with_clamps_and_the_other_basis()
        {
            var settings = Settings();
            settings.FeeType = HandlingFeeType.Percentage;
            settings.FeePercent = 7.5m;
            settings.FeeMinimum = 2m;
            settings.FeeMaximum = 6m;
            settings.SubtotalBasis = HandlingFeeSubtotalBasis.BeforeOrderDiscounts;
            settings.RequireShippedItem = false;
            settings.SuppressWhenShippingCharged = false;

            HandlingFeeRuleDescriber.Describe(settings, null, Money)[0].Should().Be(
                "Charge 7.5% of the subtotal (at least $2.00, at most $6.00) when any order has a subtotal (before order discounts) of $50.00 or less.");
        }

        [Test]
        public void Describes_tiers_in_order()
        {
            var settings = Settings();
            settings.FeeType = HandlingFeeType.Tiered;
            settings.TiersJson = "[{\"UpTo\":50,\"Fee\":3.95},{\"UpTo\":25,\"Fee\":5.95}]";

            HandlingFeeRuleDescriber.Describe(settings, null, Money)[0].Should().Be(
                "Charge $5.95 up to $25.00, $3.95 up to $50.00 when an order with at least one shipped item has a subtotal (after discounts) " +
                "in that band and ships for free. Nothing above $50.00.");
        }

        [Test]
        public void Names_restrictions_and_shows_ids_for_anything_unknown()
        {
            var settings = Settings();
            settings.ShippingMethodsJson = HandlingFeeRule.SerialiseTokens(new[] { "method:Free Shipping", "provider:Shipping.UPS" });
            settings.CountryIds = "1";
            settings.ExemptCustomerRoleIds = "3,99";
            settings.ExcludedCategoryIds = "5";
            settings.ExcludedProductIds = "8";

            var names = new HandlingFeeRuleNames
            {
                ShippingMethods = new Dictionary<string, string> { ["method:Free Shipping"] = "Free Shipping", ["provider:Shipping.UPS"] = "UPS (any service)" },
                Countries = new Dictionary<int, string> { [1] = "United States" },
                Roles = new Dictionary<int, string> { [3] = "Wholesale" },
                Categories = new Dictionary<int, string> { [5] = "Gift Cards" },
                Products = new Dictionary<int, string> { [8] = "Sample pack" }
            };

            HandlingFeeRuleDescriber.Describe(settings, names, Money).Should().Equal(
                "Charge $4.95 when an order with at least one shipped item has a subtotal (after discounts) of $50.00 or less and ships for free.",
                "Only with these shipping methods: Free Shipping, UPS (any service).",
                "Only when shipping to: United States.",
                "Never charged to: Wholesale, #99.",
                "Not charged when the cart contains: Gift Cards (and its subcategories), Sample pack.");
        }

        [Test]
        public void Explains_a_decision()
        {
            HandlingFeeRuleDescriber.Explain(new HandlingFeeDecision(4.95m, HandlingFeeReason.Applied, 30m), Money)
                .Should().Be("Charged $4.95 (measured subtotal $30.00).");
            HandlingFeeRuleDescriber.Explain(new HandlingFeeDecision(0m, HandlingFeeReason.AboveThreshold, 60m), Money)
                .Should().Be("No fee: the measured subtotal of $60.00 is above the limit.");
        }

        [TestCase("[{\"UpTo\":25,\"Fee\":5.95}]", true)]
        [TestCase("[]", false)] //no bands
        [TestCase("", false)]
        [TestCase("nonsense", false)]
        [TestCase("[{\"UpTo\":25,\"Fee\":0}]", false)] //zero fee
        [TestCase("[{\"UpTo\":-1,\"Fee\":2}]", false)] //blank limit from the band table
        [TestCase("[{\"UpTo\":25,\"Fee\":2},{\"UpTo\":25,\"Fee\":3}]", false)] //duplicate limit
        public void Tier_validation(string json, bool valid)
        {
            ConfigurationModelValidator.AreValidTiers(json).Should().Be(valid);
        }
    }
}
