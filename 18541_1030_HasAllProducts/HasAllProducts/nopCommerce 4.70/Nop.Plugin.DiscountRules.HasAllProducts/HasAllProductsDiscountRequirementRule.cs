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

namespace Nop.Plugin.DiscountRules.HasAllProducts;

public partial class HasAllProductsDiscountRequirementRule : BasePlugin, IDiscountRequirementRule
{
    #region Fields

    private readonly IActionContextAccessor _actionContextAccessor;
    private readonly IDiscountService _discountService;
    private readonly ILocalizationService _localizationService;
    private readonly ISettingService _settingService;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly IUrlHelperFactory _urlHelperFactory;
    private readonly IWebHelper _webHelper;
    private static readonly char[] _separator = [','];

    #endregion

    #region Ctor

    public HasAllProductsDiscountRequirementRule(IActionContextAccessor actionContextAccessor,
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
        ArgumentNullException.ThrowIfNull(request);

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
            .Split(_separator, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .ToList();
        if (!restrictedProducts.Any())
            return result;

        //group products in the cart by product ID
        //it could be the same product with distinct product attributes
        //that's why we get the total quantity of this product
        var cart = (await _shoppingCartService.GetShoppingCartAsync(customer: request.Customer, shoppingCartType: ShoppingCartType.ShoppingCart, storeId: request.Store.Id))
            .GroupBy(sci => sci.ProductId)
            .Select(g => new { ProductId = g.Key, TotalQuantity = g.Sum(x => x.Quantity) });

        var allFound = true;
        foreach (var restrictedProduct in restrictedProducts)
        {
            if (string.IsNullOrWhiteSpace(restrictedProduct))
                continue;

            var found1 = false;
            foreach (var sci in cart)
            {
                if (restrictedProduct.Contains(':'))
                {
                    if (restrictedProduct.Contains('-'))
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

            if (!found1)
            {
                allFound = false;
                break;
            }
        }

        if (allFound)
        {
            //valid
            result.IsValid = true;
            return result;
        }

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

        return urlHelper.Action("Configure", "DiscountRulesHasAllProducts",
            new { discountId = discountId, discountRequirementId = discountRequirementId }, _webHelper.GetCurrentRequestProtocol());
    }

    /// <summary>
    /// Install the plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task InstallAsync()
    {
        //locales
        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            ["Plugins.DiscountRules.HasAllProducts.Fields.Products"] = "Restricted products [and quantity range]",
            ["Plugins.DiscountRules.HasAllProducts.Fields.Products.Hint"] = "The comma-separated list of product identifiers (e.g. 77, 123, 156). You can find a product ID on its details page. You can also specify the comma-separated list of product identifiers with quantities ({Product ID}:{Quantity}. for example, 77:1, 123:2, 156:3). And you can also specify the comma-separated list of product identifiers with quantity range ({Product ID}:{Min quantity}-{Max quantity}. for example, 77:1-3, 123:2-5, 156:3-8).",
            ["Plugins.DiscountRules.HasAllProducts.Fields.Products.AddNew"] = "Add product",
            ["Plugins.DiscountRules.HasAllProducts.Fields.Products.Choose"] = "Choose",
            ["Plugins.DiscountRules.HasAllProducts.Fields.ProductIds.Required"] = "Products are required",
            ["Plugins.DiscountRules.HasAllProducts.Fields.ProductIds.InvalidFormat"] = "Invalid format of the products selection. Format should be comma-separated list of product identifiers (e.g. 77, 123, 156). You can find a product ID on its details page. You can also specify the comma-separated list of product identifiers with quantities ({Product ID}:{Quantity}. for example, 77:1, 123:2, 156:3). And you can also specify the comma-separated list of product identifiers with quantity range ({Product ID}:{Min quantity}-{Max quantity}. for example, 77:1-3, 123:2-5, 156:3-8).",
            ["Plugins.DiscountRules.HasAllProducts.Fields.DiscountId.Required"] = "Discount is required"
        });

        await base.InstallAsync();
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
        await _localizationService.DeleteLocaleResourcesAsync("Plugins.DiscountRules.HasAllProducts");

        await base.UninstallAsync();
    }

    #endregion
}