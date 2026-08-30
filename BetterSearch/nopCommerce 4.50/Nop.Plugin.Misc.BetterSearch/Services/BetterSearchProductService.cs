using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Discounts;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Security;
using Nop.Services.Shipping.Date;
using Nop.Services.Stores;

namespace Nop.Plugin.Misc.BetterSearch.Services
{
    /// <summary>
    /// Overrides <see cref="ProductService.SearchProductsAsync"/> so results come from the Lucene
    /// index rather than the stock keyword query.
    ///
    /// The index RANKS, nopCommerce FILTERS - never the other way round. The base class has no
    /// id-list overload, so the trick used here is to call it with the caller's original
    /// arguments but <c>keywords: null</c>. That makes the base query apply every filter it
    /// normally would - published, ACL, store mapping, availability dates, category,
    /// manufacturer, vendor, price range, specification filters - while matching no keyword.
    /// The result is then intersected with the index's ids. A product the index has never heard
    /// of, or one the base query excludes for any reason, never reaches the caller: the index is
    /// a snapshot and does not know a product was unpublished, moved out of a store, or had its
    /// ACL changed since the last write.
    ///
    /// Any failure talking to the index - unavailable, or an outright exception - degrades to
    /// plain <see cref="ProductService"/> behaviour. An index problem must never reach a page
    /// render.
    /// </summary>
    public class BetterSearchProductService : ProductService
    {
        #region Fields

        //base class fields are private and unreachable, so the override keeps its own copies of
        //what it needs
        private readonly SearchIndexManager _searchIndexManager;
        private readonly ISettingService _settingService;
        private readonly IStoreContext _storeContext;

        #endregion

        #region Ctor

        public BetterSearchProductService(CatalogSettings catalogSettings,
            CommonSettings commonSettings,
            IAclService aclService,
            ICustomerService customerService,
            IDateRangeService dateRangeService,
            ILanguageService languageService,
            ILocalizationService localizationService,
            IProductAttributeParser productAttributeParser,
            IProductAttributeService productAttributeService,
            IRepository<CrossSellProduct> crossSellProductRepository,
            IRepository<DiscountProductMapping> discountProductMappingRepository,
            IRepository<LocalizedProperty> localizedPropertyRepository,
            IRepository<Product> productRepository,
            IRepository<ProductAttributeCombination> productAttributeCombinationRepository,
            IRepository<ProductAttributeMapping> productAttributeMappingRepository,
            IRepository<ProductCategory> productCategoryRepository,
            IRepository<ProductManufacturer> productManufacturerRepository,
            IRepository<ProductPicture> productPictureRepository,
            IRepository<ProductProductTagMapping> productTagMappingRepository,
            IRepository<ProductReview> productReviewRepository,
            IRepository<ProductReviewHelpfulness> productReviewHelpfulnessRepository,
            IRepository<ProductSpecificationAttribute> productSpecificationAttributeRepository,
            IRepository<ProductTag> productTagRepository,
            IRepository<ProductWarehouseInventory> productWarehouseInventoryRepository,
            IRepository<RelatedProduct> relatedProductRepository,
            IRepository<Shipment> shipmentRepository,
            IRepository<StockQuantityHistory> stockQuantityHistoryRepository,
            IRepository<TierPrice> tierPriceRepository,
            IRepository<Warehouse> warehouseRepository,
            IStaticCacheManager staticCacheManager,
            IStoreService storeService,
            IStoreMappingService storeMappingService,
            IWorkContext workContext,
            LocalizationSettings localizationSettings,
            SearchIndexManager searchIndexManager,
            ISettingService settingService,
            IStoreContext storeContext)
            : base(catalogSettings, commonSettings, aclService, customerService, dateRangeService,
                languageService, localizationService, productAttributeParser, productAttributeService,
                crossSellProductRepository, discountProductMappingRepository, localizedPropertyRepository,
                productRepository, productAttributeCombinationRepository, productAttributeMappingRepository,
                productCategoryRepository, productManufacturerRepository, productPictureRepository,
                productTagMappingRepository, productReviewRepository, productReviewHelpfulnessRepository,
                productSpecificationAttributeRepository, productTagRepository, productWarehouseInventoryRepository,
                relatedProductRepository, shipmentRepository, stockQuantityHistoryRepository, tierPriceRepository,
                warehouseRepository, staticCacheManager, storeService, storeMappingService, workContext,
                localizationSettings)
        {
            _searchIndexManager = searchIndexManager;
            _settingService = settingService;
            _storeContext = storeContext;
        }

        #endregion

        #region Methods

        /// <inheritdoc />
        public override async Task<IPagedList<Product>> SearchProductsAsync(
            int pageIndex = 0,
            int pageSize = int.MaxValue,
            IList<int> categoryIds = null,
            IList<int> manufacturerIds = null,
            int storeId = 0,
            int vendorId = 0,
            int warehouseId = 0,
            ProductType? productType = null,
            bool visibleIndividuallyOnly = false,
            bool excludeFeaturedProducts = false,
            decimal? priceMin = null,
            decimal? priceMax = null,
            int productTagId = 0,
            string keywords = null,
            bool searchDescriptions = false,
            bool searchManufacturerPartNumber = true,
            bool searchSku = true,
            bool searchProductTags = false,
            int languageId = 0,
            IList<SpecificationAttributeOption> filteredSpecOptions = null,
            ProductSortingEnum orderBy = ProductSortingEnum.Position,
            bool showHidden = false,
            bool? overridePublished = null)
        {
            Task<IPagedList<Product>> DelegateToBase() => base.SearchProductsAsync(
                pageIndex, pageSize, categoryIds, manufacturerIds, storeId, vendorId, warehouseId,
                productType, visibleIndividuallyOnly, excludeFeaturedProducts, priceMin, priceMax,
                productTagId, keywords, searchDescriptions, searchManufacturerPartNumber, searchSku,
                searchProductTags, languageId, filteredSpecOptions, orderBy, showHidden, overridePublished);

            //rule 2: nothing to rank on - stock behaviour handles a blank keyword the same way
            //it always has
            if (string.IsNullOrWhiteSpace(keywords))
                return await DelegateToBase();

            var settings = await _settingService.LoadSettingAsync<BetterSearchSettings>(
                (await _storeContext.GetCurrentStoreAsync()).Id);

            //rule 1: master switch off - the index is never touched
            if (!settings.Enabled)
                return await DelegateToBase();

            IList<int> indexIds;

            try
            {
                //rule 3: an unavailable index degrades to stock search rather than searching
                //nothing
                if (!await _searchIndexManager.IsAvailableAsync())
                    return await DelegateToBase();

                indexIds = await _searchIndexManager.SearchAsync(keywords, Math.Max(1, settings.MaxIndexResults));
            }
            catch
            {
                //rule 4: an index failure must never reach a page render
                return await DelegateToBase();
            }

            //the base query, run with every filter it normally applies but matching no keyword -
            //this is what keeps ACL, store mapping, publish state and every other filter intact.
            //A large page pulls the whole filtered set so it can be intersected with the index ids
            //below; pagination of the combined result happens afterwards.
            var filtered = await base.SearchProductsAsync(
                0, int.MaxValue, categoryIds, manufacturerIds, storeId, vendorId, warehouseId,
                productType, visibleIndividuallyOnly, excludeFeaturedProducts, priceMin, priceMax,
                productTagId, null, searchDescriptions, searchManufacturerPartNumber, searchSku,
                searchProductTags, languageId, filteredSpecOptions, orderBy, showHidden, overridePublished);

            //real Lucene will not produce duplicate ids, but an index anomaly must never reach a
            //page render (rule 4's point, extended to malformed results as well as thrown ones) -
            //Distinct() plus a duplicate-tolerant map keeps a repeated id from throwing or from
            //emitting the same product twice
            var distinctIndexIds = indexIds.Distinct().ToList();
            var indexIdSet = new HashSet<int>(distinctIndexIds);

            List<Product> result;

            if (orderBy == ProductSortingEnum.Position)
            {
                //rule 5: re-sort the survivors into index order - the index ranks, base only filters
                var byId = new Dictionary<int, Product>();
                foreach (var product in filtered.Where(p => indexIdSet.Contains(p.Id)))
                    byId[product.Id] = product;

                result = distinctIndexIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
            }
            else
            {
                //rule 6: the caller asked for a specific sort - keep the base query's order,
                //restricted to products the index actually matched
                result = filtered.Where(p => indexIdSet.Contains(p.Id)).ToList();
            }

            return new PagedList<Product>(result, pageIndex, pageSize);
        }

        #endregion
    }
}
