using System.Collections.Generic;
using System.Linq;
using Moq;
using Nop.Core.Domain.Catalog;
using Nop.Data;
using Nop.Plugin.Misc.BetterSearch.Services;

namespace Nop.Plugin.Misc.BetterSearch.Tests
{
    /// <summary>
    /// Backs ProductIndexInputFactory with in-memory repositories, and counts how many times
    /// each repository's Table is read so a test can assert the batch path does not scale its
    /// query count with the number of products.
    /// </summary>
    public class IndexInputFactoryHarness
    {
        private readonly List<ProductCategory> _productCategories = new();
        private readonly List<Category> _categories = new();
        private readonly List<ProductManufacturer> _productManufacturers = new();
        private readonly List<Manufacturer> _manufacturers = new();
        private readonly List<ProductProductTagMapping> _tagMappings = new();
        private readonly List<ProductTag> _tags = new();
        private readonly List<ProductAttributeCombination> _combinations = new();

        public int QueryCount { get; private set; }

        public void ResetQueryCount() => QueryCount = 0;

        public ProductIndexInputFactory Factory => new(
            Repo(_productCategories), Repo(_categories),
            Repo(_productManufacturers), Repo(_manufacturers),
            Repo(_tagMappings), Repo(_tags),
            Repo(_combinations));

        private IRepository<T> Repo<T>(List<T> items) where T : Nop.Core.BaseEntity
        {
            var repo = new Mock<IRepository<T>>();
            repo.Setup(r => r.Table).Returns(() =>
            {
                QueryCount++;
                return items.AsQueryable();
            });
            return repo.Object;
        }

        public IndexInputFactoryHarness WithCategory(int productId, string name)
        {
            var id = _categories.Count + 1;
            _categories.Add(new Category { Id = id, Name = name });
            _productCategories.Add(new ProductCategory { ProductId = productId, CategoryId = id });
            return this;
        }

        public IndexInputFactoryHarness WithManufacturer(int productId, string name)
        {
            var id = _manufacturers.Count + 1;
            _manufacturers.Add(new Manufacturer { Id = id, Name = name });
            _productManufacturers.Add(new ProductManufacturer { ProductId = productId, ManufacturerId = id });
            return this;
        }

        public IndexInputFactoryHarness WithTag(int productId, string name)
        {
            var id = _tags.Count + 1;
            _tags.Add(new ProductTag { Id = id, Name = name });
            _tagMappings.Add(new ProductProductTagMapping { ProductId = productId, ProductTagId = id });
            return this;
        }

        public IndexInputFactoryHarness WithCombinationSku(int productId, string sku)
        {
            _combinations.Add(new ProductAttributeCombination { ProductId = productId, Sku = sku });
            return this;
        }
    }
}
