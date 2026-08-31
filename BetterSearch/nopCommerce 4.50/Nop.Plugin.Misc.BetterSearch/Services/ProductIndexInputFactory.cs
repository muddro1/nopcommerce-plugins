using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Core.Domain.Catalog;
using Nop.Data;

namespace Nop.Plugin.Misc.BetterSearch.Services
{
    /// <summary>
    /// Builds the plugin's own <see cref="ProductIndexInput"/> from a nopCommerce
    /// <see cref="Product"/>. Both the event consumer and the scheduled rebuild need this
    /// mapping, so it lives here once rather than as two copies that could drift apart.
    ///
    /// Two paths, deliberately:
    ///
    /// <see cref="BuildAsync"/> maps a single product and is what the event consumer uses when
    /// one product is saved. Its cost does not matter - it runs once per save.
    ///
    /// <see cref="BuildManyAsync"/> maps a whole catalogue with a FIXED number of queries
    /// instead of one set per product. A rebuild covering several thousand products through the
    /// single-product path issued six round trips each, which made the scheduled rebuild a
    /// standing load and, worse, made the admin's synchronous "Rebuild now" button run long
    /// enough to hit a request timeout.
    ///
    /// This works against repositories rather than the catalogue services because nopCommerce
    /// 4.50 exposes no by-many-product-ids lookups - every service method here takes a single
    /// product id, which is precisely the shape that forces N+1.
    /// </summary>
    public class ProductIndexInputFactory
    {
        #region Fields

        private readonly IRepository<ProductCategory> _productCategoryRepository;
        private readonly IRepository<Category> _categoryRepository;
        private readonly IRepository<ProductManufacturer> _productManufacturerRepository;
        private readonly IRepository<Manufacturer> _manufacturerRepository;
        private readonly IRepository<ProductProductTagMapping> _productTagMappingRepository;
        private readonly IRepository<ProductTag> _productTagRepository;
        private readonly IRepository<ProductAttributeCombination> _combinationRepository;

        #endregion

        #region Ctor

        public ProductIndexInputFactory(IRepository<ProductCategory> productCategoryRepository,
            IRepository<Category> categoryRepository,
            IRepository<ProductManufacturer> productManufacturerRepository,
            IRepository<Manufacturer> manufacturerRepository,
            IRepository<ProductProductTagMapping> productTagMappingRepository,
            IRepository<ProductTag> productTagRepository,
            IRepository<ProductAttributeCombination> combinationRepository)
        {
            _productCategoryRepository = productCategoryRepository;
            _categoryRepository = categoryRepository;
            _productManufacturerRepository = productManufacturerRepository;
            _manufacturerRepository = manufacturerRepository;
            _productTagMappingRepository = productTagMappingRepository;
            _productTagRepository = productTagRepository;
            _combinationRepository = combinationRepository;
        }

        #endregion

        #region Methods

        /// <summary>Maps a single product. Used by the event consumer when one product is saved.</summary>
        public virtual async Task<ProductIndexInput> BuildAsync(Product product)
        {
            return (await BuildManyAsync(new[] { product })).Single();
        }

        /// <summary>
        /// Maps many products using a fixed number of queries, whatever the product count.
        /// </summary>
        public virtual Task<IList<ProductIndexInput>> BuildManyAsync(IList<Product> products)
        {
            if (products == null || !products.Any())
                return Task.FromResult<IList<ProductIndexInput>>(new List<ProductIndexInput>());

            var productIds = products.Select(product => product.Id).ToList();

            //one query per relationship for the WHOLE batch, joined to its lookup table, rather
            //than one pair of queries per product
            var categoriesByProduct = (from mapping in _productCategoryRepository.Table
                                       join category in _categoryRepository.Table
                                           on mapping.CategoryId equals category.Id
                                       where productIds.Contains(mapping.ProductId)
                                       select new { mapping.ProductId, category.Name })
                .ToList()
                .GroupBy(x => x.ProductId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToList());

            var manufacturersByProduct = (from mapping in _productManufacturerRepository.Table
                                          join manufacturer in _manufacturerRepository.Table
                                              on mapping.ManufacturerId equals manufacturer.Id
                                          where productIds.Contains(mapping.ProductId)
                                          select new { mapping.ProductId, manufacturer.Name })
                .ToList()
                .GroupBy(x => x.ProductId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToList());

            var tagsByProduct = (from mapping in _productTagMappingRepository.Table
                                 join tag in _productTagRepository.Table
                                     on mapping.ProductTagId equals tag.Id
                                 where productIds.Contains(mapping.ProductId)
                                 select new { mapping.ProductId, tag.Name })
                .ToList()
                .GroupBy(x => x.ProductId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToList());

            //stock nopCommerce's own search unions attribute-combination SKUs in separately
            //(ProductService.SearchProductsAsync's ProductAttributeCombination query); this
            //plugin's override calls base with keywords: null, so that union never runs, and a
            //variant SKU search would silently return nothing without this.
            var combinationSkusByProduct = _combinationRepository.Table
                .Where(combination => productIds.Contains(combination.ProductId))
                .Select(combination => new { combination.ProductId, combination.Sku })
                .ToList()
                .Where(x => !string.IsNullOrWhiteSpace(x.Sku))
                .GroupBy(x => x.ProductId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Sku).ToList());

            IList<ProductIndexInput> inputs = products.Select(product => new ProductIndexInput
            {
                ProductId = product.Id,
                Name = product.Name,
                Sku = product.Sku,
                ManufacturerPartNumber = product.ManufacturerPartNumber,
                Gtin = product.Gtin,
                ShortDescription = product.ShortDescription,
                FullDescription = product.FullDescription,
                CombinationSkus = Lookup(combinationSkusByProduct, product.Id),
                Tags = Lookup(tagsByProduct, product.Id),
                Categories = Lookup(categoriesByProduct, product.Id),
                Manufacturers = Lookup(manufacturersByProduct, product.Id)
            }).ToList();

            return Task.FromResult(inputs);
        }

        #endregion

        #region Utilities

        private static IList<string> Lookup(IDictionary<int, List<string>> source, int productId)
        {
            return source.TryGetValue(productId, out var values) ? values : new List<string>();
        }

        #endregion
    }
}
