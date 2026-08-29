using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Discounts;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Tax;
using Nop.Plugin.Misc.HandlingFee.Services;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Discounts;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Shipping;
using Nop.Services.Tax;

namespace Nop.Plugin.Misc.HandlingFee.Tests
{
    /// <summary>
    /// Feeds fixed figures into the copied GetShoppingCartTotalAsync so that only the
    /// plugin's own logic is under test.
    /// </summary>
    public class TestableTotalsService : HandlingFeeOrderTotalCalculationService
    {
        private readonly decimal _subtotal;
        private readonly decimal? _shipping;
        private readonly decimal _giftCardBalance;

        public TestableTotalsService(decimal subtotal, decimal? shipping, decimal giftCardBalance,
            CatalogSettings catalogSettings,
            IAddressService addressService,
            ICheckoutAttributeParser checkoutAttributeParser,
            ICustomerService customerService,
            IDiscountService discountService,
            IGenericAttributeService genericAttributeService,
            IGiftCardService giftCardService,
            IOrderService orderService,
            IPaymentService paymentService,
            IPriceCalculationService priceCalculationService,
            IProductService productService,
            IRewardPointService rewardPointService,
            IShippingPluginManager shippingPluginManager,
            IShippingService shippingService,
            IShoppingCartService shoppingCartService,
            IStoreContext storeContext,
            ITaxService taxService,
            IWorkContext workContext,
            RewardPointsSettings rewardPointsSettings,
            ShippingSettings shippingSettings,
            ShoppingCartSettings shoppingCartSettings,
            TaxSettings taxSettings)
            : base(catalogSettings, addressService, checkoutAttributeParser, customerService,
                discountService, genericAttributeService, giftCardService, orderService,
                paymentService, priceCalculationService, productService, rewardPointService,
                shippingPluginManager, shippingService, shoppingCartService, storeContext,
                taxService, workContext, rewardPointsSettings, shippingSettings,
                shoppingCartSettings, taxSettings)
        {
            _subtotal = subtotal;
            _shipping = shipping;
            _giftCardBalance = giftCardBalance;
        }

        //NOTE: tuple element names must match the base signatures exactly, or the compiler
        //rejects the override with CS8139. Copy the return types verbatim from core.

        public override Task<(decimal discountAmount, List<Discount> appliedDiscounts, decimal subTotalWithoutDiscount, decimal subTotalWithDiscount, SortedDictionary<decimal, decimal> taxRates)>
            GetShoppingCartSubTotalAsync(IList<ShoppingCartItem> cart, bool includingTax)
        {
            return Task.FromResult((decimal.Zero, new List<Discount>(), _subtotal, _subtotal,
                new SortedDictionary<decimal, decimal>()));
        }

        public override Task<(decimal? shippingTotal, decimal taxRate, List<Discount> appliedDiscounts)>
            GetShoppingCartShippingTotalAsync(IList<ShoppingCartItem> cart, bool includingTax)
        {
            return Task.FromResult((_shipping, decimal.Zero, new List<Discount>()));
        }

        public override Task<(decimal taxTotal, SortedDictionary<decimal, decimal> taxRates)>
            GetTaxTotalAsync(IList<ShoppingCartItem> cart, bool usePaymentMethodAdditionalFee = true)
        {
            return Task.FromResult((decimal.Zero, new SortedDictionary<decimal, decimal>()));
        }

        protected override Task<(decimal orderDiscount, List<Discount> appliedDiscounts)>
            GetOrderTotalDiscountAsync(Customer customer, decimal orderTotal)
        {
            return Task.FromResult((decimal.Zero, new List<Discount>()));
        }

        protected override Task<decimal> AppliedGiftCardsAsync(IList<ShoppingCartItem> cart,
            List<AppliedGiftCard> appliedGiftCards, Customer customer, decimal resultTemp)
        {
            var used = resultTemp > _giftCardBalance ? _giftCardBalance : resultTemp;
            return Task.FromResult(resultTemp - used);
        }

        protected override Task<(int redeemedRewardPoints, decimal redeemedRewardPointsAmount)>
            SetRewardPointsAsync(int redeemedRewardPoints, decimal redeemedRewardPointsAmount,
                bool? useRewardPoints, Customer customer, decimal orderTotal)
        {
            return Task.FromResult((0, decimal.Zero));
        }
    }
}
