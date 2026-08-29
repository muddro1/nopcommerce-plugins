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
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Discounts;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Shipping;
using Nop.Services.Tax;

namespace Nop.Plugin.Misc.HandlingFee.Services
{
    /// <summary>
    /// Overrides GetShoppingCartTotalAsync so the handling fee is visible before a
    /// payment method has been selected (e.g. on the cart page).
    /// </summary>
    public class HandlingFeeOrderTotalCalculationService : OrderTotalCalculationService
    {
        #region Fields

        //core's own fields backing these services are private, so the copied method body
        //below needs its own copies of the seven it touches
        private readonly ICustomerService _customerService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IPaymentService _paymentService;
        private readonly IPriceCalculationService _priceCalculationService;
        private readonly IStoreContext _storeContext;
        private readonly ITaxService _taxService;
        private readonly ShoppingCartSettings _shoppingCartSettings;

        #endregion

        #region Ctor

        public HandlingFeeOrderTotalCalculationService(CatalogSettings catalogSettings,
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
            _customerService = customerService;
            _genericAttributeService = genericAttributeService;
            _paymentService = paymentService;
            _priceCalculationService = priceCalculationService;
            _storeContext = storeContext;
            _taxService = taxService;
            _shoppingCartSettings = shoppingCartSettings;
        }

        #endregion

        #region Methods

        // Copied from Nop.Services.Orders.OrderTotalCalculationService.GetShoppingCartTotalAsync
        // (nopCommerce 4.50.2, lines 1190-1281) with one change: the !string.IsNullOrEmpty(
        // paymentMethodSystemName) condition is removed so the handling fee is visible on the
        // cart page. RE-DIFF THIS METHOD AGAINST CORE ON ANY NOPCOMMERCE UPGRADE.
        public override async Task<(decimal? shoppingCartTotal, decimal discountAmount, List<Discount> appliedDiscounts, List<AppliedGiftCard> appliedGiftCards, int redeemedRewardPoints, decimal redeemedRewardPointsAmount)> GetShoppingCartTotalAsync(IList<ShoppingCartItem> cart,
            bool? useRewardPoints = null, bool usePaymentMethodAdditionalFee = true)
        {
            var redeemedRewardPoints = 0;
            var redeemedRewardPointsAmount = decimal.Zero;

            var customer = await _customerService.GetShoppingCartCustomerAsync(cart);
            var store = await _storeContext.GetCurrentStoreAsync();
            var paymentMethodSystemName = string.Empty;

            if (customer != null)
            {
                paymentMethodSystemName = await _genericAttributeService.GetAttributeAsync<string>(customer,
                    NopCustomerDefaults.SelectedPaymentMethodAttribute, store.Id);
            }

            //subtotal without tax
            var (_, _, _, subTotalWithDiscountBase, _) = await GetShoppingCartSubTotalAsync(cart, false);
            //subtotal with discount
            var subtotalBase = subTotalWithDiscountBase;

            //shipping without tax
            var shoppingCartShipping = (await GetShoppingCartShippingTotalAsync(cart, false)).shippingTotal;

            //payment method additional fee without tax
            var paymentMethodAdditionalFeeWithoutTax = decimal.Zero;
            //the guard on paymentMethodSystemName is deliberately dropped: it hides the fee
            //on the cart page, where no payment method has been selected yet
            if (usePaymentMethodAdditionalFee)
            {
                var paymentMethodAdditionalFee = await _paymentService.GetAdditionalHandlingFeeAsync(cart,
                    paymentMethodSystemName);
                paymentMethodAdditionalFeeWithoutTax =
                    (await _taxService.GetPaymentMethodAdditionalFeeAsync(paymentMethodAdditionalFee,
                        false, customer)).price;
            }

            //tax
            var shoppingCartTax = (await GetTaxTotalAsync(cart, usePaymentMethodAdditionalFee)).taxTotal;

            //order total
            var resultTemp = decimal.Zero;
            resultTemp += subtotalBase;
            if (shoppingCartShipping.HasValue)
            {
                resultTemp += shoppingCartShipping.Value;
            }

            resultTemp += paymentMethodAdditionalFeeWithoutTax;
            resultTemp += shoppingCartTax;
            if (_shoppingCartSettings.RoundPricesDuringCalculation)
                resultTemp = await _priceCalculationService.RoundPriceAsync(resultTemp);

            //order total discount
            var (discountAmount, appliedDiscounts) = await GetOrderTotalDiscountAsync(customer, resultTemp);

            //sub totals with discount        
            if (resultTemp < discountAmount)
                discountAmount = resultTemp;

            //reduce subtotal
            resultTemp -= discountAmount;

            if (resultTemp < decimal.Zero)
                resultTemp = decimal.Zero;
            if (_shoppingCartSettings.RoundPricesDuringCalculation)
                resultTemp = await _priceCalculationService.RoundPriceAsync(resultTemp);

            //let's apply gift cards now (gift cards that can be used)
            var appliedGiftCards = new List<AppliedGiftCard>();
            resultTemp = await AppliedGiftCardsAsync(cart, appliedGiftCards, customer, resultTemp);

            if (resultTemp < decimal.Zero)
                resultTemp = decimal.Zero;
            if (_shoppingCartSettings.RoundPricesDuringCalculation)
                resultTemp = await _priceCalculationService.RoundPriceAsync(resultTemp);

            if (!shoppingCartShipping.HasValue)
            {
                //we have errors
                return (null, discountAmount, appliedDiscounts, appliedGiftCards,redeemedRewardPoints, redeemedRewardPointsAmount);
            }

            var orderTotal = resultTemp;

            //reward points
            (redeemedRewardPoints, redeemedRewardPointsAmount) = await SetRewardPointsAsync(redeemedRewardPoints, redeemedRewardPointsAmount, useRewardPoints, customer, orderTotal);

            orderTotal -= redeemedRewardPointsAmount;

            if (_shoppingCartSettings.RoundPricesDuringCalculation)
                orderTotal = await _priceCalculationService.RoundPriceAsync(orderTotal);
            return (orderTotal, discountAmount, appliedDiscounts, appliedGiftCards, redeemedRewardPoints, redeemedRewardPointsAmount);
        }

        #endregion
    }
}
