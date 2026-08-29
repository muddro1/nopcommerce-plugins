using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
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

        private readonly IServiceProvider _serviceProvider;
        private readonly IShoppingCartService _shoppingCartService;
        private readonly ISettingService _settingService;
        private readonly IStoreContext _storeContext;

        #endregion

        #region Ctor

        public HandlingFeePaymentService(ICustomerService customerService,
            IHttpContextAccessor httpContextAccessor,
            IPaymentPluginManager paymentPluginManager,
            IPriceCalculationService priceCalculationService,
            PaymentSettings paymentSettings,
            ShoppingCartSettings shoppingCartSettings,
            IServiceProvider serviceProvider,
            IShoppingCartService shoppingCartService,
            ISettingService settingService,
            IStoreContext storeContext)
            : base(customerService, httpContextAccessor, paymentPluginManager,
                priceCalculationService, paymentSettings, shoppingCartSettings)
        {
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

            //resolved lazily rather than injected, to avoid a DI cycle with
            //HandlingFeeOrderTotalCalculationService, which depends on IPaymentService
            var orderTotalCalculationService = _serviceProvider.GetRequiredService<IOrderTotalCalculationService>();

            var (_, _, _, subTotalWithDiscount, _) = await orderTotalCalculationService
                .GetShoppingCartSubTotalAsync(cart, false);
            var shippingTotal = (await orderTotalCalculationService
                .GetShoppingCartShippingTotalAsync(cart, false)).shippingTotal;
            var requiresShipping = await _shoppingCartService.ShoppingCartRequiresShippingAsync(cart);

            return fee + HandlingFeeCalculator.Calculate(settings, subTotalWithDiscount, shippingTotal, requiresShipping);
        }

        #endregion
    }
}
