using System;
using System.Linq;
using FluentAssertions;
using Lucene.Net.Documents;
using Nop.Plugin.Misc.BetterSearch;
using Nop.Plugin.Misc.BetterSearch.Services;
using NUnit.Framework;

namespace Nop.Plugin.Misc.BetterSearch.Tests
{
    [TestFixture]
    public class ProductDocumentBuilderTests
    {
        private static ProductIndexInput Sample()
        {
            return new ProductIndexInput
            {
                ProductId = 42,
                Name = "Running Shoes - Red",
                Sku = "FMSA-AB-1234",
                ManufacturerPartNumber = "MPN-99",
                Gtin = "5012345678900",
                ShortDescription = "Light running shoe",
                FullDescription = "A very light running shoe for road use",
                Tags = new[] { "running", "footwear" },
                Categories = new[] { "Shoes" },
                Manufacturers = new[] { "Acme" }
            };
        }

        [Test]
        public void Stores_the_product_id_retrievably()
        {
            var document = ProductDocumentBuilder.Build(Sample());

            document.Get(BetterSearchDefaults.FIELD_PRODUCT_ID).Should().Be("42");
        }

        [Test]
        public void Indexes_the_sku_raw_normalised_and_lowercased()
        {
            var document = ProductDocumentBuilder.Build(Sample());

            //raw is lowercased so matching is case-insensitive without relying on the analyzer
            document.Get(BetterSearchDefaults.FIELD_SKU_RAW).Should().Be("fmsa-ab-1234");
        }

        [Test]
        public void Indexes_every_sku_segment()
        {
            var document = ProductDocumentBuilder.Build(Sample());

            var segments = document.GetValues(BetterSearchDefaults.FIELD_SKU_SEGMENT);
            segments.Should().Contain(new[] { "fmsa", "ab", "1234" });
        }

        [Test]
        public void Indexes_sku_ngrams_so_partial_segments_match()
        {
            var document = ProductDocumentBuilder.Build(Sample());

            var grams = document.GetValues(BetterSearchDefaults.FIELD_SKU_NGRAM);
            grams.Should().Contain("234");
            grams.Should().Contain("fmsaab1234");
        }

        [Test]
        public void Indexes_the_manufacturer_part_number_the_same_way_as_the_sku()
        {
            var document = ProductDocumentBuilder.Build(Sample());

            document.Get(BetterSearchDefaults.FIELD_MPN_RAW).Should().Be("mpn-99");
            document.GetValues(BetterSearchDefaults.FIELD_MPN_SEGMENT).Should().Contain("99");
        }

        [Test]
        public void Indexes_gtin_exactly_and_does_not_gram_it()
        {
            var document = ProductDocumentBuilder.Build(Sample());

            document.Get(BetterSearchDefaults.FIELD_GTIN).Should().Be("5012345678900");
        }

        [Test]
        public void Indexes_the_text_fields()
        {
            var document = ProductDocumentBuilder.Build(Sample());

            document.Get(BetterSearchDefaults.FIELD_NAME).Should().Be("Running Shoes - Red");
            document.Get(BetterSearchDefaults.FIELD_SHORT_DESCRIPTION).Should().Be("Light running shoe");
            document.Get(BetterSearchDefaults.FIELD_FULL_DESCRIPTION).Should().Contain("road use");
        }

        [Test]
        public void Indexes_tags_categories_and_manufacturers()
        {
            var document = ProductDocumentBuilder.Build(Sample());

            document.GetValues(BetterSearchDefaults.FIELD_TAGS).Should().Contain("running");
            document.GetValues(BetterSearchDefaults.FIELD_CATEGORIES).Should().Contain("Shoes");
            document.GetValues(BetterSearchDefaults.FIELD_MANUFACTURERS).Should().Contain("Acme");
        }

        [Test]
        public void Tolerates_a_product_with_no_sku_or_descriptions()
        {
            var sparse = new ProductIndexInput { ProductId = 7, Name = "Bare product" };

            var document = ProductDocumentBuilder.Build(sparse);

            document.Get(BetterSearchDefaults.FIELD_PRODUCT_ID).Should().Be("7");
            document.Get(BetterSearchDefaults.FIELD_NAME).Should().Be("Bare product");
            document.GetValues(BetterSearchDefaults.FIELD_SKU_SEGMENT).Should().BeEmpty();
        }

        [Test]
        public void Indexes_gtin_lowercased_for_case_insensitivity()
        {
            //an ISBN-10 can end in "X" - case is never significant on any field, GTIN included
            var input = new ProductIndexInput { ProductId = 1, Name = "Textbook", Gtin = "080442957X" };

            var document = ProductDocumentBuilder.Build(input);

            document.Get(BetterSearchDefaults.FIELD_GTIN).Should().Be("080442957x");
        }

        [Test]
        public void Indexes_a_combination_sku_through_the_same_fields_as_the_main_sku()
        {
            var input = new ProductIndexInput
            {
                ProductId = 42,
                Name = "Flange assembly",
                Sku = "fmsa-ab-1234",
                CombinationSkus = new[] { "fmsa-ab-1234-XL" }
            };

            var document = ProductDocumentBuilder.Build(input);

            var rawValues = document.GetValues(BetterSearchDefaults.FIELD_SKU_RAW);
            rawValues.Should().Contain("fmsa-ab-1234");
            rawValues.Should().Contain("fmsa-ab-1234-xl");

            var segments = document.GetValues(BetterSearchDefaults.FIELD_SKU_SEGMENT);
            segments.Should().Contain(new[] { "fmsa", "ab", "1234", "xl" });
        }

        [Test]
        public void A_product_with_no_combinations_indexes_exactly_as_before()
        {
            var input = new ProductIndexInput
            {
                ProductId = 42,
                Name = "Flange assembly",
                Sku = "fmsa-ab-1234"
                //CombinationSkus left at its default empty list
            };

            Action build = () => ProductDocumentBuilder.Build(input);

            build.Should().NotThrow();

            var document = ProductDocumentBuilder.Build(input);
            document.GetValues(BetterSearchDefaults.FIELD_SKU_RAW).Should().Equal("fmsa-ab-1234");
        }
    }
}
