using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Stores;
using Nop.Plugin.Misc.HandlingFee.Services;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Tax;

namespace Nop.Plugin.Misc.HandlingFee.Tests
{
    public static class TotalsHarness
    {
        public static async Task<(decimal total, decimal fee)> ComputeAsync(
            decimal subtotal, decimal? shipping, bool requiresShipping,
            string paymentMethodSystemName, decimal giftCardBalance = 0m)
        {
            var settings = new HandlingFeeSettings
            {
                Enabled = true,
                ThresholdAmount = 50m,
                FeeAmount = 4.95m,
                SuppressWhenShippingCharged = true
            };

            var customer = new Customer();
            var store = new Store { Id = 1 };

            var customerService = new Mock<ICustomerService>();
            customerService.Setup(x => x.GetShoppingCartCustomerAsync(It.IsAny<IList<ShoppingCartItem>>()))
                .ReturnsAsync(customer);

            var storeContext = new Mock<IStoreContext>();
            storeContext.Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(store);

            var genericAttributeService = new Mock<IGenericAttributeService>();
            genericAttributeService.Setup(x => x.GetAttributeAsync<string>(
                    It.IsAny<Customer>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
                .ReturnsAsync(paymentMethodSystemName);

            //taxable is off for these assertions, so the fee passes through unchanged
            var taxService = new Mock<ITaxService>();
            taxService.Setup(x => x.GetPaymentMethodAdditionalFeeAsync(
                    It.IsAny<decimal>(), It.IsAny<bool>(), It.IsAny<Customer>()))
                .ReturnsAsync((decimal price, bool _, Customer _) => (price, decimal.Zero));

            var cartService = new Mock<IShoppingCartService>();
            cartService.Setup(x => x.ShoppingCartRequiresShippingAsync(It.IsAny<IList<ShoppingCartItem>>()))
                .ReturnsAsync(requiresShipping);

            var settingService = new Mock<ISettingService>();
            settingService.Setup(x => x.LoadSettingAsync<HandlingFeeSettings>(It.IsAny<int>()))
                .ReturnsAsync(settings);

            //the inner totals service the payment service consults for subtotal and shipping
            var innerTotals = new Mock<IOrderTotalCalculationService>();
            innerTotals.Setup(x => x.GetShoppingCartSubTotalAsync(It.IsAny<IList<ShoppingCartItem>>(), false))
                .ReturnsAsync((0m, new List<Nop.Core.Domain.Discounts.Discount>(), subtotal, subtotal,
                    new SortedDictionary<decimal, decimal>()));
            innerTotals.Setup(x => x.GetShoppingCartShippingTotalAsync(It.IsAny<IList<ShoppingCartItem>>(), false))
                .ReturnsAsync((shipping, 0m, new List<Nop.Core.Domain.Discounts.Discount>()));

            var provider = new Mock<System.IServiceProvider>();
            provider.Setup(x => x.GetService(typeof(IOrderTotalCalculationService)))
                .Returns(innerTotals.Object);

            var paymentService = new HandlingFeePaymentService(
                customerService.Object,
                new Mock<IHttpContextAccessor>().Object,
                new Mock<IPaymentPluginManager>().Object,
                new Mock<IPriceCalculationService>().Object,
                new PaymentSettings(),
                new ShoppingCartSettings(),
                provider.Object,
                cartService.Object,
                settingService.Object,
                storeContext.Object);

            //Pass the mocks above for customerService, genericAttributeService, paymentService,
            //storeContext and taxService. Pass new ShoppingCartSettings { RoundPricesDuringCalculation = false }.
            //Every other base constructor parameter may be null: those dependencies are only
            //reached through the six methods TestableTotalsService overrides.
            var service = new TestableTotalsService(subtotal, shipping, giftCardBalance,
                null, //catalogSettings
                null, //addressService
                null, //checkoutAttributeParser
                customerService.Object,
                null, //discountService
                genericAttributeService.Object,
                null, //giftCardService
                null, //orderService
                paymentService,
                null, //priceCalculationService
                null, //productService
                null, //rewardPointService
                null, //shippingPluginManager
                null, //shippingService
                null, //shoppingCartService
                storeContext.Object,
                taxService.Object,
                null, //workContext
                null, //rewardPointsSettings
                null, //shippingSettings
                new ShoppingCartSettings { RoundPricesDuringCalculation = false },
                null); //taxSettings

            var cart = new List<ShoppingCartItem>();
            var fee = await paymentService.GetAdditionalHandlingFeeAsync(cart, paymentMethodSystemName);
            var (total, _, _, _, _, _) = await service.GetShoppingCartTotalAsync(cart);

            return (total ?? decimal.Zero, fee);
        }
    }
}
