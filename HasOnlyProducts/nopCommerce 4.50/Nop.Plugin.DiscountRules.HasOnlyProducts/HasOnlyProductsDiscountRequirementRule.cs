using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Services.Configuration;
using Nop.Services.Discounts;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Plugins;

namespace Nop.Plugin.DiscountRules.HasOnlyProducts
{
    public partial class HasOnlyProductsDiscountRequirementRule : BasePlugin, IDiscountRequirementRule
    {
        #region Fields

        private readonly IActionContextAccessor _actionContextAccessor;
        private readonly IDiscountService _discountService;
        private readonly ILocalizationService _localizationService;
        private readonly ISettingService _settingService;
        private readonly IShoppingCartService _shoppingCartService;
        private readonly IUrlHelperFactory _urlHelperFactory;
        private readonly IWebHelper _webHelper;

        #endregion

        #region Ctor

        public HasOnlyProductsDiscountRequirementRule(IActionContextAccessor actionContextAccessor,
            IDiscountService discountService,
            ILocalizationService localizationService,
            ISettingService settingService,
            IShoppingCartService shoppingCartService,
            IUrlHelperFactory urlHelperFactory,
            IWebHelper webHelper)
        {
            _actionContextAccessor = actionContextAccessor;
            _discountService = discountService;
            _localizationService = localizationService;
            _settingService = settingService;
            _shoppingCartService = shoppingCartService;
            _urlHelperFactory = urlHelperFactory;
            _webHelper = webHelper;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Check discount requirement
        /// </summary>
        /// <param name="request">Object that contains all information required to check the requirement (Current customer, discount, etc)</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public async Task<DiscountRequirementValidationResult> CheckRequirementAsync(DiscountRequirementValidationRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            //invalid by default
            var result = new DiscountRequirementValidationResult();

            var restrictedProductIds = await _settingService.GetSettingByKeyAsync<string>(string.Format(DiscountRequirementDefaults.SETTINGS_KEY, request.DiscountRequirementId));
            if (string.IsNullOrWhiteSpace(restrictedProductIds))
            {
                //valid
                result.IsValid = true;
                return result;
            }

            if (request.Customer == null)
                return result;

            //we support three ways of specifying products:
            //1. The comma-separated list of product identifiers (e.g. 77, 123, 156).
            //2. The comma-separated list of product identifiers with quantities.
            //      {Product ID}:{Quantity}. For example, 77:1, 123:2, 156:3
            //3. The comma-separated list of product identifiers with quantity range.
            //      {Product ID}:{Min quantity}-{Max quantity}. For example, 77:1-3, 123:2-5, 156:3-8
            var restrictedProducts = restrictedProductIds
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .ToList();
            if (!restrictedProducts.Any())
                return result;

            //group products in the cart by product ID
            //it could be the same product with distinct product attributes
            //that's why we get the total quantity of this product
            var cart = (await _shoppingCartService.GetShoppingCartAsync(customer: request.Customer, shoppingCartType: ShoppingCartType.ShoppingCart, storeId: request.Store.Id))
                .GroupBy(sci => sci.ProductId)
                .Select(g => new { ProductId = g.Key, TotalQuantity = g.Sum(x => x.Quantity) })
                .ToList();

            //any of the restricted products may be enough, instead of all of them
            var matchAnyProduct = await _settingService.GetSettingByKeyAsync(string.Format(DiscountRequirementDefaults.MATCH_ANY_SETTINGS_KEY, request.DiscountRequirementId), false);

            var allFound = true;
            var anyFound = false;
            foreach (var restrictedProduct in restrictedProducts)
            {
                if (string.IsNullOrWhiteSpace(restrictedProduct))
                    continue;

                var found1 = false;
                foreach (var sci in cart)
                {
                    if (restrictedProduct.Contains(":"))
                    {
                        if (restrictedProduct.Contains("-"))
                        {
                            //the third way (the quantity rage specified)
                            //{Product ID}:{Min quantity}-{Max quantity}. For example, 77:1-3, 123:2-5, 156:3-8
                            if (!int.TryParse(restrictedProduct.Split(':')[0], out var restrictedProductId))
                                //parsing error; exit;
                                return result;
                            if (!int.TryParse(restrictedProduct.Split(':')[1].Split('-')[0], out var quantityMin))
                                //parsing error; exit;
                                return result;
                            if (!int.TryParse(restrictedProduct.Split(':')[1].Split('-')[1], out var quantityMax))
                                //parsing error; exit;
                                return result;

                            if (sci.ProductId == restrictedProductId && quantityMin <= sci.TotalQuantity && sci.TotalQuantity <= quantityMax)
                            {
                                found1 = true;
                                break;
                            }
                        }
                        else
                        {
                            //the second way (the quantity specified)
                            //{Product ID}:{Quantity}. For example, 77:1, 123:2, 156:3
                            if (!int.TryParse(restrictedProduct.Split(':')[0], out var restrictedProductId))
                                //parsing error; exit;
                                return result;

                            if (!int.TryParse(restrictedProduct.Split(':')[1], out var quantity))
                                //parsing error; exit;
                                return result;

                            if (sci.ProductId == restrictedProductId && sci.TotalQuantity == quantity)
                            {
                                found1 = true;
                                break;
                            }
                        }
                    }
                    else
                    {
                        //the first way (the quantity is not specified)
                        if (int.TryParse(restrictedProduct, out var restrictedProductId))
                        {
                            if (sci.ProductId == restrictedProductId)
                            {
                                found1 = true;
                                break;
                            }
                        }
                    }
                }

                if (found1)
                    anyFound = true;
                else
                    allFound = false;

                //when all of the products are required, stop as soon as one of them is missing
                if (!allFound && !matchAnyProduct)
                    break;

                //when any of the products is enough, stop as soon as one of them is found
                if (anyFound && matchAnyProduct)
                    break;
            }

            if (!(matchAnyProduct ? anyFound : allFound))
                return result;

            //the restricted products requirement is met
            //now check whether the cart is allowed to contain anything else
            var onlyTheseProducts = await _settingService.GetSettingByKeyAsync(string.Format(DiscountRequirementDefaults.EXCLUSIVE_SETTINGS_KEY, request.DiscountRequirementId), true);
            if (onlyTheseProducts)
            {
                var allowedProductIds = GetRestrictedProductIds(restrictedProducts);
                if (cart.Any(sci => !allowedProductIds.Contains(sci.ProductId)))
                    //the cart contains a product that is not one of the restricted ones
                    return result;
            }

            //valid
            result.IsValid = true;
            return result;
        }

        /// <summary>
        /// Get URL for rule configuration
        /// </summary>
        /// <param name="discountId">Discount identifier</param>
        /// <param name="discountRequirementId">Discount requirement identifier (if editing)</param>
        /// <returns>URL</returns>
        public string GetConfigurationUrl(int discountId, int? discountRequirementId)
        {
            var urlHelper = _urlHelperFactory.GetUrlHelper(_actionContextAccessor.ActionContext);

            return urlHelper.Action("Configure", "DiscountRulesHasOnlyProducts",
                new { discountId = discountId, discountRequirementId = discountRequirementId }, _webHelper.GetCurrentRequestProtocol());
        }

        /// <summary>
        /// Install the plugin
        /// </summary>
        /// <returns>A task that represents the asynchronous operation</returns>
        public override async Task InstallAsync()
        {
            //locales
            await AddOrUpdateLocalesAsync();

            await base.InstallAsync();
        }

        /// <summary>
        /// Update the plugin
        /// </summary>
        /// <param name="currentVersion">Current version of the plugin</param>
        /// <param name="targetVersion">New version of the plugin</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public override async Task UpdateAsync(string currentVersion, string targetVersion)
        {
            //locale resources added in later versions are missing on sites that installed an earlier one,
            //so make sure they all exist whenever the plugin is upgraded
            await AddOrUpdateLocalesAsync();

            await base.UpdateAsync(currentVersion, targetVersion);
        }

        /// <summary>
        /// Uninstall the plugin
        /// </summary>
        /// <returns>A task that represents the asynchronous operation</returns>
        public override async Task UninstallAsync()
        {
            //discount requirements
            var discountRequirements = (await _discountService.GetAllDiscountRequirementsAsync())
                .Where(discountRequirement => discountRequirement.DiscountRequirementRuleSystemName == DiscountRequirementDefaults.SYSTEM_NAME);
            foreach (var discountRequirement in discountRequirements)
            {
                await _discountService.DeleteDiscountRequirementAsync(discountRequirement, false);
            }

            //locales
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.DiscountRules.HasOnlyProducts");

            await base.UninstallAsync();
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Add or update the locale resources used by the plugin
        /// </summary>
        /// <returns>A task that represents the asynchronous operation</returns>
        private async Task AddOrUpdateLocalesAsync()
        {
            await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
            {
                ["Plugins.DiscountRules.HasOnlyProducts.Fields.Products"] = "Restricted products [and quantity range]",
                ["Plugins.DiscountRules.HasOnlyProducts.Fields.Products.Hint"] = "The comma-separated list of product identifiers (e.g. 77, 123, 156). You can find a product ID on its details page. You can also specify the comma-separated list of product identifiers with quantities ({Product ID}:{Quantity}. for example, 77:1, 123:2, 156:3). And you can also specify the comma-separated list of product identifiers with quantity range ({Product ID}:{Min quantity}-{Max quantity}. for example, 77:1-3, 123:2-5, 156:3-8).",
                ["Plugins.DiscountRules.HasOnlyProducts.Fields.Products.AddNew"] = "Add product",
                ["Plugins.DiscountRules.HasOnlyProducts.Fields.Products.Choose"] = "Choose",
                ["Plugins.DiscountRules.HasOnlyProducts.Fields.ProductIds.Required"] = "Products are required",
                ["Plugins.DiscountRules.HasOnlyProducts.Fields.ProductIds.InvalidFormat"] = "Invalid format of the products selection. Format should be comma-separated list of product identifiers (e.g. 77, 123, 156). You can find a product ID on its details page. You can also specify the comma-separated list of product identifiers with quantities ({Product ID}:{Quantity}. for example, 77:1, 123:2, 156:3). And you can also specify the comma-separated list of product identifiers with quantity range ({Product ID}:{Min quantity}-{Max quantity}. for example, 77:1-3, 123:2-5, 156:3-8).",
                ["Plugins.DiscountRules.HasOnlyProducts.Fields.MatchAnyProduct"] = "Any one of these products is enough",
                ["Plugins.DiscountRules.HasOnlyProducts.Fields.MatchAnyProduct.Hint"] = "Check to apply the discount when the cart contains at least one of the restricted products above. Leave unchecked to require all of them.",
                ["Plugins.DiscountRules.HasOnlyProducts.Fields.OnlyTheseProducts"] = "These must be the only products in the cart",
                ["Plugins.DiscountRules.HasOnlyProducts.Fields.OnlyTheseProducts.Hint"] = "Check to apply the discount only when the cart contains nothing but the restricted products above. Any other product in the cart makes the discount invalid. Uncheck to allow other products in the cart.",
                ["Plugins.DiscountRules.HasOnlyProducts.Fields.DiscountId.Required"] = "Discount is required"
            });
        }

        /// <summary>
        /// Get the identifiers of the restricted products, ignoring any specified quantity or quantity range
        /// </summary>
        /// <param name="restrictedProducts">The restricted products as configured (e.g. 77, 123:2, 156:3-8)</param>
        /// <returns>The identifiers of the restricted products</returns>
        private static HashSet<int> GetRestrictedProductIds(IEnumerable<string> restrictedProducts)
        {
            var productIds = new HashSet<int>();

            foreach (var restrictedProduct in restrictedProducts)
            {
                if (string.IsNullOrWhiteSpace(restrictedProduct))
                    continue;

                //the quantity (or the quantity range) is specified after the colon; we only need the product identifier
                var rawProductId = restrictedProduct.Contains(":")
                    ? restrictedProduct.Split(':')[0]
                    : restrictedProduct;

                if (int.TryParse(rawProductId, out var productId))
                    productIds.Add(productId);
            }

            return productIds;
        }

        #endregion
    }
}
