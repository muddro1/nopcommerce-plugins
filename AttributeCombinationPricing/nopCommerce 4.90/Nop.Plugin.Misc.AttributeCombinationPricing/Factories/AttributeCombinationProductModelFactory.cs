using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Security;
using Nop.Core.Domain.Seo;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Stores;
using Nop.Core.Domain.Vendors;
using Nop.Plugin.Misc.AttributeCombinationPricing.Services;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Services.Seo;
using Nop.Services.Shipping.Date;
using Nop.Services.Stores;
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
        //the base class exposes its dependencies as protected fields, so none are duplicated here

        public AttributeCombinationProductModelFactory(CaptchaSettings captchaSettings,
            CatalogSettings catalogSettings,
            CustomerSettings customerSettings,
            ICategoryService categoryService,
            ICurrencyService currencyService,
            ICustomerService customerService,
            ICustomWishlistService customWishlistService,
            IDateRangeService dateRangeService,
            IDateTimeHelper dateTimeHelper,
            IDownloadService downloadService,
            IGenericAttributeService genericAttributeService,
            IJsonLdModelFactory jsonLdModelFactory,
            ILocalizationService localizationService,
            IManufacturerService manufacturerService,
            IPermissionService permissionService,
            IPictureService pictureService,
            IPriceCalculationService priceCalculationService,
            IPriceFormatter priceFormatter,
            IProductAttributeParser productAttributeParser,
            IProductAttributeService productAttributeService,
            IProductReviewService productReviewService,
            IProductService productService,
            IProductTagService productTagService,
            IProductTemplateService productTemplateService,
            IReviewTypeService reviewTypeService,
            IShoppingCartService shoppingCartService,
            ISpecificationAttributeService specificationAttributeService,
            IStaticCacheManager staticCacheManager,
            IStoreContext storeContext,
            IStoreService storeService,
            IShoppingCartModelFactory shoppingCartModelFactory,
            ITaxService taxService,
            IUrlRecordService urlRecordService,
            IVendorService vendorService,
            IVideoService videoService,
            IWebHelper webHelper,
            IWorkContext workContext,
            MediaSettings mediaSettings,
            OrderSettings orderSettings,
            SeoSettings seoSettings,
            ShippingSettings shippingSettings,
            VendorSettings vendorSettings)
            : base(captchaSettings, catalogSettings, customerSettings, categoryService, currencyService,
                customerService, customWishlistService, dateRangeService, dateTimeHelper, downloadService,
                genericAttributeService, jsonLdModelFactory, localizationService, manufacturerService,
                permissionService, pictureService, priceCalculationService, priceFormatter,
                productAttributeParser, productAttributeService, productReviewService, productService,
                productTagService, productTemplateService, reviewTypeService, shoppingCartService,
                specificationAttributeService, staticCacheManager, storeContext, storeService,
                shoppingCartModelFactory, taxService, urlRecordService, vendorService, videoService,
                webHelper, workContext, mediaSettings, orderSettings, seoSettings, shippingSettings,
                vendorSettings)
        {
        }

        protected override async Task<IList<ProductDetailsModel.ProductAttributeModel>> PrepareProductAttributeModelsAsync(Product product, ShoppingCartItem updatecartitem)
        {
            var model = await base.PrepareProductAttributeModelsAsync(product, updatecartitem);

            //mirrors the same permission check core's own PrepareProductAttributeModelsAsync uses before
            //it builds any price label - if prices aren't shown, there's nothing to correct
            if (!await _permissionService.AuthorizeAsync(StandardPermission.PublicStore.DISPLAY_PRICES))
                return model;

            //mirrors core's own store, customer and quantity resolution for the same method
            var store = updatecartitem != null
                ? await _storeService.GetStoreByIdAsync(updatecartitem.StoreId)
                : await _storeContext.GetCurrentStoreAsync();
            var customer = updatecartitem?.CustomerId is null
                ? await _workContext.GetCurrentCustomerAsync()
                : await _customerService.GetCustomerByIdAsync(updatecartitem.CustomerId);
            var quantity = updatecartitem?.Quantity ?? 1;

            await ApplyCorrectedAdjustmentsAsync(product, model, customer, store, quantity);

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
            IList<ProductDetailsModel.ProductAttributeModel> model, Customer customer, Store store, int quantity = 1)
        {
            var (_, basePrice, _, _) = await _priceCalculationService.GetFinalPriceAsync(product, customer, store, quantity: quantity);

            foreach (var attributeModel in model)
            {
                var rawValues = await _productAttributeService.GetProductAttributeValuesAsync(attributeModel.Id);
                var associatedToProductValues = rawValues
                    .Where(v => v.AttributeValueType == AttributeValueType.AssociatedToProduct)
                    .ToList();

                if (!associatedToProductValues.Any())
                    continue;

                var mapping = await _productAttributeService.GetProductAttributeMappingByIdAsync(attributeModel.Id);
                if (mapping == null)
                    continue;

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
