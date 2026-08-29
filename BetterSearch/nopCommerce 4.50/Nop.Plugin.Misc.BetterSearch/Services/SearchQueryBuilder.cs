using System.Collections.Generic;
using System.Linq;
using Lucene.Net.Index;
using Lucene.Net.Search;

namespace Nop.Plugin.Misc.BetterSearch.Services
{
    /// <summary>
    /// Turns a user's query text into a Lucene query.
    ///
    /// Identifier fields (SKU, part number, GTIN) are matched exactly, by segment and by
    /// substring. They are NOT fuzzy-matched unless allowFuzzyIdentifiers is set, which the
    /// caller does only after a strict pass returned nothing: two part numbers one edit apart
    /// are different parts, and returning the wrong one is worse than returning none.
    /// </summary>
    public static class SearchQueryBuilder
    {
        //boosts: an identifier hit must beat a name hit, which must beat a description hit
        private const float BOOST_IDENTIFIER_RAW = 12f;
        private const float BOOST_IDENTIFIER_SEGMENT = 8f;
        private const float BOOST_IDENTIFIER_NGRAM = 3f;
        private const float BOOST_GTIN = 12f;
        private const float BOOST_NAME = 5f;
        private const float BOOST_TAGS = 2f;
        private const float BOOST_SHORT_DESCRIPTION = 1.5f;
        private const float BOOST_FULL_DESCRIPTION = 1f;
        private const float BOOST_CATEGORY = 1f;

        public static Query Build(string queryText, bool allowFuzzyIdentifiers)
        {
            var outer = new BooleanQuery();

            if (string.IsNullOrWhiteSpace(queryText))
                return outer;

            var raw = queryText.Trim().ToLowerInvariant();
            var terms = SkuNormaliser.Segments(raw);
            if (!terms.Any())
                return outer;

            //the whole query as typed, against the raw identifier fields
            AddTerm(outer, BetterSearchDefaults.FIELD_SKU_RAW, raw, BOOST_IDENTIFIER_RAW);
            AddTerm(outer, BetterSearchDefaults.FIELD_MPN_RAW, raw, BOOST_IDENTIFIER_RAW);
            AddTerm(outer, BetterSearchDefaults.FIELD_GTIN, queryText.Trim(), BOOST_GTIN);

            //the whole query with separators stripped, so "ab1234" matches "ab-1234"
            var normalisedWhole = SkuNormaliser.Normalise(raw);
            if (normalisedWhole.Length >= ProductDocumentBuilder.NGRAM_MIN)
            {
                AddTerm(outer, BetterSearchDefaults.FIELD_SKU_NGRAM, normalisedWhole, BOOST_IDENTIFIER_SEGMENT);
                AddTerm(outer, BetterSearchDefaults.FIELD_MPN_NGRAM, normalisedWhole, BOOST_IDENTIFIER_SEGMENT);
            }

            //Every segment the caller typed must be present in a product's identifier for the
            //identifier match to count - matching only some of several segments is not enough.
            //Two part numbers that share a prefix and differ in only their last segment are
            //different parts, and scoring alone cannot exclude a wrong one from a plain
            //SHOULD/OR query: a lower boost still leaves it in the result set. Requiring every
            //segment (via an inner MUST group) is what keeps a partial match out entirely.
            AddAllSegmentsMatch(outer, BetterSearchDefaults.FIELD_SKU_SEGMENT, terms, BOOST_IDENTIFIER_SEGMENT);
            AddAllSegmentsMatch(outer, BetterSearchDefaults.FIELD_MPN_SEGMENT, terms, BOOST_IDENTIFIER_SEGMENT);

            var ngramTerms = terms.Where(term => term.Length >= ProductDocumentBuilder.NGRAM_MIN).ToList();
            AddAllSegmentsMatch(outer, BetterSearchDefaults.FIELD_SKU_NGRAM, ngramTerms, BOOST_IDENTIFIER_NGRAM);
            AddAllSegmentsMatch(outer, BetterSearchDefaults.FIELD_MPN_NGRAM, ngramTerms, BOOST_IDENTIFIER_NGRAM);

            foreach (var term in terms)
            {
                if (allowFuzzyIdentifiers)
                {
                    AddFuzzy(outer, BetterSearchDefaults.FIELD_SKU_SEGMENT, term, BOOST_IDENTIFIER_NGRAM);
                    AddFuzzy(outer, BetterSearchDefaults.FIELD_MPN_SEGMENT, term, BOOST_IDENTIFIER_NGRAM);
                }

                //text fields: exact plus fuzzy, scaled by term length
                AddTerm(outer, BetterSearchDefaults.FIELD_NAME, term, BOOST_NAME);
                AddFuzzy(outer, BetterSearchDefaults.FIELD_NAME, term, BOOST_NAME * 0.6f);
                AddTerm(outer, BetterSearchDefaults.FIELD_TAGS, term, BOOST_TAGS);
                AddTerm(outer, BetterSearchDefaults.FIELD_SHORT_DESCRIPTION, term, BOOST_SHORT_DESCRIPTION);
                AddFuzzy(outer, BetterSearchDefaults.FIELD_SHORT_DESCRIPTION, term, BOOST_SHORT_DESCRIPTION * 0.6f);
                AddTerm(outer, BetterSearchDefaults.FIELD_FULL_DESCRIPTION, term, BOOST_FULL_DESCRIPTION);
                AddTerm(outer, BetterSearchDefaults.FIELD_CATEGORIES, term, BOOST_CATEGORY);
                AddTerm(outer, BetterSearchDefaults.FIELD_MANUFACTURERS, term, BOOST_CATEGORY);
            }

            return outer;
        }

        /// <summary>
        /// Edits allowed for a term, by length. Short terms get none: at three characters
        /// almost everything is within one edit of everything else.
        /// </summary>
        public static int MaxEdits(string term)
        {
            if (term.Length <= 3)
                return 0;

            return term.Length <= 7 ? 1 : 2;
        }

        private static void AddTerm(BooleanQuery outer, string field, string text, float boost)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var query = new TermQuery(new Term(field, text)) { Boost = boost };
            outer.Add(query, Occur.SHOULD);
        }

        /// <summary>
        /// Adds a single SHOULD clause to <paramref name="outer"/> that only matches a document
        /// having every one of <paramref name="terms"/> as an exact value in <paramref name="field"/>.
        /// </summary>
        private static void AddAllSegmentsMatch(BooleanQuery outer, string field, IReadOnlyList<string> terms, float boost)
        {
            if (terms == null || terms.Count == 0)
                return;

            var group = new BooleanQuery();
            foreach (var term in terms)
                group.Add(new TermQuery(new Term(field, term)), Occur.MUST);

            group.Boost = boost;
            outer.Add(group, Occur.SHOULD);
        }

        private static void AddFuzzy(BooleanQuery outer, string field, string text, float boost)
        {
            var edits = MaxEdits(text);
            if (edits == 0)
                return;

            var query = new FuzzyQuery(new Term(field, text), edits) { Boost = boost };
            outer.Add(query, Occur.SHOULD);
        }
    }
}
