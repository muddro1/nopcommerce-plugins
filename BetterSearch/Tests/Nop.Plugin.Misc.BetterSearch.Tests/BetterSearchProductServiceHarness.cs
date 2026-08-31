using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Discounts;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Stores;
using Nop.Data;
using Nop.Plugin.Misc.BetterSearch.Services;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Security;
using Nop.Services.Shipping.Date;
using Nop.Services.Stores;

namespace Nop.Plugin.Misc.BetterSearch.Tests
{
    /// <summary>
    /// Builds a <see cref="BetterSearchProductService"/> whose base-class dependencies are Moq
    /// objects, so tests exercise the real <see cref="ProductService.SearchProductsAsync"/>
    /// filtering logic - the whole point of the override - rather than assuming it.
    ///
    /// Only the dependencies that method actually touches when called with <c>keywords: null</c>
    /// (the product repository, ACL, store mapping and work context) are configured with
    /// meaningful behaviour; everything else the base constructor demands but this method never
    /// reaches is a bare, unconfigured mock.
    /// </summary>
    internal class BetterSearchProductServiceHarness
    {
        public Mock<SearchIndexManager> SearchIndexManager { get; } = new Mock<SearchIndexManager>((string)null);
        public Mock<ISettingService> SettingService { get; } = new Mock<ISettingService>();
        public Mock<IStoreContext> StoreContext { get; } = new Mock<IStoreContext>();
        public Mock<ILogger> Logger { get; } = new Mock<ILogger>();
        public List<Product> Products { get; } = new List<Product>();

        public BetterSearchSettings Settings { get; set; } = new BetterSearchSettings { Enabled = true };

        public BetterSearchProductServiceHarness()
        {
            var store = new Store { Id = 1 };
            StoreContext.Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(store);

            SettingService.Setup(x => x.LoadSettingAsync<BetterSearchSettings>(It.IsAny<int>()))
                .Returns(() => Task.FromResult(Settings));

            var productRepository = new Mock<IRepository<Product>>();
            productRepository.Setup(x => x.Table).Returns(() => Products.AsQueryable());

            var storeMappingService = new Mock<IStoreMappingService>();
            storeMappingService
                .Setup(x => x.ApplyStoreMapping(It.IsAny<IQueryable<Product>>(), It.IsAny<int>()))
                .Returns((IQueryable<Product> query, int _) => Task.FromResult(query));

            var customer = new Customer();
            var workContext = new Mock<IWorkContext>();
            workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
            workContext.Setup(x => x.GetWorkingLanguageAsync()).ReturnsAsync(new Language { Id = 1 });

            var aclService = new Mock<IAclService>();
            aclService
                .Setup(x => x.ApplyAcl(It.IsAny<IQueryable<Product>>(), It.IsAny<Customer>()))
                .Returns((IQueryable<Product> query, Customer _) => Task.FromResult(query));

            //only reached when a test delegates straight to base with the original (non-null)
            //keywords, exercising the base class's own keyword-search branch
            var languageService = new Mock<ILanguageService>();
            languageService.Setup(x => x.GetAllLanguagesAsync(It.IsAny<bool>(), It.IsAny<int>()))
                .ReturnsAsync(new List<Language>());

            var productAttributeCombinationRepository = new Mock<IRepository<ProductAttributeCombination>>();
            productAttributeCombinationRepository.Setup(x => x.Table)
                .Returns(Enumerable.Empty<ProductAttributeCombination>().AsQueryable());

            var localizedPropertyRepository = new Mock<IRepository<LocalizedProperty>>();
            localizedPropertyRepository.Setup(x => x.Table)
                .Returns(Enumerable.Empty<LocalizedProperty>().AsQueryable());

            var productTagMappingRepository = new Mock<IRepository<ProductProductTagMapping>>();
            productTagMappingRepository.Setup(x => x.Table)
                .Returns(Enumerable.Empty<ProductProductTagMapping>().AsQueryable());

            var productTagRepository = new Mock<IRepository<ProductTag>>();
            productTagRepository.Setup(x => x.Table).Returns(Enumerable.Empty<ProductTag>().AsQueryable());

            var productCategoryRepository = new Mock<IRepository<ProductCategory>>();
            productCategoryRepository.Setup(x => x.Table).Returns(Enumerable.Empty<ProductCategory>().AsQueryable());

            var productManufacturerRepository = new Mock<IRepository<ProductManufacturer>>();
            productManufacturerRepository.Setup(x => x.Table)
                .Returns(Enumerable.Empty<ProductManufacturer>().AsQueryable());

            var productSpecificationAttributeRepository = new Mock<IRepository<ProductSpecificationAttribute>>();
            productSpecificationAttributeRepository.Setup(x => x.Table)
                .Returns(Enumerable.Empty<ProductSpecificationAttribute>().AsQueryable());

            var productWarehouseInventoryRepository = new Mock<IRepository<ProductWarehouseInventory>>();
            productWarehouseInventoryRepository.Setup(x => x.Table)
                .Returns(Enumerable.Empty<ProductWarehouseInventory>().AsQueryable());

            _productRepository = productRepository;
            _storeMappingService = storeMappingService;
            _workContext = workContext;
            _aclService = aclService;
            _languageService = languageService;
            _productAttributeCombinationRepository = productAttributeCombinationRepository;
            _localizedPropertyRepository = localizedPropertyRepository;
            _productTagMappingRepository = productTagMappingRepository;
            _productTagRepository = productTagRepository;
            _productCategoryRepository = productCategoryRepository;
            _productManufacturerRepository = productManufacturerRepository;
            _productSpecificationAttributeRepository = productSpecificationAttributeRepository;
            _productWarehouseInventoryRepository = productWarehouseInventoryRepository;
        }

        private readonly Mock<IRepository<Product>> _productRepository;
        private readonly Mock<IStoreMappingService> _storeMappingService;
        private readonly Mock<IWorkContext> _workContext;
        private readonly Mock<IAclService> _aclService;
        private readonly Mock<ILanguageService> _languageService;
        private readonly Mock<IRepository<ProductAttributeCombination>> _productAttributeCombinationRepository;
        private readonly Mock<IRepository<LocalizedProperty>> _localizedPropertyRepository;
        private readonly Mock<IRepository<ProductProductTagMapping>> _productTagMappingRepository;
        private readonly Mock<IRepository<ProductTag>> _productTagRepository;
        private readonly Mock<IRepository<ProductCategory>> _productCategoryRepository;
        private readonly Mock<IRepository<ProductManufacturer>> _productManufacturerRepository;
        private readonly Mock<IRepository<ProductSpecificationAttribute>> _productSpecificationAttributeRepository;
        private readonly Mock<IRepository<ProductWarehouseInventory>> _productWarehouseInventoryRepository;

        public BetterSearchProductService BuildService()
        {
            return new BetterSearchProductService(
                new CatalogSettings(),
                new CommonSettings(),
                _aclService.Object,
                new Mock<ICustomerService>().Object,
                new Mock<IDateRangeService>().Object,
                _languageService.Object,
                new Mock<ILocalizationService>().Object,
                new Mock<IProductAttributeParser>().Object,
                new Mock<IProductAttributeService>().Object,
                new Mock<IRepository<CrossSellProduct>>().Object,
                new Mock<IRepository<DiscountProductMapping>>().Object,
                _localizedPropertyRepository.Object,
                _productRepository.Object,
                _productAttributeCombinationRepository.Object,
                new Mock<IRepository<ProductAttributeMapping>>().Object,
                _productCategoryRepository.Object,
                _productManufacturerRepository.Object,
                new Mock<IRepository<ProductPicture>>().Object,
                _productTagMappingRepository.Object,
                new Mock<IRepository<ProductReview>>().Object,
                new Mock<IRepository<ProductReviewHelpfulness>>().Object,
                _productSpecificationAttributeRepository.Object,
                _productTagRepository.Object,
                _productWarehouseInventoryRepository.Object,
                new Mock<IRepository<RelatedProduct>>().Object,
                new Mock<IRepository<Shipment>>().Object,
                new Mock<IRepository<StockQuantityHistory>>().Object,
                new Mock<IRepository<TierPrice>>().Object,
                new Mock<IRepository<Warehouse>>().Object,
                new Mock<IStaticCacheManager>().Object,
                new Mock<IStoreService>().Object,
                _storeMappingService.Object,
                _workContext.Object,
                new LocalizationSettings(),
                SearchIndexManager.Object,
                SettingService.Object,
                StoreContext.Object,
                Logger.Object);
        }

        /// <summary>
        /// A plain, un-overridden <see cref="ProductService"/> built from the same mocks. Tests
        /// use this to prove a "delegates to base" scenario is genuine - the override's result
        /// must match this instance's result exactly, because the override does nothing but call
        /// through to it.
        /// </summary>
        public ProductService BuildBaseService()
        {
            return new ProductService(
                new CatalogSettings(),
                new CommonSettings(),
                _aclService.Object,
                new Mock<ICustomerService>().Object,
                new Mock<IDateRangeService>().Object,
                _languageService.Object,
                new Mock<ILocalizationService>().Object,
                new Mock<IProductAttributeParser>().Object,
                new Mock<IProductAttributeService>().Object,
                new Mock<IRepository<CrossSellProduct>>().Object,
                new Mock<IRepository<DiscountProductMapping>>().Object,
                _localizedPropertyRepository.Object,
                _productRepository.Object,
                _productAttributeCombinationRepository.Object,
                new Mock<IRepository<ProductAttributeMapping>>().Object,
                _productCategoryRepository.Object,
                _productManufacturerRepository.Object,
                new Mock<IRepository<ProductPicture>>().Object,
                _productTagMappingRepository.Object,
                new Mock<IRepository<ProductReview>>().Object,
                new Mock<IRepository<ProductReviewHelpfulness>>().Object,
                _productSpecificationAttributeRepository.Object,
                _productTagRepository.Object,
                _productWarehouseInventoryRepository.Object,
                new Mock<IRepository<RelatedProduct>>().Object,
                new Mock<IRepository<Shipment>>().Object,
                new Mock<IRepository<StockQuantityHistory>>().Object,
                new Mock<IRepository<TierPrice>>().Object,
                new Mock<IRepository<Warehouse>>().Object,
                new Mock<IStaticCacheManager>().Object,
                new Mock<IStoreService>().Object,
                _storeMappingService.Object,
                _workContext.Object,
                new LocalizationSettings());
        }
    }
}
