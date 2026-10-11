using Nop.Plugin.Misc.AttributeCombinationPricing.Services;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AttributeCombinationPricing.Tests
{
    [TestFixture]
    public class AttributeCombinationPriceAdjustmentCalculatorTests
    {
        [Test]
        public void Returns_null_when_no_combination_matches()
        {
            var result = AttributeCombinationPriceAdjustmentCalculator.TryGetCorrectedAdjustment(
                basePrice: 299m, combinationOverriddenPrice: null);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void Returns_the_positive_delta_when_the_override_costs_more_than_base()
        {
            var result = AttributeCombinationPriceAdjustmentCalculator.TryGetCorrectedAdjustment(
                basePrice: 299m, combinationOverriddenPrice: 320m);

            Assert.That(result, Is.EqualTo(21m));
        }

        [Test]
        public void Returns_a_negative_delta_when_the_override_costs_less_than_base()
        {
            var result = AttributeCombinationPriceAdjustmentCalculator.TryGetCorrectedAdjustment(
                basePrice: 299m, combinationOverriddenPrice: 285m);

            Assert.That(result, Is.EqualTo(-14m));
        }

        [Test]
        public void Returns_zero_when_the_override_equals_base()
        {
            var result = AttributeCombinationPriceAdjustmentCalculator.TryGetCorrectedAdjustment(
                basePrice: 299m, combinationOverriddenPrice: 299m);

            Assert.That(result, Is.EqualTo(0m));
        }
    }
}
