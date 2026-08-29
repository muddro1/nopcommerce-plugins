using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Misc.HandlingFee.Services;
using Nop.Services.Orders;
using NUnit.Framework;

namespace Nop.Plugin.Misc.HandlingFee.Tests
{
    /// <summary>
    /// Asserts the FINAL ORDER TOTAL rather than just the fee, so that the interaction
    /// with tax and gift cards is covered rather than assumed.
    /// </summary>
    [TestFixture]
    public class HandlingFeeTotalsTests
    {
        [Test]
        public async Task Fee_reaches_the_total_when_no_payment_method_is_selected()
        {
            //the cart page selects no payment method; core would hide the fee here
            var (total, _) = await TotalsHarness.ComputeAsync(
                subtotal: 30m, shipping: 0m, requiresShipping: true, paymentMethodSystemName: string.Empty);

            total.Should().Be(34.95m);
        }

        [Test]
        public async Task Fee_is_absent_once_paid_shipping_is_chosen()
        {
            var (total, _) = await TotalsHarness.ComputeAsync(
                subtotal: 30m, shipping: 8m, requiresShipping: true, paymentMethodSystemName: string.Empty);

            total.Should().Be(38m);
        }

        [Test]
        public async Task Large_order_paid_mostly_by_gift_card_still_pays_no_fee()
        {
            var (total, _) = await TotalsHarness.ComputeAsync(
                subtotal: 100m, shipping: 0m, requiresShipping: true,
                paymentMethodSystemName: string.Empty, giftCardBalance: 80m);

            //threshold saw £100, so no fee; gift card then pays £80 of it
            total.Should().Be(20m);
        }

        [Test]
        public async Task Small_order_paid_by_gift_card_still_pays_the_fee()
        {
            var (total, fee) = await TotalsHarness.ComputeAsync(
                subtotal: 30m, shipping: 0m, requiresShipping: true,
                paymentMethodSystemName: string.Empty, giftCardBalance: 80m);

            fee.Should().Be(4.95m);
            total.Should().Be(0m);
        }

        [Test]
        public async Task Downloadable_only_order_pays_no_fee()
        {
            var (total, _) = await TotalsHarness.ComputeAsync(
                subtotal: 30m, shipping: 0m, requiresShipping: false, paymentMethodSystemName: string.Empty);

            total.Should().Be(30m);
        }
    }
}
