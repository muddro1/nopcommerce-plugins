using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Orders;
using Nop.Services.Payments;

namespace Nop.Plugin.Misc.HandlingFee.Services
{
    /// <summary>
    /// Adds the handling fee to nopCommerce's payment method additional fee.
    /// Riding that channel means tax treatment, persistence on the order and display
    /// in the cart, admin, emails and invoices all work with no further code.
    /// </summary>
    public class HandlingFeePaymentService : PaymentService
    {
        #region Fields

        private readonly HandlingFeeContextFactory _contextFactory;
        private readonly IServiceProvider _serviceProvider;
        private readonly IShoppingCartService _shoppingCartService;
        private readonly ISettingService _settingService;
        private readonly IStoreContext _storeContext;

        #endregion

        #region Ctor

        public HandlingFeePaymentService(ICustomerService customerService,
            IPaymentPluginManager paymentPluginManager,
            IPriceCalculationService priceCalculationService,
            PaymentSettings paymentSettings,
            ShoppingCartSettings shoppingCartSettings,
            IServiceProvider serviceProvider,
            IShoppingCartService shoppingCartService,
            ISettingService settingService,
            IStoreContext storeContext,
            HandlingFeeContextFactory contextFactory)
            : base(customerService, paymentPluginManager,
                priceCalculationService, paymentSettings, shoppingCartSettings)
        {
            _contextFactory = contextFactory;
            _serviceProvider = serviceProvider;
            _shoppingCartService = shoppingCartService;
            _settingService = settingService;
            _storeContext = storeContext;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets the additional handling fee, with our handling fee added to it
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <param name="paymentMethodSystemName">Payment method system name</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the fee</returns>
        public override async Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart, string paymentMethodSystemName)
        {
            var fee = await base.GetAdditionalHandlingFeeAsync(cart, paymentMethodSystemName);

            var store = await _storeContext.GetCurrentStoreAsync();
            var settings = await _settingService.LoadSettingAsync<HandlingFeeSettings>(store?.Id ?? 0);

            //bail out before doing any work at all when the plugin is off,
            //so that a disabled or uninstalled-but-present plugin costs nothing
            if (settings == null || !settings.Enabled)
                return fee;

            //cheap check first: when a shipped item is required, a cart that needs no shipping
            //never attracts the fee, so there is no reason to pay for the totals calls the context
            //needs - each is called once per active payment method when rendering the payment
            //method page, and shipping internally recomputes the subtotal again when "free
            //shipping over X" is enabled, so both are expensive. Downloadable-only carts should
            //cost nothing extra. (Leaving excluded items out can only remove shipped items, so
            //this shortcut holds in either exclusion mode.)
            if (settings.RequireShippedItem && !await _shoppingCartService.ShoppingCartRequiresShippingAsync(cart))
                return fee;

            //resolved lazily rather than injected, to avoid a DI cycle with
            //HandlingFeeOrderTotalCalculationService, which depends on IPaymentService
            var orderTotalCalculationService = _serviceProvider.GetRequiredService<IOrderTotalCalculationService>();

            var context = await _contextFactory.CreateAsync(cart, settings, orderTotalCalculationService);

            var decision = HandlingFeeRule.Evaluate(settings, context);
            //no currency passed: RoundPriceAsync then uses the primary store currency's rule, which
            //is the currency every amount in the settings, and the fee itself, is held in
            var handlingFee = HandlingFeeRule.NeedsCurrencyRounding(settings, decision, _shoppingCartSettings.RoundPricesDuringCalculation)
                ? await _priceCalculationService.RoundPriceAsync(decision.Fee)
                : decision.Fee;

            return fee + handlingFee;
        }


        /// <summary>
        /// The payment method's OWN additional fee, without this plugin's handling fee
        /// folded in. Used by the checkout model factory so the payment method selection
        /// page shows each method's real surcharge rather than our once-per-order fee.
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <param name="paymentMethodSystemName">Payment method system name</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the fee</returns>
        public virtual async Task<decimal> GetPaymentMethodOwnFeeAsync(IList<ShoppingCartItem> cart, string paymentMethodSystemName)
        {
            return await base.GetAdditionalHandlingFeeAsync(cart, paymentMethodSystemName);
        }

        #endregion
    }
}
