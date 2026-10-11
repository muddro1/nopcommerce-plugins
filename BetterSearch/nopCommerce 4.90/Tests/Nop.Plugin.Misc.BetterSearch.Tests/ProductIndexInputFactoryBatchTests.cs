using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Nop.Core.Domain.Catalog;
using Nop.Plugin.Misc.BetterSearch.Services;
using NUnit.Framework;

namespace Nop.Plugin.Misc.BetterSearch.Tests
{
    /// <summary>
    /// The batch path exists so a rebuild costs a fixed number of queries rather than six per
    /// product. These tests pin the OUTPUT equivalence - a batch build must produce exactly what
    /// the per-product build would have - because that is the property a future optimisation
    /// could silently break.
    /// </summary>
    [TestFixture]
    public class ProductIndexInputFactoryBatchTests
    {
        private static Product Product(int id, string name, string sku)
        {
            return new Product { Id = id, Name = name, Sku = sku };
        }

        [Test]
        public async Task Batch_build_returns_one_input_per_product_in_order()
        {
            var harness = new IndexInputFactoryHarness()
                .WithCategory(1, "Flanges")
                .WithManufacturer(1, "Acme")
                .WithTag(2, "clearance")
                .WithCombinationSku(1, "fmsa-ab-1234-xl");

            var inputs = await harness.Factory.BuildManyAsync(new[]
            {
                Product(1, "Flange assembly", "fmsa-ab-1234"),
                Product(2, "Cover plate", "fmsa-cd-5678")
            });

            inputs.Select(i => i.ProductId).Should().Equal(1, 2);
        }

        [Test]
        public async Task Batch_build_matches_the_per_product_build_exactly()
        {
            //the equivalence that matters: batching is an optimisation, not a behaviour change
            var harness = new IndexInputFactoryHarness()
                .WithCategory(1, "Flanges")
                .WithManufacturer(1, "Acme")
                .WithTag(1, "hydraulic")
                .WithCombinationSku(1, "fmsa-ab-1234-xl");

            var product = Product(1, "Flange assembly", "fmsa-ab-1234");

            var single = await harness.Factory.BuildAsync(product);
            var batched = (await harness.Factory.BuildManyAsync(new[] { product })).Single();

            batched.Should().BeEquivalentTo(single);
        }

        [Test]
        public async Task Batch_build_keeps_each_products_own_collections_separate()
        {
            //the failure a naive batch implementation makes: every product getting every category
            var harness = new IndexInputFactoryHarness()
                .WithCategory(1, "Flanges")
                .WithCategory(2, "Plates")
                .WithCombinationSku(1, "fmsa-ab-1234-xl");

            var inputs = await harness.Factory.BuildManyAsync(new[]
            {
                Product(1, "Flange assembly", "fmsa-ab-1234"),
                Product(2, "Cover plate", "fmsa-cd-5678")
            });

            inputs.Single(i => i.ProductId == 1).Categories.Should().Equal("Flanges");
            inputs.Single(i => i.ProductId == 2).Categories.Should().Equal("Plates");
            inputs.Single(i => i.ProductId == 1).CombinationSkus.Should().Equal("fmsa-ab-1234-xl");
            inputs.Single(i => i.ProductId == 2).CombinationSkus.Should().BeEmpty();
        }

        [Test]
        public async Task Batch_build_tolerates_a_product_with_no_related_data()
        {
            var harness = new IndexInputFactoryHarness();

            var inputs = await harness.Factory.BuildManyAsync(new[] { Product(7, "Bare", "fmsa-zz-0001") });

            var only = inputs.Single();
            only.Categories.Should().BeEmpty();
            only.Manufacturers.Should().BeEmpty();
            only.Tags.Should().BeEmpty();
            only.CombinationSkus.Should().BeEmpty();
        }

        [Test]
        public async Task Batch_build_of_an_empty_list_returns_an_empty_list()
        {
            var harness = new IndexInputFactoryHarness();

            (await harness.Factory.BuildManyAsync(new List<Product>())).Should().BeEmpty();
        }

        [Test]
        public async Task Batch_build_issues_a_fixed_number_of_queries_regardless_of_product_count()
        {
            //this is the whole point of the batch path: the query count must not scale with N
            var harness = new IndexInputFactoryHarness()
                .WithCategory(1, "Flanges").WithCategory(2, "Plates").WithCategory(3, "Seals");

            harness.ResetQueryCount();
            await harness.Factory.BuildManyAsync(new[] { Product(1, "A", "a-1"), Product(2, "B", "b-2") });
            var forTwo = harness.QueryCount;

            harness.ResetQueryCount();
            await harness.Factory.BuildManyAsync(new[]
            {
                Product(1, "A", "a-1"), Product(2, "B", "b-2"), Product(3, "C", "c-3")
            });
            var forThree = harness.QueryCount;

            forThree.Should().Be(forTwo, "the batch path must not issue more queries for more products");
        }
    }
}
