using System.Threading.Tasks;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Directory;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Discounts;

namespace Nop.Plugin.Misc.AttributeCombinationPricing.Services
{
    /// <summary>
    /// Corrects the raw price adjustment nopCommerce computes for an "Associated to product" attribute
    /// value, wherever it is consulted - not just the storefront product details page label that
    /// <see cref="Factories.AttributeCombinationProductModelFactory"/> already fixes, but every other
    /// caller of <see cref="IPriceCalculationService.GetProductAttributeValuePriceAdjustmentAsync"/>,
    /// including <c>ProductAttributeFormatter.FormatAttributesAsync</c> - the method behind the
    /// shopping cart, checkout review and order/PDF attribute description text.
    ///
    /// Safe for real order totals: <c>ShoppingCartService.GetUnitPriceAsync</c> only calls this method
    /// in the branch that runs when no attribute combination override matches, which is exactly the
    /// case where this override makes no change - it falls straight through to <c>base</c>.
    /// See docs/superpowers/specs/2026-09-20-attribute-combination-pricing-design.md.
    /// </summary>
    public class AttributeCombinationPriceCalculationService : PriceCalculationService
    {
        private readonly IProductAttributeParser _productAttributeParser;
        private readonly IProductAttributeService _productAttributeService;

        public AttributeCombinationPriceCalculationService(CatalogSettings catalogSettings,
            CurrencySettings currencySettings,
            ICategoryService categoryService,
            ICurrencyService currencyService,
            ICustomerService customerService,
            IDiscountService discountService,
            IManufacturerService manufacturerService,
            IProductAttributeParser productAttributeParser,
            IProductService productService,
            IStaticCacheManager staticCacheManager,
            IStoreContext storeContext,
            IProductAttributeService productAttributeService)
            : base(catalogSettings, currencySettings, categoryService, currencyService, customerService,
                discountService, manufacturerService, productAttributeParser, productService,
                staticCacheManager, storeContext)
        {
            _productAttributeParser = productAttributeParser;
            _productAttributeService = productAttributeService;
        }

        public override async Task<decimal> GetProductAttributeValuePriceAdjustmentAsync(Product product,
            ProductAttributeValue value, Customer customer, decimal? productPrice = null)
        {
            if (value?.AttributeValueType == AttributeValueType.AssociatedToProduct)
            {
                var mapping = await _productAttributeService.GetProductAttributeMappingByIdAsync(value.ProductAttributeMappingId);
                if (mapping != null)
                {
                    var candidateXml = _productAttributeParser.AddProductAttribute(string.Empty, mapping, value.Id.ToString());
                    var combination = await _productAttributeParser.FindProductAttributeCombinationAsync(product, candidateXml);

                    if (combination?.OverriddenPrice.HasValue ?? false)
                    {
                        var basePrice = productPrice ?? (await GetFinalPriceAsync(product, customer)).finalPrice;
                        var correctedAdjustment = AttributeCombinationPriceAdjustmentCalculator
                            .TryGetCorrectedAdjustment(basePrice, combination.OverriddenPrice);

                        if (correctedAdjustment.HasValue)
                            return correctedAdjustment.Value;
                    }
                }
            }

            return await base.GetProductAttributeValuePriceAdjustmentAsync(product, value, customer, productPrice);
        }
    }
}
