using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Security;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Tax;
using Nop.Core;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Shipping.Pickup;
using Nop.Services.Shipping;
using Nop.Services.Stores;
using Nop.Services.Tax;
using Nop.Web.Models.Checkout;
using Nop.Web.Models.Common;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Plugin.Misc.HandlingFee.Services;
using Nop.Web.Factories;

namespace Nop.Plugin.Misc.HandlingFee.Factories
{
    /// <summary>
    /// Corrects the payment method selection page.
    ///
    /// nopCommerce asks every active payment method for its own surcharge so it can list
    /// it beside the method. Our handling fee answers the same amount for all of them,
    /// which makes each method look as though it charges the fee. It does not - the fee is
    /// charged once per order regardless of the method chosen.
    ///
    /// This class fixes the DISPLAY only. It deliberately does not touch the charging path:
    /// the fee still reaches the order total and the order columns exactly as before.
    /// It post-processes the model rather than reimplementing the base method, so there is
    /// no duplicated core logic to re-diff on a nopCommerce upgrade.
    /// </summary>
    public class HandlingFeeCheckoutModelFactory : CheckoutModelFactory
    {
        #region Ctor

        public HandlingFeeCheckoutModelFactory(AddressSettings addressSettings,
            CaptchaSettings captchaSettings,
            CommonSettings commonSettings,
            IAddressModelFactory addressModelFactory,
            IAddressService addressService,
            ICountryService countryService,
            ICurrencyService currencyService,
            ICustomerService customerService,
            IGenericAttributeService genericAttributeService,
            ILocalizationService localizationService,
            IOrderProcessingService orderProcessingService,
            IOrderTotalCalculationService orderTotalCalculationService,
            IPaymentPluginManager paymentPluginManager,
            IPaymentService paymentService,
            IPickupPluginManager pickupPluginManager,
            IPriceFormatter priceFormatter,
            IRewardPointService rewardPointService,
            IShippingPluginManager shippingPluginManager,
            IShippingService shippingService,
            IShoppingCartService shoppingCartService,
            IStateProvinceService stateProvinceService,
            IStoreContext storeContext,
            IStoreMappingService storeMappingService,
            ITaxService taxService,
            IWorkContext workContext,
            OrderSettings orderSettings,
            PaymentSettings paymentSettings,
            RewardPointsSettings rewardPointsSettings,
            ShippingSettings shippingSettings,
            TaxSettings taxSettings)
            : base(addressSettings,
                captchaSettings,
                commonSettings,
                addressModelFactory,
                addressService,
                countryService,
                currencyService,
                customerService,
                genericAttributeService,
                localizationService,
                orderProcessingService,
                orderTotalCalculationService,
                paymentPluginManager,
                paymentService,
                pickupPluginManager,
                priceFormatter,
                rewardPointService,
                shippingPluginManager,
                shippingService,
                shoppingCartService,
                stateProvinceService,
                storeContext,
                storeMappingService,
                taxService,
                workContext,
                orderSettings,
                paymentSettings,
                rewardPointsSettings,
                shippingSettings,
                taxSettings)
        {
            //the base class exposes its dependencies as protected fields, so none are duplicated here
        }

        #endregion

        #region Methods

        /// <summary>
        /// Prepare the payment method selection model, showing each method's OWN surcharge
        /// rather than the handling fee our payment service adds to every answer
        /// </summary>
        public override async Task<CheckoutPaymentMethodModel> PreparePaymentMethodModelAsync(
            IList<ShoppingCartItem> cart, int filterByCountryId)
        {
            var model = await base.PreparePaymentMethodModelAsync(cart, filterByCountryId);

            //if something else owns IPaymentService, the displayed fees are not ours to correct
            if (_paymentService is not HandlingFeePaymentService handlingFeeService)
                return model;

            var customer = await _workContext.GetCurrentCustomerAsync();
            var currency = await _workContext.GetWorkingCurrencyAsync();

            foreach (var paymentMethod in model.PaymentMethods)
            {
                //the method's genuine surcharge, without our handling fee folded in
                var ownFee = await handlingFeeService
                    .GetPaymentMethodOwnFeeAsync(cart, paymentMethod.PaymentMethodSystemName);

                var (rateBase, _) = await _taxService
                    .GetPaymentMethodAdditionalFeeAsync(ownFee, customer);
                var rate = await _currencyService
                    .ConvertFromPrimaryStoreCurrencyAsync(rateBase, currency);

                //mirrors the base class: no fee line at all when the rate is zero
                paymentMethod.Fee = rate > decimal.Zero
                    ? await _priceFormatter.FormatPaymentMethodAdditionalFeeAsync(rate, true)
                    : string.Empty;
            }

            return model;
        }

        #endregion
    }
}
