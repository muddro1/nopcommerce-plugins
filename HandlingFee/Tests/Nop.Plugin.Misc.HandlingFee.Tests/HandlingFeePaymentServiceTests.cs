using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
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
            decimal subtotal, decimal? shipping, bool requiresShipping)
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
                new Mock<IHttpContextAccessor>().Object,
                new Mock<IPaymentPluginManager>().Object,
                new Mock<IPriceCalculationService>().Object,
                new PaymentSettings(),
                new ShoppingCartSettings(),
                provider.Object,
                cartService.Object,
                settingService.Object,
                storeContext.Object);
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
        public async Task Adds_nothing_when_disabled()
        {
            var service = Build(Settings(enabled: false), subtotal: 30m, shipping: 0m, requiresShipping: true);
            var fee = await service.GetAdditionalHandlingFeeAsync(new List<ShoppingCartItem>(), string.Empty);
            fee.Should().Be(0m);
        }
    }
}
