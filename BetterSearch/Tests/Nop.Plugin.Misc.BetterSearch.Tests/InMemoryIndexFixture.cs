using System.Collections.Generic;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Nop.Plugin.Misc.BetterSearch;
using Nop.Plugin.Misc.BetterSearch.Services;

namespace Nop.Plugin.Misc.BetterSearch.Tests
{
    /// <summary>
    /// A throwaway in-memory index holding a handful of products, so query behaviour can be
    /// asserted end to end without a database or a disk.
    /// </summary>
    public class InMemoryIndexFixture : System.IDisposable
    {
        public const LuceneVersion Version = LuceneVersion.LUCENE_48;

        private readonly Directory _directory = new RAMDirectory();
        private IndexSearcher _searcher;

        public InMemoryIndexFixture(IEnumerable<ProductIndexInput> products)
        {
            var analyzer = new StandardAnalyzer(Version);
            var config = new IndexWriterConfig(Version, analyzer);
            using (var writer = new IndexWriter(_directory, config))
            {
                foreach (var product in products)
                    writer.AddDocument(ProductDocumentBuilder.Build(product));
                writer.Commit();
            }

            _searcher = new IndexSearcher(DirectoryReader.Open(_directory));
        }

        /// <summary>Product ids returned for the query, best match first</summary>
        public IList<int> Search(string queryText, bool allowFuzzyIdentifiers = false, int max = 20)
        {
            var query = SearchQueryBuilder.Build(queryText, allowFuzzyIdentifiers);
            var hits = _searcher.Search(query, max).ScoreDocs;

            var ids = new List<int>();
            foreach (var hit in hits)
            {
                var document = _searcher.Doc(hit.Doc);
                ids.Add(int.Parse(document.Get(BetterSearchDefaults.FIELD_PRODUCT_ID)));
            }

            return ids;
        }

        public void Dispose()
        {
            _directory?.Dispose();
        }
    }
}
