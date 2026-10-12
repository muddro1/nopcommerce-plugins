using FluentAssertions;
using Nop.Plugin.Misc.HandlingFee;
using Nop.Plugin.Misc.HandlingFee.Services;
using NUnit.Framework;

namespace Nop.Plugin.Misc.HandlingFee.Tests
{
    [TestFixture]
    public class HandlingFeeCalculatorTests
    {
        private static HandlingFeeSettings Settings(bool enabled = true, bool suppress = true)
        {
            return new HandlingFeeSettings
            {
                Enabled = enabled,
                ThresholdAmount = 50m,
                FeeAmount = 4.95m,
                SuppressWhenShippingCharged = suppress
            };
        }

        [Test]
        public void Charges_the_fee_below_the_threshold_with_free_shipping()
        {
            HandlingFeeCalculator.Calculate(Settings(), 30m, 0m, true).Should().Be(4.95m);
        }

        [Test]
        public void Charges_the_fee_exactly_at_the_threshold()
        {
            HandlingFeeCalculator.Calculate(Settings(), 50m, 0m, true).Should().Be(4.95m);
        }

        [Test]
        public void No_fee_above_the_threshold()
        {
            HandlingFeeCalculator.Calculate(Settings(), 50.01m, 0m, true).Should().Be(0m);
        }

        [Test]
        public void No_fee_when_any_shipping_is_charged()
        {
            HandlingFeeCalculator.Calculate(Settings(), 30m, 1.50m, true).Should().Be(0m);
        }

        [Test]
        public void Null_shipping_counts_as_free_shipping()
        {
            HandlingFeeCalculator.Calculate(Settings(), 30m, null, true).Should().Be(4.95m);
        }

        [Test]
        public void No_fee_when_the_cart_needs_no_shipping()
        {
            HandlingFeeCalculator.Calculate(Settings(), 30m, 0m, false).Should().Be(0m);
        }

        [Test]
        public void No_fee_when_disabled()
        {
            HandlingFeeCalculator.Calculate(Settings(enabled: false), 30m, 0m, true).Should().Be(0m);
        }

        [Test]
        public void Suppression_can_be_turned_off()
        {
            HandlingFeeCalculator.Calculate(Settings(suppress: false), 30m, 8m, true).Should().Be(4.95m);
        }

        [Test]
        public void Zero_value_goods_are_below_the_threshold()
        {
            HandlingFeeCalculator.Calculate(Settings(), 0m, 0m, true).Should().Be(4.95m);
        }

        [Test]
        public void Null_settings_yield_no_fee()
        {
            HandlingFeeCalculator.Calculate(null, 30m, 0m, true).Should().Be(0m);
        }

        [Test]
        public void Negative_fee_amount_is_never_returned()
        {
            var settings = Settings();
            settings.FeeAmount = -4.95m;
            HandlingFeeCalculator.Calculate(settings, 30m, 0m, true).Should().Be(0m);
        }

        [Test]
        public void Zero_fee_amount_returns_zero()
        {
            var settings = Settings();
            settings.FeeAmount = 0m;
            HandlingFeeCalculator.Calculate(settings, 30m, 0m, true).Should().Be(0m);
        }
    }
}
