using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Nop.Plugin.Misc.BetterSearch.Services;
using NUnit.Framework;

namespace Nop.Plugin.Misc.BetterSearch.Tests
{
    [TestFixture]
    public class SearchIndexManagerTests
    {
        private string _path;
        private SearchIndexManager _manager;

        private static ProductIndexInput Product(int id, string name, string sku)
        {
            return new ProductIndexInput { ProductId = id, Name = name, Sku = sku };
        }

        [SetUp]
        public async Task SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(), "bettersearch-tests", Path.GetRandomFileName());
            _manager = new SearchIndexManager(_path);
            await _manager.RebuildAsync(new[]
            {
                Product(1, "Flange assembly", "fmsa-ab-1234"),
                Product(2, "Cover plate", "fmsa-cd-5678")
            });
        }

        [TearDown]
        public void TearDown()
        {
            _manager?.Dispose();
            if (Directory.Exists(_path))
                Directory.Delete(_path, true);
        }

        [Test]
        public async Task Reports_itself_available_once_built()
        {
            (await _manager.IsAvailableAsync()).Should().BeTrue();
        }

        [Test]
        public async Task Counts_its_documents()
        {
            (await _manager.DocumentCountAsync()).Should().Be(2);
        }

        [Test]
        public async Task Finds_a_product_by_sku_segment()
        {
            (await _manager.SearchAsync("1234", 10)).Should().Contain(1);
        }

        [Test]
        public async Task Returns_an_empty_list_when_nothing_matches()
        {
            (await _manager.SearchAsync("zzzzzzzz", 10)).Should().BeEmpty();
        }

        [Test]
        public async Task Marks_a_strict_hit_as_not_approximate()
        {
            await _manager.SearchAsync("1234", 10);

            _manager.LastSearchWasApproximate.Should().BeFalse();
        }

        [Test]
        public async Task Falls_through_to_the_approximate_pass_and_says_so_when_allowed()
        {
            //one edit from 1234; the strict pass finds nothing, the approximate pass finds it -
            //but only because allowApproximateFallback is explicitly true here
            var results = await _manager.SearchAsync("1235", 10, allowApproximateFallback: true);

            results.Should().Contain(1);
            _manager.LastSearchWasApproximate.Should().BeTrue();
        }

        [Test]
        public async Task Strict_only_mode_never_falls_through_and_stays_unmarked()
        {
            //same near-miss as above, but the caller never opted into the approximate pass
            //(the default) - a customer must never be shown a different part number than the
            //one they typed with no indication it is a guess
            var results = await _manager.SearchAsync("1235", 10);

            results.Should().BeEmpty();
            _manager.LastSearchWasApproximate.Should().BeFalse();
        }

        [Test]
        public async Task Strict_only_mode_explicitly_false_behaves_the_same_as_the_default()
        {
            var results = await _manager.SearchAsync("1235", 10, allowApproximateFallback: false);

            results.Should().BeEmpty();
            _manager.LastSearchWasApproximate.Should().BeFalse();
        }

        [Test]
        public async Task A_strict_hit_still_wins_even_when_the_approximate_pass_is_allowed()
        {
            //allowing the fallback must never contaminate a genuine strict hit
            var results = await _manager.SearchAsync("1234", 10, allowApproximateFallback: true);

            results.Should().Contain(1);
            _manager.LastSearchWasApproximate.Should().BeFalse();
        }

        [Test]
        public async Task Upsert_adds_a_new_product()
        {
            await _manager.UpsertAsync(Product(3, "Bracket", "fmsa-ef-9999"));

            (await _manager.SearchAsync("9999", 10)).Should().Contain(3);
            (await _manager.DocumentCountAsync()).Should().Be(3);
        }

        [Test]
        public async Task Upsert_replaces_rather_than_duplicates()
        {
            await _manager.UpsertAsync(Product(1, "Flange assembly mk2", "fmsa-ab-1234"));

            (await _manager.DocumentCountAsync()).Should().Be(2);
            (await _manager.SearchAsync("1234", 10)).Should().Equal(1);
        }

        [Test]
        public async Task Delete_removes_a_product()
        {
            await _manager.DeleteAsync(1);

            (await _manager.SearchAsync("1234", 10)).Should().NotContain(1);
            (await _manager.DocumentCountAsync()).Should().Be(1);
        }

        [Test]
        public async Task Rebuild_replaces_the_whole_index()
        {
            await _manager.RebuildAsync(new[] { Product(9, "Only survivor", "fmsa-zz-0001") });

            (await _manager.DocumentCountAsync()).Should().Be(1);
            (await _manager.SearchAsync("1234", 10)).Should().BeEmpty();
        }

        [Test]
        public async Task Reports_itself_unavailable_when_the_directory_does_not_exist()
        {
            var missing = new SearchIndexManager(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));

            (await missing.IsAvailableAsync()).Should().BeFalse();
            missing.Dispose();
        }

        [Test]
        public async Task Searching_an_unavailable_index_returns_empty_rather_than_throwing()
        {
            var missing = new SearchIndexManager(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));

            (await missing.SearchAsync("anything", 10)).Should().BeEmpty();
            missing.Dispose();
        }

        [Test]
        public async Task Rebuild_reports_success()
        {
            //an explicit admin rebuild must be able to tell the caller it worked, unlike the
            //search path where faults are swallowed so a shopper's page still renders
            var rebuilt = await _manager.RebuildAsync(new[] { Product(9, "Only survivor", "fmsa-zz-0001") });

            rebuilt.Should().BeTrue();
        }

        [Test]
        public async Task Rebuild_over_an_unusable_path_reports_failure_rather_than_throwing()
        {
            //a file where the index directory should be makes CreateDirectory fail
            var blocked = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            await File.WriteAllTextAsync(blocked, "not a directory");

            try
            {
                using var manager = new SearchIndexManager(blocked);

                var rebuilt = await manager.RebuildAsync(new[] { Product(1, "Anything", "fmsa-aa-0001") });

                rebuilt.Should().BeFalse();
            }
            finally
            {
                File.Delete(blocked);
            }
        }

        [Test]
        public async Task Rebuild_that_throws_mid_loop_leaves_the_previous_index_committed()
        {
            //This Lucene.Net port has no IndexWriterConfig.CommitOnClose switch - verified by
            //reflection against the shipped assembly - and Dispose() always commits whatever is
            //buffered. So the failure path calls Rollback() explicitly. Without it, an exception
            //thrown after some documents were added would commit a PARTIAL index, silently
            //replacing a good one with a broken one while reporting failure.
            var productsThatThrowPartway = ThrowingProducts(Product(9, "Should never appear", "fmsa-zz-0001"));

            var rebuilt = await _manager.RebuildAsync(productsThatThrowPartway);

            rebuilt.Should().BeFalse();
            //the original two-product index from SetUp must still be intact and searchable
            (await _manager.DocumentCountAsync()).Should().Be(2);
            (await _manager.SearchAsync("1234", 10)).Should().Contain(1);
            (await _manager.SearchAsync("0001", 10)).Should().BeEmpty();
        }

        private static IEnumerable<ProductIndexInput> ThrowingProducts(ProductIndexInput first)
        {
            yield return first;
            throw new InvalidOperationException("simulated failure partway through the rebuild");
        }

        [Test]
        public async Task Content_checksum_is_stable_for_the_same_content()
        {
            var first = await _manager.ContentChecksumAsync();
            var second = await _manager.ContentChecksumAsync();

            first.Should().NotBeNullOrEmpty();
            first.Should().Be(second);
        }

        [Test]
        public async Task Content_checksum_changes_when_a_products_name_changes_even_though_the_count_does_not()
        {
            var before = await _manager.ContentChecksumAsync();

            //same product id and SKU, different name - document count is unchanged
            await _manager.UpsertAsync(Product(1, "Flange assembly RENAMED", "fmsa-ab-1234"));

            var after = await _manager.ContentChecksumAsync();

            after.Should().NotBe(before);
        }

    }
}
