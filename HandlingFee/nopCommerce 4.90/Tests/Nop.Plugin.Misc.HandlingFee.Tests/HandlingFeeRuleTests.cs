using FluentAssertions;
using Nop.Plugin.Misc.HandlingFee;
using Nop.Plugin.Misc.HandlingFee.Services;
using NUnit.Framework;

namespace Nop.Plugin.Misc.HandlingFee.Tests
{
    [TestFixture]
    public class HandlingFeeRuleTests
    {
        private static HandlingFeeSettings Settings()
        {
            return new HandlingFeeSettings
            {
                Enabled = true,
                ThresholdAmount = 50m,
                FeeAmount = 4.95m
            };
        }

        private static HandlingFeeContext Cart(decimal subtotal = 30m)
        {
            return new HandlingFeeContext
            {
                SubtotalAfterDiscounts = subtotal,
                SubtotalBeforeOrderDiscounts = subtotal,
                ShippingTotal = 0m,
                HasShippedItem = true
            };
        }

        private static HandlingFeeDecision Evaluate(HandlingFeeSettings settings, HandlingFeeContext context)
        {
            return HandlingFeeRule.Evaluate(settings, context);
        }

        #region Upgrading changes nothing

        [TestCase(30, 0, true, 4.95)] //physical, free shipping
        [TestCase(30, 5, true, 0)] //physical, paid shipping
        [TestCase(100, 0, true, 0)] //above threshold
        [TestCase(30, 0, false, 0)] //downloadable only
        [TestCase(50, 0, true, 4.95)] //exactly on the threshold
        public void Default_210_settings_reproduce_the_200_worked_examples(decimal subtotal, decimal shipping,
            bool ships, decimal expected)
        {
            //a settings object carrying only what a 2.00 store had: everything added in 2.10 is at its default
            var context = Cart(subtotal) with { ShippingTotal = shipping, HasShippedItem = ships };

            Evaluate(Settings(), context).Fee.Should().Be(expected);
        }

        #endregion

        #region Conditions

        [Test]
        public void Applies_and_reports_the_measured_subtotal()
        {
            Evaluate(Settings(), Cart()).Should().Be(new HandlingFeeDecision(4.95m, HandlingFeeReason.Applied, 30m));
        }

        [Test]
        public void Disabled()
        {
            var settings = Settings();
            settings.Enabled = false;

            Evaluate(settings, Cart()).Reason.Should().Be(HandlingFeeReason.Disabled);
            Evaluate(null, Cart()).Reason.Should().Be(HandlingFeeReason.Disabled);
        }

        [Test]
        public void An_exempt_role_waives_the_fee()
        {
            var settings = Settings();
            settings.ExemptCustomerRoleIds = "7, 9";

            Evaluate(settings, Cart() with { CustomerRoleIds = new[] { 3, 9 } }).Reason.Should().Be(HandlingFeeReason.ExemptRole);
            Evaluate(settings, Cart() with { CustomerRoleIds = new[] { 3 } }).Reason.Should().Be(HandlingFeeReason.Applied);
        }

        [Test]
        public void An_excluded_item_waives_the_fee_in_waive_mode()
        {
            Evaluate(Settings(), Cart() with { HasExcludedItem = true }).Reason.Should().Be(HandlingFeeReason.ExcludedItem);
        }

        [Test]
        public void An_excluded_item_is_ignored_by_the_rule_in_leave_out_mode()
        {
            //in leave-out mode the context factory has already removed excluded items from the
            //subtotal and the shipped-item check, so the flag itself must not waive the fee
            var settings = Settings();
            settings.ExclusionMode = HandlingFeeExclusionMode.LeaveOut;

            Evaluate(settings, Cart() with { HasExcludedItem = true }).Reason.Should().Be(HandlingFeeReason.Applied);
        }

        [Test]
        public void No_shipped_item_can_be_allowed()
        {
            var settings = Settings();
            Evaluate(settings, Cart() with { HasShippedItem = false }).Reason.Should().Be(HandlingFeeReason.NoShippedItem);

            settings.RequireShippedItem = false;
            Evaluate(settings, Cart() with { HasShippedItem = false }).Reason.Should().Be(HandlingFeeReason.Applied);
        }

        [Test]
        public void Country_filter()
        {
            var settings = Settings();
            settings.CountryIds = "1,2";

            Evaluate(settings, Cart() with { CountryId = 2 }).Reason.Should().Be(HandlingFeeReason.Applied);
            Evaluate(settings, Cart() with { CountryId = 5 }).Reason.Should().Be(HandlingFeeReason.CountryNotIncluded);
        }

        [Test]
        public void An_unknown_country_passes()
        {
            var settings = Settings();
            settings.CountryIds = "1";

            Evaluate(settings, Cart() with { CountryId = null }).Reason.Should().Be(HandlingFeeReason.Applied);
        }

        [Test]
        public void Shipping_method_filter_matches_a_named_method_or_a_whole_provider()
        {
            var settings = Settings();
            settings.ShippingMethodsJson = HandlingFeeRule.SerialiseTokens(new[] { "method:Free Shipping", "provider:Shipping.UPS" });

            Evaluate(settings, Cart() with { ShippingProviderSystemName = "Shipping.FixedByWeightByTotal", ShippingOptionName = "free shipping" })
                .Reason.Should().Be(HandlingFeeReason.Applied);
            Evaluate(settings, Cart() with { ShippingProviderSystemName = "Shipping.UPS", ShippingOptionName = "UPS Ground" })
                .Reason.Should().Be(HandlingFeeReason.Applied);
            Evaluate(settings, Cart() with { ShippingProviderSystemName = "Shipping.FedEx", ShippingOptionName = "FedEx 2Day" })
                .Reason.Should().Be(HandlingFeeReason.ShippingMethodNotIncluded);
        }

        [Test]
        public void An_unchosen_shipping_method_passes()
        {
            var settings = Settings();
            settings.ShippingMethodsJson = HandlingFeeRule.SerialiseTokens(new[] { "method:Free Shipping" });

            Evaluate(settings, Cart()).Reason.Should().Be(HandlingFeeReason.Applied);
        }

        [Test]
        public void Shipping_charge_suppression_is_unchanged()
        {
            var settings = Settings();
            Evaluate(settings, Cart() with { ShippingTotal = 0.01m }).Reason.Should().Be(HandlingFeeReason.ShippingCharged);
            Evaluate(settings, Cart() with { ShippingTotal = null }).Reason.Should().Be(HandlingFeeReason.Applied);

            settings.SuppressWhenShippingCharged = false;
            Evaluate(settings, Cart() with { ShippingTotal = 8m }).Reason.Should().Be(HandlingFeeReason.Applied);
        }

        [Test]
        public void Subtotal_basis_picks_which_subtotal_is_measured()
        {
            var settings = Settings();
            var context = Cart() with { SubtotalAfterDiscounts = 45m, SubtotalBeforeOrderDiscounts = 60m };

            Evaluate(settings, context).Reason.Should().Be(HandlingFeeReason.Applied);

            settings.SubtotalBasis = HandlingFeeSubtotalBasis.BeforeOrderDiscounts;
            Evaluate(settings, context).Should().Be(new HandlingFeeDecision(0m, HandlingFeeReason.AboveThreshold, 60m));
        }

        #endregion

        #region Fee types

        [Test]
        public void Percentage_fee_is_rounded_and_clamped()
        {
            var settings = Settings();
            settings.FeeType = HandlingFeeType.Percentage;
            settings.FeePercent = 10m;

            Evaluate(settings, Cart(33.35m)).Fee.Should().Be(3.34m); //3.335 rounds away from zero

            settings.FeeMinimum = 4m;
            Evaluate(settings, Cart(20m)).Fee.Should().Be(4m);

            settings.FeeMaximum = 4.50m;
            Evaluate(settings, Cart(49m)).Fee.Should().Be(4.50m);

            Evaluate(settings, Cart(50.01m)).Reason.Should().Be(HandlingFeeReason.AboveThreshold);
        }

        [Test]
        public void Percentage_of_a_zero_subtotal_with_no_minimum_is_not_charged()
        {
            var settings = Settings();
            settings.FeeType = HandlingFeeType.Percentage;
            settings.FeePercent = 10m;

            Evaluate(settings, Cart(0m)).Reason.Should().Be(HandlingFeeReason.ZeroFee);
        }

        [TestCase(0, 5.95)]
        [TestCase(25, 5.95)] //exactly on a band edge belongs to that band
        [TestCase(25.01, 3.95)]
        [TestCase(50, 3.95)]
        public void Tiered_fee_picks_the_first_band_at_or_above_the_subtotal(decimal subtotal, decimal expected)
        {
            var settings = Settings();
            settings.FeeType = HandlingFeeType.Tiered;
            //stored out of order on purpose: bands are sorted when read
            settings.TiersJson = "[{\"UpTo\":50,\"Fee\":3.95},{\"UpTo\":25,\"Fee\":5.95}]";

            Evaluate(settings, Cart(subtotal)).Fee.Should().Be(expected);
        }

        [Test]
        public void Tiered_fee_ignores_the_flat_threshold_and_stops_above_the_top_band()
        {
            var settings = Settings();
            settings.FeeType = HandlingFeeType.Tiered;
            settings.ThresholdAmount = 1000m;
            settings.TiersJson = HandlingFeeRule.SerialiseTiers(new[] { new HandlingFeeTier { UpTo = 25m, Fee = 5.95m } });

            Evaluate(settings, Cart(30m)).Reason.Should().Be(HandlingFeeReason.AboveThreshold);
        }

        [Test]
        public void Malformed_stored_lists_never_throw_into_checkout()
        {
            var settings = Settings();
            settings.FeeType = HandlingFeeType.Tiered;
            settings.TiersJson = "{not json";
            settings.ShippingMethodsJson = "also not json";
            settings.CountryIds = "a, ,-3";

            Evaluate(settings, Cart() with { ShippingOptionName = "x", CountryId = 4 })
                .Reason.Should().Be(HandlingFeeReason.AboveThreshold); //no bands = nothing applies
        }

        #endregion
    }
}
