using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Shipping;
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
using Nop.Web.Models.Checkout;

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
        #region Fields

        private readonly ICurrencyService _handlingFeeCurrencyService;
        private readonly IPaymentService _handlingFeePaymentService;
        private readonly IPriceFormatter _handlingFeePriceFormatter;
        private readonly ITaxService _handlingFeeTaxService;
        private readonly IWorkContext _handlingFeeWorkContext;

        #endregion

        #region Ctor

        public HandlingFeeCheckoutModelFactory(AddressSettings addressSettings,
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
            ShippingSettings shippingSettings)
            : base(addressSettings,
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
                shippingSettings)
        {
            //the base class keeps its dependencies private, so we hold our own references
            _handlingFeeCurrencyService = currencyService;
            _handlingFeePaymentService = paymentService;
            _handlingFeePriceFormatter = priceFormatter;
            _handlingFeeTaxService = taxService;
            _handlingFeeWorkContext = workContext;
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
            if (_handlingFeePaymentService is not HandlingFeePaymentService handlingFeeService)
                return model;

            var customer = await _handlingFeeWorkContext.GetCurrentCustomerAsync();
            var currency = await _handlingFeeWorkContext.GetWorkingCurrencyAsync();

            foreach (var paymentMethod in model.PaymentMethods)
            {
                //the method's genuine surcharge, without our handling fee folded in
                var ownFee = await handlingFeeService
                    .GetPaymentMethodOwnFeeAsync(cart, paymentMethod.PaymentMethodSystemName);

                var (rateBase, _) = await _handlingFeeTaxService
                    .GetPaymentMethodAdditionalFeeAsync(ownFee, customer);
                var rate = await _handlingFeeCurrencyService
                    .ConvertFromPrimaryStoreCurrencyAsync(rateBase, currency);

                //mirrors the base class: no fee line at all when the rate is zero
                paymentMethod.Fee = rate > decimal.Zero
                    ? await _handlingFeePriceFormatter.FormatPaymentMethodAdditionalFeeAsync(rate, true)
                    : string.Empty;
            }

            return model;
        }

        #endregion
    }
}
