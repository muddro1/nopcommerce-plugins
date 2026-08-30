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
        public async Task Falls_through_to_the_approximate_pass_and_says_so()
        {
            //one edit from 1234; the strict pass finds nothing, the approximate pass finds it
            var results = await _manager.SearchAsync("1235", 10);

            results.Should().Contain(1);
            _manager.LastSearchWasApproximate.Should().BeTrue();
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
    }
}
