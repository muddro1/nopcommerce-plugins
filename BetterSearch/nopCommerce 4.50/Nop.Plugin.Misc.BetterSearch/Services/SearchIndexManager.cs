using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace Nop.Plugin.Misc.BetterSearch.Services
{
    /// <summary>
    /// Owns the on-disk Lucene index: writing, reading, and the two-pass search rule.
    ///
    /// Every public method is defensive by design. The index lives on disk and can be missing,
    /// locked or corrupt for reasons entirely outside the plugin's control (a rebuild task
    /// mid-write, a deleted folder, a disk fault). None of that is a reason to throw into a page
    /// render: callers degrade to "no results" and fall back to stock search instead.
    ///
    /// Search itself is two-pass. The first pass is strict - identifiers match exactly, by
    /// segment, or by substring, but never fuzzily. Only when that pass finds nothing does a
    /// second, fuzzy pass run, and only then is <see cref="LastSearchWasApproximate"/> set. Two
    /// part numbers one digit apart are different parts; a strict hit must never be
    /// contaminated by a fuzzy identifier match.
    /// </summary>
    public class SearchIndexManager : IDisposable
    {
        private const LuceneVersion Version = LuceneVersion.LUCENE_48;

        private readonly string _path;
        private readonly object _readerLock = new object();

        private FSDirectory _directory;
        private DirectoryReader _reader;
        private bool _disposed;

        public virtual bool LastSearchWasApproximate { get; private set; }

        public SearchIndexManager(string path)
        {
            _path = path;
        }

        /// <summary>
        /// Product ids matching <paramref name="queryText"/>, best match first. Runs the strict
        /// pass first; only if that returns nothing AND <paramref name="allowApproximateFallback"/>
        /// is true does it fall back to the fuzzy pass, setting
        /// <see cref="LastSearchWasApproximate"/> accordingly. When
        /// <paramref name="allowApproximateFallback"/> is false (the default), the fuzzy pass
        /// never runs and <see cref="LastSearchWasApproximate"/> stays false - nothing today
        /// labels an approximate result as such, and two part numbers one digit apart are
        /// different parts, so a silent guess must never be shown. Never throws - a missing,
        /// locked or corrupt index degrades to an empty result.
        /// </summary>
        public virtual Task<IList<int>> SearchAsync(string queryText, int maxResults, bool allowApproximateFallback = false)
        {
            LastSearchWasApproximate = false;

            try
            {
                var searcher = GetSearcher();
                if (searcher == null)
                    return Task.FromResult<IList<int>>(new List<int>());

                var strictResults = RunQuery(searcher, SearchQueryBuilder.Build(queryText, false), maxResults);
                if (strictResults.Count > 0)
                    return Task.FromResult<IList<int>>(strictResults);

                if (!allowApproximateFallback)
                    return Task.FromResult<IList<int>>(strictResults);

                var fuzzyResults = RunQuery(searcher, SearchQueryBuilder.Build(queryText, true), maxResults);
                if (fuzzyResults.Count > 0)
                    LastSearchWasApproximate = true;

                return Task.FromResult<IList<int>>(fuzzyResults);
            }
            catch
            {
                return Task.FromResult<IList<int>>(new List<int>());
            }
        }

        /// <summary>True if the index directory exists and holds a readable Lucene index.</summary>
        public virtual Task<bool> IsAvailableAsync()
        {
            try
            {
                if (!System.IO.Directory.Exists(_path))
                    return Task.FromResult(false);

                using var directory = FSDirectory.Open(_path);
                return Task.FromResult(DirectoryReader.IndexExists(directory));
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        /// <summary>Writes a fresh index from scratch, replacing whatever was there before.</summary>
        public virtual Task<bool> RebuildAsync(IEnumerable<ProductIndexInput> products)
        {
            try
            {
                System.IO.Directory.CreateDirectory(_path);

                CloseReader();
                CloseDirectory();

                var directory = FSDirectory.Open(_path);
                var analyzer = new StandardAnalyzer(Version);
                var config = new IndexWriterConfig(Version, analyzer) { OpenMode = OpenMode.CREATE };

                //This Lucene.Net port has no IndexWriterConfig.CommitOnClose switch - Dispose()
                //always behaves like a normal close, which commits whatever has been buffered.
                //Left to a plain `using`, an exception thrown partway through the document loop
                //would still commit a PARTIAL index when the block disposes the writer,
                //replacing a good index with a broken one while telling the admin nothing
                //changed. So this is NOT a `using`: on the success path the writer commits
                //explicitly and then disposes normally, but on any failure the catch below
                //calls Rollback() instead, which discards every change made since the writer
                //was opened and closes without committing - leaving the previous index
                //completely untouched.
                var writer = new IndexWriter(directory, config);
                try
                {
                    foreach (var product in products)
                        writer.AddDocument(ProductDocumentBuilder.Build(product));

                    writer.Commit();
                    writer.Dispose();
                }
                catch
                {
                    try
                    {
                        writer.Rollback();
                    }
                    catch
                    {
                        //best effort - the outer catch below still reports failure either way
                    }

                    throw;
                }

                _directory = directory;
                RefreshReader();
            }
            catch
            {
                //Still never throws: an index fault must not reach a shopper's page render.
                //But the caller is told, because an explicit admin rebuild that silently
                //"succeeds" having done nothing is worse than an error.
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }

        /// <summary>Adds or replaces a single product's document, keyed on its product id.</summary>
        public virtual Task UpsertAsync(ProductIndexInput product)
        {
            try
            {
                using (var writer = OpenWriter())
                {
                    var term = new Term(BetterSearchDefaults.FIELD_PRODUCT_ID, product.ProductId.ToString());
                    writer.UpdateDocument(term, ProductDocumentBuilder.Build(product));
                    writer.Commit();
                }

                RefreshReader();
            }
            catch
            {
                //an unwritable index simply fails to update; callers fall back to stock search
            }

            return Task.CompletedTask;
        }

        /// <summary>Removes a single product's document, if present.</summary>
        public virtual Task DeleteAsync(int productId)
        {
            try
            {
                using (var writer = OpenWriter())
                {
                    var term = new Term(BetterSearchDefaults.FIELD_PRODUCT_ID, productId.ToString());
                    writer.DeleteDocuments(term);
                    writer.Commit();
                }

                RefreshReader();
            }
            catch
            {
                //an unwritable index simply fails to update; callers fall back to stock search
            }

            return Task.CompletedTask;
        }

        /// <summary>Number of documents currently in the index, or zero if it cannot be read.</summary>
        public virtual Task<int> DocumentCountAsync()
        {
            try
            {
                var searcher = GetSearcher();
                return Task.FromResult(searcher?.IndexReader.NumDocs ?? 0);
            }
            catch
            {
                return Task.FromResult(0);
            }
        }

        /// <summary>
        /// A content checksum over the whole index: a stable projection of every live document
        /// (product id, raw SKU, name), sorted by product id so write order can never affect the
        /// result, hashed with SHA-256. Document counts alone catch inserts and deletes but not
        /// a product whose SKU or name changed via a path that raised no event - the count stays
        /// identical while the content silently goes stale, and <see cref="DriftDetector"/>
        /// needs this to catch that case too. Returns an empty string, never throws, when the
        /// index cannot be read.
        /// </summary>
        public virtual Task<string> ContentChecksumAsync()
        {
            try
            {
                var searcher = GetSearcher();
                if (searcher == null)
                    return Task.FromResult(string.Empty);

                var reader = searcher.IndexReader;
                var liveDocs = MultiFields.GetLiveDocs(reader);

                var projections = new List<(int ProductId, string Line)>();

                for (var docId = 0; docId < reader.MaxDoc; docId++)
                {
                    //Lucene keeps a deleted document's slot until the next merge; skipping it
                    //here is what keeps a deleted-but-not-yet-merged document out of the checksum
                    if (liveDocs != null && !liveDocs.Get(docId))
                        continue;

                    var document = reader.Document(docId);
                    var idText = document.Get(BetterSearchDefaults.FIELD_PRODUCT_ID);
                    if (!int.TryParse(idText, out var productId))
                        continue;

                    var sku = document.Get(BetterSearchDefaults.FIELD_SKU_RAW) ?? string.Empty;
                    var name = document.Get(BetterSearchDefaults.FIELD_NAME) ?? string.Empty;

                    projections.Add((productId, $"{productId}|{sku}|{name}"));
                }

                var stableText = string.Join("\n", projections.OrderBy(p => p.ProductId).Select(p => p.Line));

                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(stableText));
                return Task.FromResult(Convert.ToHexString(hash));
            }
            catch
            {
                return Task.FromResult(string.Empty);
            }
        }

        private IndexWriter OpenWriter()
        {
            if (_directory == null)
                _directory = FSDirectory.Open(_path);

            var analyzer = new StandardAnalyzer(Version);
            var config = new IndexWriterConfig(Version, analyzer) { OpenMode = OpenMode.CREATE_OR_APPEND };
            return new IndexWriter(_directory, config);
        }

        /// <summary>
        /// Opens (or reopens) the reader against the current directory contents so writes are
        /// visible to the next search, using <see cref="DirectoryReader.OpenIfChanged(DirectoryReader)"/>
        /// where possible rather than a full reopen.
        /// </summary>
        private void RefreshReader()
        {
            lock (_readerLock)
            {
                if (_directory == null)
                    return;

                if (_reader == null)
                {
                    _reader = DirectoryReader.Open(_directory);
                    return;
                }

                var updated = DirectoryReader.OpenIfChanged(_reader);
                if (updated != null)
                {
                    _reader.Dispose();
                    _reader = updated;
                }
            }
        }

        private IndexSearcher GetSearcher()
        {
            lock (_readerLock)
            {
                if (_reader == null)
                {
                    if (!System.IO.Directory.Exists(_path))
                        return null;

                    _directory ??= FSDirectory.Open(_path);
                    if (!DirectoryReader.IndexExists(_directory))
                        return null;

                    _reader = DirectoryReader.Open(_directory);
                }

                return new IndexSearcher(_reader);
            }
        }

        private static List<int> RunQuery(IndexSearcher searcher, Query query, int maxResults)
        {
            var hits = searcher.Search(query, maxResults).ScoreDocs;

            var ids = new List<int>();
            foreach (var hit in hits)
            {
                var document = searcher.Doc(hit.Doc);
                var idText = document.Get(BetterSearchDefaults.FIELD_PRODUCT_ID);
                if (int.TryParse(idText, out var id))
                    ids.Add(id);
            }

            return ids;
        }

        private void CloseReader()
        {
            lock (_readerLock)
            {
                _reader?.Dispose();
                _reader = null;
            }
        }

        private void CloseDirectory()
        {
            _directory?.Dispose();
            _directory = null;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            CloseReader();
            CloseDirectory();

            _disposed = true;
        }
    }
}
