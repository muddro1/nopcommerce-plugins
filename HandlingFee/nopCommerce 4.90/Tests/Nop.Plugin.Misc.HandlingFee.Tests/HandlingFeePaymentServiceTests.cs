using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Discounts;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Stores;
using Nop.Plugin.Misc.HandlingFee.Services;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Orders;
using Nop.Services.Payments;
using NUnit.Framework;

namespace Nop.Plugin.Misc.HandlingFee.Tests
{
    [TestFixture]
    public class HandlingFeePaymentServiceTests
    {
        private static HandlingFeePaymentService Build(HandlingFeeSettings settings,
            decimal subtotal, decimal? shipping, bool requiresShipping,
            IPriceCalculationService priceCalculationService = null,
            bool roundPricesDuringCalculation = false)
        {
            var totals = new Mock<IOrderTotalCalculationService>();
            totals.Setup(x => x.GetShoppingCartSubTotalAsync(It.IsAny<IList<ShoppingCartItem>>(), false))
                .ReturnsAsync((0m, new List<Discount>(), subtotal, subtotal,
                    new SortedDictionary<decimal, decimal>()));
            totals.Setup(x => x.GetShoppingCartShippingTotalAsync(It.IsAny<IList<ShoppingCartItem>>(), false))
                .ReturnsAsync((shipping, 0m, new List<Discount>()));

            var provider = new Mock<IServiceProvider>();
            provider.Setup(x => x.GetService(typeof(IOrderTotalCalculationService))).Returns(totals.Object);

            var cartService = new Mock<IShoppingCartService>();
            cartService.Setup(x => x.ShoppingCartRequiresShippingAsync(It.IsAny<IList<ShoppingCartItem>>()))
                .ReturnsAsync(requiresShipping);

            var settingService = new Mock<ISettingService>();
            settingService.Setup(x => x.LoadSettingAsync<HandlingFeeSettings>(It.IsAny<int>()))
                .ReturnsAsync(settings);

            var storeContext = new Mock<IStoreContext>();
            storeContext.Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Store { Id = 1 });

            return new HandlingFeePaymentService(
                new Mock<ICustomerService>().Object,
                new Mock<IPaymentPluginManager>().Object,
                priceCalculationService ?? new Mock<IPriceCalculationService>().Object,
                new PaymentSettings(),
                new ShoppingCartSettings { RoundPricesDuringCalculation = roundPricesDuringCalculation },
                provider.Object,
                cartService.Object,
                settingService.Object,
                storeContext.Object,
                ContextFactories.Build(null, cartService.Object, storeContext.Object));
        }

        private static HandlingFeeSettings Settings(bool enabled = true)
        {
            return new HandlingFeeSettings
            {
                Enabled = enabled,
                ThresholdAmount = 50m,
                FeeAmount = 4.95m,
                SuppressWhenShippingCharged = true
            };
        }

        [Test]
        public async Task Adds_the_fee_for_a_small_physical_order_with_free_shipping()
        {
            var service = Build(Settings(), subtotal: 30m, shipping: 0m, requiresShipping: true);
            var fee = await service.GetAdditionalHandlingFeeAsync(new List<ShoppingCartItem>(), string.Empty);
            fee.Should().Be(4.95m);
        }

        [Test]
        public async Task Adds_nothing_when_shipping_is_charged()
        {
            var service = Build(Settings(), subtotal: 30m, shipping: 8m, requiresShipping: true);
            var fee = await service.GetAdditionalHandlingFeeAsync(new List<ShoppingCartItem>(), string.Empty);
            fee.Should().Be(0m);
        }

        [Test]
        public async Task Adds_nothing_for_a_downloadable_only_order()
        {
            var service = Build(Settings(), subtotal: 30m, shipping: 0m, requiresShipping: false);
            var fee = await service.GetAdditionalHandlingFeeAsync(new List<ShoppingCartItem>(), string.Empty);
            fee.Should().Be(0m);
        }

        [Test]
        public async Task A_percentage_fee_goes_through_the_currency_rounding()
        {
            //a currency that rounds up to the next 0.05, as CHF does
            var rounding = new Mock<IPriceCalculationService>();
            rounding.Setup(x => x.RoundPriceAsync(It.IsAny<decimal>(), null))
                .ReturnsAsync((decimal value, Nop.Core.Domain.Directory.Currency _) => Math.Ceiling(value * 20m) / 20m);

            var settings = Settings();
            settings.FeeType = HandlingFeeType.Percentage;
            settings.FeePercent = 10m;

            //10% of 33.35 is 3.335, which the rule takes to 3.34 and the currency to 3.35
            var service = Build(settings, subtotal: 33.35m, shipping: 0m, requiresShipping: true, rounding.Object,
                roundPricesDuringCalculation: true);
            var fee = await service.GetAdditionalHandlingFeeAsync(new List<ShoppingCartItem>(), string.Empty);
            fee.Should().Be(3.35m);
        }

        [Test]
        public async Task A_percentage_fee_is_not_currency_rounded_when_the_store_does_not_round_during_calculation()
        {
            var rounding = new Mock<IPriceCalculationService>();
            rounding.Setup(x => x.RoundPriceAsync(It.IsAny<decimal>(), null)).ReturnsAsync(999m);

            var settings = Settings();
            settings.FeeType = HandlingFeeType.Percentage;
            settings.FeePercent = 10m;

            var service = Build(settings, subtotal: 33.35m, shipping: 0m, requiresShipping: true, rounding.Object,
                roundPricesDuringCalculation: false);
            var fee = await service.GetAdditionalHandlingFeeAsync(new List<ShoppingCartItem>(), string.Empty);

            fee.Should().Be(3.34m);
            rounding.Verify(x => x.RoundPriceAsync(It.IsAny<decimal>(), It.IsAny<Nop.Core.Domain.Directory.Currency>()), Times.Never);
        }

        [Test]
        public async Task A_flat_fee_is_charged_exactly_as_entered()
        {
            var rounding = new Mock<IPriceCalculationService>();
            rounding.Setup(x => x.RoundPriceAsync(It.IsAny<decimal>(), null)).ReturnsAsync(999m);

            var service = Build(Settings(), subtotal: 30m, shipping: 0m, requiresShipping: true, rounding.Object,
                roundPricesDuringCalculation: true);
            var fee = await service.GetAdditionalHandlingFeeAsync(new List<ShoppingCartItem>(), string.Empty);

            fee.Should().Be(4.95m);
            rounding.Verify(x => x.RoundPriceAsync(It.IsAny<decimal>(), It.IsAny<Nop.Core.Domain.Directory.Currency>()), Times.Never);
        }

        [Test]
        public async Task Adds_nothing_when_disabled()
        {
            var service = Build(Settings(enabled: false), subtotal: 30m, shipping: 0m, requiresShipping: true);
            var fee = await service.GetAdditionalHandlingFeeAsync(new List<ShoppingCartItem>(), string.Empty);
            fee.Should().Be(0m);
        }
    }
}
