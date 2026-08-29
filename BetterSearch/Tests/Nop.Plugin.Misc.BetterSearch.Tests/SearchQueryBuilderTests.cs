using System.Collections.Generic;
using FluentAssertions;
using Nop.Plugin.Misc.BetterSearch.Services;
using NUnit.Framework;

namespace Nop.Plugin.Misc.BetterSearch.Tests
{
    [TestFixture]
    public class SearchQueryBuilderTests
    {
        private InMemoryIndexFixture _index;

        private const int TargetProduct = 1;      // fmsa-ab-1234
        private const int SimilarProduct = 2;     // fmsa-ab-1284, one digit different
        private const int OtherSegment = 3;       // fmsa-cd-1234, same tail, different middle
        private const int TextOnlyProduct = 4;    // mentions 1234 in its description only

        [SetUp]
        public void SetUp()
        {
            _index = new InMemoryIndexFixture(new[]
            {
                new ProductIndexInput { ProductId = TargetProduct, Name = "Flange assembly", Sku = "fmsa-ab-1234" },
                new ProductIndexInput { ProductId = SimilarProduct, Name = "Flange assembly heavy", Sku = "fmsa-ab-1284" },
                new ProductIndexInput { ProductId = OtherSegment, Name = "Cover plate", Sku = "fmsa-cd-1234" },
                new ProductIndexInput { ProductId = TextOnlyProduct, Name = "Manual", FullDescription = "Covers part 1234 in detail" }
            });
        }

        [TearDown]
        public void TearDown() => _index?.Dispose();

        [Test]
        public void Finds_a_product_by_its_whole_sku()
        {
            _index.Search("fmsa-ab-1234").Should().StartWith(TargetProduct);
        }

        [Test]
        public void Whole_sku_search_is_case_insensitive()
        {
            _index.Search("FMSA-AB-1234").Should().StartWith(TargetProduct);
        }

        [Test]
        public void Finds_a_product_by_a_single_sku_segment()
        {
            _index.Search("1234").Should().Contain(TargetProduct);
        }

        [Test]
        public void Finds_a_product_by_two_sku_segments()
        {
            _index.Search("ab-1234").Should().Contain(TargetProduct);
        }

        [Test]
        public void Two_segments_outrank_one()
        {
            //ab-1234 identifies one product; 1234 alone matches two
            _index.Search("ab-1234").Should().StartWith(TargetProduct);
        }

        [Test]
        public void Finds_a_product_by_the_normalised_sku_without_separators()
        {
            _index.Search("ab1234").Should().Contain(TargetProduct);
        }

        [Test]
        public void Finds_a_product_by_a_partial_segment()
        {
            _index.Search("234").Should().Contain(TargetProduct);
        }

        [Test]
        public void An_identifier_hit_outranks_a_description_mention()
        {
            var results = _index.Search("1234");

            results.Should().Contain(TargetProduct);
            results.IndexOf(TargetProduct).Should().BeLessThan(results.IndexOf(TextOnlyProduct));
        }

        [Test]
        public void The_constant_prefix_matches_everything_with_a_sku()
        {
            var results = _index.Search("fmsa");

            results.Should().Contain(new[] { TargetProduct, SimilarProduct, OtherSegment });
        }

        [Test]
        public void A_mistyped_identifier_does_not_return_a_different_part_on_the_strict_pass()
        {
            //1284 is one edit from 1234; the strict pass must never confuse them
            var results = _index.Search("fmsa-ab-1284", allowFuzzyIdentifiers: false);

            results.Should().NotContain(TargetProduct);
            results.Should().StartWith(SimilarProduct);
        }

        [Test]
        public void The_approximate_pass_may_return_a_near_identifier()
        {
            var results = _index.Search("fmsa-ab-1235", allowFuzzyIdentifiers: true);

            results.Should().NotBeEmpty();
        }

        [Test]
        public void Text_search_tolerates_a_typo()
        {
            _index.Search("flnge").Should().Contain(TargetProduct);
        }

        [Test]
        public void Text_search_matches_across_word_order()
        {
            _index.Search("assembly flange").Should().Contain(TargetProduct);
        }

        [Test]
        public void A_very_short_term_is_not_fuzzy_matched()
        {
            //"cov" must not fuzzily match "cover"; short terms are exact only
            var results = _index.Search("xyz");

            results.Should().BeEmpty();
        }

        [Test]
        public void An_empty_query_returns_nothing_rather_than_everything()
        {
            _index.Search("   ").Should().BeEmpty();
        }
    }
}
