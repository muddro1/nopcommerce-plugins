using System.Linq;
using System.Threading.Tasks;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Nop.Plugin.Misc.BetterSearch.Services
{
    /// <summary>
    /// Builds the plugin's own <see cref="ProductIndexInput"/> from a nopCommerce
    /// <see cref="Product"/>. Both the event consumer and the scheduled rebuild need this
    /// mapping, so it lives here once rather than as two copies that could drift apart.
    /// </summary>
    public class ProductIndexInputFactory
    {
        private readonly ICategoryService _categoryService;
        private readonly IManufacturerService _manufacturerService;
        private readonly IProductTagService _productTagService;

        public ProductIndexInputFactory(ICategoryService categoryService,
            IManufacturerService manufacturerService,
            IProductTagService productTagService)
        {
            _categoryService = categoryService;
            _manufacturerService = manufacturerService;
            _productTagService = productTagService;
        }

        /// <summary>Loads a product's categories, manufacturers and tags and maps everything to a <see cref="ProductIndexInput"/>.</summary>
        public virtual async Task<ProductIndexInput> BuildAsync(Product product)
        {
            var productCategories = await _categoryService.GetProductCategoriesByProductIdAsync(product.Id, showHidden: true);
            var categories = await _categoryService.GetCategoriesByIdsAsync(
                productCategories.Select(pc => pc.CategoryId).ToArray());

            var productManufacturers = await _manufacturerService.GetProductManufacturersByProductIdAsync(product.Id, showHidden: true);
            var manufacturers = await _manufacturerService.GetManufacturersByIdsAsync(
                productManufacturers.Select(pm => pm.ManufacturerId).ToArray());

            var tags = await _productTagService.GetAllProductTagsByProductIdAsync(product.Id);

            return new ProductIndexInput
            {
                ProductId = product.Id,
                Name = product.Name,
                Sku = product.Sku,
                ManufacturerPartNumber = product.ManufacturerPartNumber,
                Gtin = product.Gtin,
                ShortDescription = product.ShortDescription,
                FullDescription = product.FullDescription,
                Tags = tags.Select(tag => tag.Name).ToList(),
                Categories = categories.Select(category => category.Name).ToList(),
                Manufacturers = manufacturers.Select(manufacturer => manufacturer.Name).ToList()
            };
        }
    }
}
