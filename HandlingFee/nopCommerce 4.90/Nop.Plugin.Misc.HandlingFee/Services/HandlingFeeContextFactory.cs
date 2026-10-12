using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Shipping;

namespace Nop.Plugin.Misc.HandlingFee.Services
{
    /// <summary>
    /// Gathers the facts <see cref="HandlingFeeRule"/> needs from a real cart.
    ///
    /// Each lookup only runs when a setting actually needs it: with the 2.00-equivalent
    /// defaults this makes exactly the calls the 2.00 payment service made (shipped-item
    /// check, subtotal, shipping total) and nothing more. That matters because the payment
    /// method page asks once per active payment method.
    /// </summary>
    public class HandlingFeeContextFactory
    {
        #region Fields

        private readonly IAddressService _addressService;
        private readonly ICategoryService _categoryService;
        private readonly ICountryService _countryService;
        private readonly ICustomerService _customerService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly ILanguageService _languageService;
        private readonly ILocalizationService _localizationService;
        private readonly IShippingMethodsService _shippingMethodsService;
        private readonly IShoppingCartService _shoppingCartService;
        private readonly IStoreContext _storeContext;

        #endregion

        #region Ctor

        public HandlingFeeContextFactory(IAddressService addressService,
            ICategoryService categoryService,
            ICountryService countryService,
            ICustomerService customerService,
            IGenericAttributeService genericAttributeService,
            ILanguageService languageService,
            ILocalizationService localizationService,
            IShippingMethodsService shippingMethodsService,
            IShoppingCartService shoppingCartService,
            IStoreContext storeContext)
        {
            _addressService = addressService;
            _categoryService = categoryService;
            _countryService = countryService;
            _customerService = customerService;
            _genericAttributeService = genericAttributeService;
            _languageService = languageService;
            _localizationService = localizationService;
            _shippingMethodsService = shippingMethodsService;
            _shoppingCartService = shoppingCartService;
            _storeContext = storeContext;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Build the rule context for a cart
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <param name="settings">Handling fee settings; decide which lookups are needed</param>
        /// <param name="orderTotalCalculationService">Totals service, passed in rather than injected
        /// to avoid a DI cycle: the totals service depends on IPaymentService, which depends on this</param>
        /// <returns>The context</returns>
        public virtual async Task<HandlingFeeContext> CreateAsync(IList<ShoppingCartItem> cart,
            HandlingFeeSettings settings,
            IOrderTotalCalculationService orderTotalCalculationService)
        {
            ArgumentNullException.ThrowIfNull(cart);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(orderTotalCalculationService);

            var customer = await _customerService.GetShoppingCartCustomerAsync(cart);

            var excludedItems = await GetExcludedItemsAsync(cart, settings);

            //in leave-out mode excluded items neither count toward the subtotal nor make the
            //order "physical"; nopCommerce's own subtotal method is re-run on the reduced cart so
            //order-subtotal discounts are re-evaluated for what is left, not approximated
            var leavingOut = settings.ExclusionMode == HandlingFeeExclusionMode.LeaveOut && excludedItems.Count > 0;
            var countedCart = leavingOut ? cart.Where(item => !excludedItems.Contains(item)).ToList() : cart;

            //a cart made up entirely of excluded items has nothing left to measure; any other cart
            //goes through the same calls 2.00 made, whatever its contents
            var nothingCounted = leavingOut && countedCart.Count == 0;

            var hasShippedItem = !nothingCounted && await _shoppingCartService.ShoppingCartRequiresShippingAsync(countedCart);

            var (subtotalBefore, subtotalAfter) = (decimal.Zero, decimal.Zero);
            if (!nothingCounted)
            {
                var (_, _, subTotalWithoutDiscount, subTotalWithDiscount, _) = await orderTotalCalculationService
                    .GetShoppingCartSubTotalAsync(countedCart, false);
                (subtotalBefore, subtotalAfter) = (subTotalWithoutDiscount, subTotalWithDiscount);
            }

            //shipping is charged on the whole order, excluded items included
            var shippingTotal = (await orderTotalCalculationService.GetShoppingCartShippingTotalAsync(cart, false)).shippingTotal;

            var (providerSystemName, optionName, shippingMethodId) = await GetSelectedShippingOptionAsync(customer, settings);

            return new HandlingFeeContext
            {
                SubtotalAfterDiscounts = subtotalAfter,
                SubtotalBeforeOrderDiscounts = subtotalBefore,
                ShippingTotal = shippingTotal,
                HasShippedItem = hasShippedItem,
                HasExcludedItem = excludedItems.Count > 0,
                OnlyExcludedItems = cart.Count > 0 && excludedItems.Count == cart.Count,
                CustomerRoleIds = await GetCustomerRoleIdsAsync(customer, settings),
                CountryId = await GetShippingCountryIdAsync(customer, settings),
                ShippingProviderSystemName = providerSystemName,
                ShippingOptionName = optionName,
                ShippingMethodId = shippingMethodId
            };
        }

        #endregion

        #region Utilities

        /// <summary>
        /// The cart items that are excluded products, or products in an excluded category or any of its subcategories
        /// </summary>
        protected virtual async Task<ISet<ShoppingCartItem>> GetExcludedItemsAsync(IList<ShoppingCartItem> cart, HandlingFeeSettings settings)
        {
            var excluded = new HashSet<ShoppingCartItem>();

            var excludedProductIds = HandlingFeeRule.ParseIds(settings.ExcludedProductIds);
            var selectedCategoryIds = HandlingFeeRule.ParseIds(settings.ExcludedCategoryIds);
            if (excludedProductIds.Count == 0 && selectedCategoryIds.Count == 0)
                return excluded;

            foreach (var item in cart.Where(item => excludedProductIds.Contains(item.ProductId)))
                excluded.Add(item);

            if (selectedCategoryIds.Count == 0)
                return excluded;

            //excluding a category excludes everything beneath it as well
            var excludedCategoryIds = new HashSet<int>(selectedCategoryIds);
            foreach (var categoryId in selectedCategoryIds)
                excludedCategoryIds.UnionWith(await _categoryService.GetChildCategoryIdsAsync(categoryId, showHidden: true));

            var productIds = cart.Select(item => item.ProductId).Distinct().ToArray();
            var productCategoryIds = await _categoryService.GetProductCategoryIdsAsync(productIds);

            foreach (var item in cart)
            {
                if (productCategoryIds.TryGetValue(item.ProductId, out var categoryIds) && categoryIds.Any(excludedCategoryIds.Contains))
                    excluded.Add(item);
            }

            return excluded;
        }

        protected virtual async Task<IReadOnlyCollection<int>> GetCustomerRoleIdsAsync(Customer customer, HandlingFeeSettings settings)
        {
            if (customer == null || HandlingFeeRule.ParseIds(settings.ExemptCustomerRoleIds).Count == 0)
                return Array.Empty<int>();

            return await _customerService.GetCustomerRoleIdsAsync(customer) ?? Array.Empty<int>();
        }

        /// <summary>
        /// The country the order goes to: the pickup point's when the customer chose pickup in
        /// store (the order then carries no shipping address, and the customer's last shipping
        /// address may be anywhere), otherwise the shipping address's. Null when not known yet,
        /// which the rule treats as passing.
        /// </summary>
        protected virtual async Task<int?> GetShippingCountryIdAsync(Customer customer, HandlingFeeSettings settings)
        {
            if (customer == null || HandlingFeeRule.ParseIds(settings.CountryIds).Count == 0)
                return null;

            var store = await _storeContext.GetCurrentStoreAsync();
            var pickupPoint = await _genericAttributeService.GetAttributeAsync<PickupPoint>(customer,
                NopCustomerDefaults.SelectedPickupPointAttribute, store.Id);
            if (pickupPoint != null)
            {
                var pickupCountry = string.IsNullOrEmpty(pickupPoint.CountryCode)
                    ? null
                    : await _countryService.GetCountryByTwoLetterIsoCodeAsync(pickupPoint.CountryCode);
                return pickupCountry?.Id;
            }

            if (customer.ShippingAddressId is not > 0)
                return null;

            var address = await _addressService.GetAddressByIdAsync(customer.ShippingAddressId.Value);
            return address?.CountryId is > 0 ? address.CountryId : null;
        }

        /// <summary>
        /// The selected shipping option, and the shipping method it was built from; nulls when
        /// none is chosen yet - which the rule treats as passing
        /// </summary>
        protected virtual async Task<(string providerSystemName, string optionName, int? shippingMethodId)> GetSelectedShippingOptionAsync(
            Customer customer, HandlingFeeSettings settings)
        {
            var tokens = HandlingFeeRule.ParseTokens(settings.ShippingMethodsJson);
            if (customer == null || tokens.Count == 0)
                return (null, null, null);

            var store = await _storeContext.GetCurrentStoreAsync();
            var option = await _genericAttributeService.GetAttributeAsync<ShippingOption>(customer,
                NopCustomerDefaults.SelectedShippingOptionAttribute, store.Id);

            var methodId = tokens.Any(token => token.StartsWith("method:", StringComparison.OrdinalIgnoreCase))
                ? await FindShippingMethodIdAsync(option?.Name)
                : null;

            return (option?.ShippingRateComputationMethodSystemName, option?.Name, methodId);
        }

        /// <summary>
        /// The shipping method whose name, in any language, is the option name the customer saw.
        /// Providers such as "Fixed or by weight and by total" name each option after a shipping
        /// method in the customer's working language, so the raw name alone misses every other language.
        /// </summary>
        protected virtual async Task<int?> FindShippingMethodIdAsync(string optionName)
        {
            if (string.IsNullOrEmpty(optionName))
                return null;

            var methods = await _shippingMethodsService.GetAllShippingMethodsAsync();

            var byRawName = methods.FirstOrDefault(method => string.Equals(method.Name, optionName, StringComparison.OrdinalIgnoreCase));
            if (byRawName != null)
                return byRawName.Id;

            foreach (var language in await _languageService.GetAllLanguagesAsync(showHidden: true))
            {
                foreach (var method in methods)
                {
                    var localizedName = await _localizationService.GetLocalizedAsync(method, x => x.Name, language.Id, false, false);
                    if (string.Equals(localizedName, optionName, StringComparison.OrdinalIgnoreCase))
                        return method.Id;
                }
            }

            return null;
        }

        #endregion
    }
}
