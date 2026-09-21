using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Security;
using Nop.Core.Domain.Seo;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Vendors;
using Nop.Core.Caching;
using Nop.Plugin.Misc.AttributeCombinationPricing.Services;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Security;
using Nop.Services.Seo;
using Nop.Services.Shipping.Date;
using Nop.Services.Tax;
using Nop.Services.Vendors;
using Nop.Web.Factories;
using Nop.Web.Models.Catalog;

namespace Nop.Plugin.Misc.AttributeCombinationPricing.Factories
{
    /// <summary>
    /// Corrects the storefront price-adjustment label shown next to an "Associated to product"
    /// attribute value, so it reflects the matching attribute combination's Overridden price rather
    /// than the associated product's own standalone price. See
    /// docs/superpowers/specs/2026-09-20-attribute-combination-pricing-design.md.
    /// </summary>
    public class AttributeCombinationProductModelFactory : ProductModelFactory
    {
        private readonly ICurrencyService _currencyService;
        private readonly ICustomerService _customerService;
        private readonly IPermissionService _permissionService;
        private readonly IPriceCalculationService _priceCalculationService;
        private readonly IPriceFormatter _priceFormatter;
        private readonly IProductAttributeParser _productAttributeParser;
        private readonly IProductAttributeService _productAttributeService;
        private readonly ITaxService _taxService;
        private readonly IWorkContext _workContext;

        public AttributeCombinationProductModelFactory(CaptchaSettings captchaSettings,
            CatalogSettings catalogSettings,
            CustomerSettings customerSettings,
            ICategoryService categoryService,
            ICurrencyService currencyService,
            ICustomerService customerService,
            IDateRangeService dateRangeService,
            IDateTimeHelper dateTimeHelper,
            IDownloadService downloadService,
            IGenericAttributeService genericAttributeService,
            ILocalizationService localizationService,
            IManufacturerService manufacturerService,
            IPermissionService permissionService,
            IPictureService pictureService,
            IPriceCalculationService priceCalculationService,
            IPriceFormatter priceFormatter,
            IProductAttributeParser productAttributeParser,
            IProductAttributeService productAttributeService,
            IProductService productService,
            IProductTagService productTagService,
            IProductTemplateService productTemplateService,
            IReviewTypeService reviewTypeService,
            ISpecificationAttributeService specificationAttributeService,
            IStaticCacheManager staticCacheManager,
            IStoreContext storeContext,
            IShoppingCartModelFactory shoppingCartModelFactory,
            ITaxService taxService,
            IUrlRecordService urlRecordService,
            IVendorService vendorService,
            IWebHelper webHelper,
            IWorkContext workContext,
            MediaSettings mediaSettings,
            OrderSettings orderSettings,
            SeoSettings seoSettings,
            ShippingSettings shippingSettings,
            VendorSettings vendorSettings)
            : base(captchaSettings, catalogSettings, customerSettings, categoryService, currencyService,
                customerService, dateRangeService, dateTimeHelper, downloadService, genericAttributeService,
                localizationService, manufacturerService, permissionService, pictureService,
                priceCalculationService, priceFormatter, productAttributeParser, productAttributeService,
                productService, productTagService, productTemplateService, reviewTypeService,
                specificationAttributeService, staticCacheManager, storeContext, shoppingCartModelFactory,
                taxService, urlRecordService, vendorService, webHelper, workContext, mediaSettings,
                orderSettings, seoSettings, shippingSettings, vendorSettings)
        {
            _currencyService = currencyService;
            _customerService = customerService;
            _permissionService = permissionService;
            _priceCalculationService = priceCalculationService;
            _priceFormatter = priceFormatter;
            _productAttributeParser = productAttributeParser;
            _productAttributeService = productAttributeService;
            _taxService = taxService;
            _workContext = workContext;
        }

        protected override async Task<IList<ProductDetailsModel.ProductAttributeModel>> PrepareProductAttributeModelsAsync(Product product, ShoppingCartItem updatecartitem)
        {
            var model = await base.PrepareProductAttributeModelsAsync(product, updatecartitem);

            //mirrors the same permission check core's own PrepareProductAttributeModelsAsync uses before
            //it builds any price label - if prices aren't shown, there's nothing to correct
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.DisplayPrices))
                return model;

            //mirrors core's own customer resolution for the same method
            var customer = updatecartitem?.CustomerId is null
                ? await _workContext.GetCurrentCustomerAsync()
                : await _customerService.GetCustomerByIdAsync(updatecartitem.CustomerId);

            await ApplyCorrectedAdjustmentsAsync(product, model, customer);

            return model;
        }

        /// <summary>
        /// For every "Associated to product" value in <paramref name="model"/>, replaces the price
        /// adjustment label with the delta the matching attribute combination's Overridden price makes
        /// against the product's own base price, when such a combination exists. Values with no matching
        /// override combination, and every <see cref="AttributeValueType.Simple"/> value, are left exactly
        /// as <see cref="ProductModelFactory"/> produced them.
        /// </summary>
        public virtual async Task ApplyCorrectedAdjustmentsAsync(Product product,
            IList<ProductDetailsModel.ProductAttributeModel> model, Customer customer)
        {
            var (_, basePrice, _, _) = await _priceCalculationService.GetFinalPriceAsync(product, customer);

            foreach (var attributeModel in model)
            {
                var rawValues = await _productAttributeService.GetProductAttributeValuesAsync(attributeModel.Id);
                var associatedToProductValues = rawValues
                    .Where(v => v.AttributeValueType == AttributeValueType.AssociatedToProduct)
                    .ToList();

                if (!associatedToProductValues.Any())
                    continue;

                var mapping = await _productAttributeService.GetProductAttributeMappingByIdAsync(attributeModel.Id);

                foreach (var valueModel in attributeModel.Values)
                {
                    var rawValue = associatedToProductValues.FirstOrDefault(v => v.Id == valueModel.Id);
                    if (rawValue == null)
                        continue;

                    var candidateXml = _productAttributeParser.AddProductAttribute(string.Empty, mapping, rawValue.Id.ToString());
                    var combination = await _productAttributeParser.FindProductAttributeCombinationAsync(product, candidateXml);

                    var correctedAdjustment = AttributeCombinationPriceAdjustmentCalculator
                        .TryGetCorrectedAdjustment(basePrice, combination?.OverriddenPrice);

                    if (!correctedAdjustment.HasValue)
                        continue;

                    var (priceAdjustmentBase, _) = await _taxService.GetProductPriceAsync(product, correctedAdjustment.Value);
                    var priceAdjustment = await _currencyService.ConvertFromPrimaryStoreCurrencyAsync(
                        priceAdjustmentBase, await _workContext.GetWorkingCurrencyAsync());

                    valueModel.PriceAdjustment = priceAdjustmentBase switch
                    {
                        > 0 => "+" + await _priceFormatter.FormatPriceAsync(priceAdjustment, false, false),
                        < 0 => "-" + await _priceFormatter.FormatPriceAsync(-priceAdjustment, false, false),
                        _ => string.Empty
                    };
                    valueModel.PriceAdjustmentValue = priceAdjustment;
                }
            }
        }
    }
}
