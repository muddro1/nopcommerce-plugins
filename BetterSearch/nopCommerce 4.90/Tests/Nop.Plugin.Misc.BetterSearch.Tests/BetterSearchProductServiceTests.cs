using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using NUnit.Framework;

namespace Nop.Plugin.Misc.BetterSearch.Tests
{
    /// <summary>
    /// Covers the six rules <see cref="Services.BetterSearchProductService.SearchProductsAsync"/>
    /// must follow. The index RANKS, nopCommerce FILTERS: every scenario here either proves the
    /// override falls back to plain <c>ProductService</c> behaviour, or proves that a product the
    /// base query would exclude never survives the merge with the index's ids.
    /// </summary>
    [TestFixture]
    public class BetterSearchProductServiceTests
    {
        private static Product Product(int id, string name, decimal price = 0m, bool published = true,
            bool visibleIndividually = true)
        {
            return new Product
            {
                Id = id,
                Name = name,
                Price = price,
                Published = published,
                VisibleIndividually = visibleIndividually
            };
        }

        [Test]
        public async Task Disabled_plugin_delegates_to_base_and_never_touches_the_index()
        {
            var harness = new BetterSearchProductServiceHarness { Settings = new BetterSearchSettings { Enabled = false } };
            harness.Products.Add(Product(1, "Widget Deluxe"));
            harness.Products.Add(Product(2, "Gadget"));

            var service = harness.BuildService();

            var result = await service.SearchProductsAsync(keywords: "Widget");

            result.Select(p => p.Id).Should().Equal(1);

            harness.SearchIndexManager.Verify(x => x.IsAvailableAsync(), Times.Never);
            harness.SearchIndexManager.Verify(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
        }

        [Test]
        public async Task Null_keywords_delegate_to_base_and_never_touch_the_index()
        {
            var harness = new BetterSearchProductServiceHarness();
            harness.Products.Add(Product(1, "Widget Deluxe"));
            harness.Products.Add(Product(2, "Gadget"));

            var service = harness.BuildService();

            var result = await service.SearchProductsAsync(keywords: null);

            //no keyword at all means base returns its whole filtered catalogue
            result.Select(p => p.Id).Should().BeEquivalentTo(new[] { 1, 2 });

            harness.SearchIndexManager.Verify(x => x.IsAvailableAsync(), Times.Never);
            harness.SearchIndexManager.Verify(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
        }

        [Test]
        public async Task Whitespace_keywords_delegate_to_base_and_never_touch_the_index()
        {
            var harness = new BetterSearchProductServiceHarness();
            harness.Products.Add(Product(1, "Widget Deluxe"));
            harness.Products.Add(Product(2, "Gadget"));

            var service = harness.BuildService();
            var baseService = harness.BuildBaseService();

            var result = await service.SearchProductsAsync(keywords: "   ");
            var baseResult = await baseService.SearchProductsAsync(keywords: "   ");

            //the override must not alter the outcome of a whitespace-only keyword in any way -
            //whatever plain ProductService would do with it is exactly what comes back
            result.Select(p => p.Id).Should().Equal(baseResult.Select(p => p.Id));

            harness.SearchIndexManager.Verify(x => x.IsAvailableAsync(), Times.Never);
            harness.SearchIndexManager.Verify(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
        }

        [Test]
        public async Task Unavailable_index_delegates_to_base_without_searching_it()
        {
            var harness = new BetterSearchProductServiceHarness();
            harness.Products.Add(Product(1, "Widget Deluxe"));
            harness.Products.Add(Product(2, "Gadget"));
            harness.SearchIndexManager.Setup(x => x.IsAvailableAsync()).ReturnsAsync(false);

            var service = harness.BuildService();

            var result = await service.SearchProductsAsync(keywords: "Widget");

            result.Select(p => p.Id).Should().Equal(1);

            harness.SearchIndexManager.Verify(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
        }

        [Test]
        public async Task Index_failure_delegates_to_base_instead_of_reaching_the_page()
        {
            var harness = new BetterSearchProductServiceHarness();
            harness.Products.Add(Product(1, "Widget Deluxe"));
            harness.Products.Add(Product(2, "Gadget"));
            harness.SearchIndexManager.Setup(x => x.IsAvailableAsync()).ThrowsAsync(new InvalidOperationException("index is on fire"));

            var service = harness.BuildService();

            var result = await service.SearchProductsAsync(keywords: "Widget");

            result.Select(p => p.Id).Should().Equal(1);
            harness.SearchIndexManager.Verify(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
        }

        [Test]
        public async Task Index_failure_logs_a_warning_naming_the_exception_before_delegating()
        {
            //a locked, corrupt or deleted index must never degrade every storefront search
            //silently - the operator needs a signal, not just "search got worse"
            var harness = new BetterSearchProductServiceHarness();
            harness.Products.Add(Product(1, "Widget Deluxe"));
            var failure = new InvalidOperationException("index is on fire");
            harness.SearchIndexManager.Setup(x => x.IsAvailableAsync()).ThrowsAsync(failure);

            var service = harness.BuildService();

            await service.SearchProductsAsync(keywords: "Widget");

            harness.Logger.Verify(x => x.WarningAsync(
                It.IsAny<string>(),
                failure,
                It.IsAny<Customer>()), Times.Once);
        }

        [Test]
        public async Task Matches_come_back_in_index_order_not_base_order()
        {
            var harness = new BetterSearchProductServiceHarness();
            harness.Products.Add(Product(1, "Widget A"));
            harness.Products.Add(Product(2, "Widget B"));
            harness.Products.Add(Product(3, "Widget C"));
            harness.SearchIndexManager.Setup(x => x.IsAvailableAsync()).ReturnsAsync(true);
            harness.SearchIndexManager
                .Setup(x => x.SearchAsync("Widget", harness.Settings.MaxIndexResults, harness.Settings.AllowApproximateFallback))
                .ReturnsAsync(new List<int> { 3, 1, 2 });

            var service = harness.BuildService();

            var result = await service.SearchProductsAsync(keywords: "Widget");

            //base's own order (by DisplayOrder/Id) would be 1, 2, 3 - the index's relevance
            //order must win
            result.Select(p => p.Id).Should().Equal(3, 1, 2);
        }

        [Test]
        public async Task Explicit_sort_is_honoured_and_the_index_order_is_skipped()
        {
            var harness = new BetterSearchProductServiceHarness();
            harness.Products.Add(Product(1, "Widget A", price: 10m));
            harness.Products.Add(Product(2, "Widget B", price: 30m));
            harness.Products.Add(Product(3, "Widget C", price: 20m));
            harness.SearchIndexManager.Setup(x => x.IsAvailableAsync()).ReturnsAsync(true);
            harness.SearchIndexManager
                .Setup(x => x.SearchAsync("Widget", harness.Settings.MaxIndexResults, harness.Settings.AllowApproximateFallback))
                .ReturnsAsync(new List<int> { 1, 2, 3 });

            var service = harness.BuildService();

            var result = await service.SearchProductsAsync(keywords: "Widget", orderBy: ProductSortingEnum.PriceDesc);

            //price descending is 2 (30), 3 (20), 1 (10) - not the index's 1, 2, 3
            result.Select(p => p.Id).Should().Equal(2, 3, 1);
        }

        [Test]
        public async Task Product_present_in_the_index_but_filtered_out_by_base_does_not_appear()
        {
            var harness = new BetterSearchProductServiceHarness();
            harness.Products.Add(Product(1, "Widget A", published: true));
            harness.Products.Add(Product(2, "Widget B", published: false)); //unpublished since the index was last written
            harness.SearchIndexManager.Setup(x => x.IsAvailableAsync()).ReturnsAsync(true);
            harness.SearchIndexManager
                .Setup(x => x.SearchAsync("Widget", harness.Settings.MaxIndexResults, harness.Settings.AllowApproximateFallback))
                .ReturnsAsync(new List<int> { 2, 1 });

            var service = harness.BuildService();

            var result = await service.SearchProductsAsync(keywords: "Widget");

            result.Select(p => p.Id).Should().Equal(1);
        }

        [Test]
        public async Task A_real_base_predicate_is_forwarded_on_the_index_path()
        {
            //I-1: the base call inside the index path (line 179) must forward every argument the
            //caller passed in, not just the ones the earlier tests happen to exercise via the
            //delegate path. visibleIndividuallyOnly is a predicate no other test in this file
            //pins on the index path - dropping it from that call would let product 2 through.
            var harness = new BetterSearchProductServiceHarness();
            harness.Products.Add(Product(1, "Widget A", visibleIndividually: true));
            harness.Products.Add(Product(2, "Widget B", visibleIndividually: false));
            harness.SearchIndexManager.Setup(x => x.IsAvailableAsync()).ReturnsAsync(true);
            harness.SearchIndexManager
                .Setup(x => x.SearchAsync("Widget", harness.Settings.MaxIndexResults, harness.Settings.AllowApproximateFallback))
                .ReturnsAsync(new List<int> { 1, 2 });

            var service = harness.BuildService();

            var result = await service.SearchProductsAsync(keywords: "Widget", visibleIndividuallyOnly: true);

            result.Select(p => p.Id).Should().Equal(1);
        }

        [Test]
        public async Task Paging_is_applied_after_the_merge_not_forwarded_into_the_base_call()
        {
            //I-2: the base call inside the index path must always request the WHOLE filtered set
            //(page 0, unbounded), then the override slices pageIndex/pageSize itself after the
            //merge. If the caller's own pageIndex/pageSize were forwarded into that base call
            //instead, the base query would page the unfiltered set first and the merge would
            //then intersect against too few rows - short pages, and a wrong TotalCount.
            var harness = new BetterSearchProductServiceHarness();
            for (var id = 1; id <= 5; id++)
                harness.Products.Add(Product(id, $"Widget {id}"));

            harness.SearchIndexManager.Setup(x => x.IsAvailableAsync()).ReturnsAsync(true);
            harness.SearchIndexManager
                .Setup(x => x.SearchAsync("Widget", harness.Settings.MaxIndexResults, harness.Settings.AllowApproximateFallback))
                .ReturnsAsync(new List<int> { 5, 4, 3, 2, 1 }); //index order reversed from catalogue order

            var service = harness.BuildService();

            var result = await service.SearchProductsAsync(keywords: "Widget", pageIndex: 1, pageSize: 2);

            //index order is 5,4,3,2,1 - page 1 (0-based) of size 2 is the third and fourth entries
            result.Select(p => p.Id).Should().Equal(3, 2);
            result.TotalCount.Should().Be(5);
        }
    }
}
