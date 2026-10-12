using System.Collections.Generic;
using Lucene.Net.Documents;

namespace Nop.Plugin.Misc.BetterSearch.Services
{
    /// <summary>
    /// Maps a product to the Lucene document that represents it.
    ///
    /// Identifiers are stored three ways - raw, by segment, and as n-grams - so that a search
    /// for any fragment of a SKU matches. See the spec's "SKU matching" section for why prefix
    /// matching alone is useless on this catalogue.
    /// </summary>
    public static class ProductDocumentBuilder
    {
        /// <summary>N-gram bounds for identifier fields</summary>
        public const int NGRAM_MIN = 2;
        public const int NGRAM_MAX = 10;

        public static Document Build(ProductIndexInput input)
        {
            var document = new Document();

            //StringField so the id can be read back out of a hit
            document.Add(new StringField(BetterSearchDefaults.FIELD_PRODUCT_ID, input.ProductId.ToString(), Field.Store.YES));

            AddText(document, BetterSearchDefaults.FIELD_NAME, input.Name);
            AddText(document, BetterSearchDefaults.FIELD_SHORT_DESCRIPTION, input.ShortDescription);
            AddText(document, BetterSearchDefaults.FIELD_FULL_DESCRIPTION, input.FullDescription);

            AddIdentifier(document, input.Sku,
                BetterSearchDefaults.FIELD_SKU_RAW,
                BetterSearchDefaults.FIELD_SKU_SEGMENT,
                BetterSearchDefaults.FIELD_SKU_NGRAM);

            //variant (attribute combination) SKUs go through the SAME fields as the product's
            //own SKU, not separate ones - that is what makes substring matching, segment
            //matching, case-insensitivity and the strict-pass exactness rules apply to a
            //variant SKU automatically, with no query-builder change needed. Stock nopCommerce
            //unions these in as a separate query; this plugin's override cannot do that (see
            //ProductIndexInputFactory), so they have to be indexed here instead.
            if (input.CombinationSkus != null)
            {
                foreach (var combinationSku in input.CombinationSkus)
                {
                    AddIdentifier(document, combinationSku,
                        BetterSearchDefaults.FIELD_SKU_RAW,
                        BetterSearchDefaults.FIELD_SKU_SEGMENT,
                        BetterSearchDefaults.FIELD_SKU_NGRAM);
                }
            }

            AddIdentifier(document, input.ManufacturerPartNumber,
                BetterSearchDefaults.FIELD_MPN_RAW,
                BetterSearchDefaults.FIELD_MPN_SEGMENT,
                BetterSearchDefaults.FIELD_MPN_NGRAM);

            //GTIN is an external identifier: right or wrong, never partially matched. Lowercased
            //like every other identifier field - an ISBN-10 can end in "X", and case is never
            //significant on any field.
            if (!string.IsNullOrWhiteSpace(input.Gtin))
                document.Add(new StringField(BetterSearchDefaults.FIELD_GTIN, input.Gtin.Trim().ToLowerInvariant(), Field.Store.YES));

            AddEach(document, BetterSearchDefaults.FIELD_TAGS, input.Tags);
            AddEach(document, BetterSearchDefaults.FIELD_CATEGORIES, input.Categories);
            AddEach(document, BetterSearchDefaults.FIELD_MANUFACTURERS, input.Manufacturers);

            return document;
        }

        private static void AddText(Document document, string field, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                document.Add(new TextField(field, value, Field.Store.YES));
        }

        private static void AddEach(Document document, string field, IEnumerable<string> values)
        {
            if (values == null)
                return;

            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    document.Add(new TextField(field, value, Field.Store.YES));
            }
        }

        private static void AddIdentifier(Document document, string value,
            string rawField, string segmentField, string ngramField)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            //lowercased here rather than relying on an analyzer: StringField is not analysed,
            //so without this the field would be case-sensitive - the trap called out in the spec
            document.Add(new StringField(rawField, value.Trim().ToLowerInvariant(), Field.Store.YES));

            foreach (var segment in SkuNormaliser.Segments(value))
                document.Add(new StringField(segmentField, segment, Field.Store.YES));

            foreach (var gram in SkuNormaliser.NGrams(value, NGRAM_MIN, NGRAM_MAX))
                document.Add(new StringField(ngramField, gram, Field.Store.NO));
        }
    }
}
