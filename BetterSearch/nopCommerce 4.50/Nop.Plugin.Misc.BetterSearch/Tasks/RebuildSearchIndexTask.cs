using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Plugin.Misc.BetterSearch.Services;
using Nop.Services.Catalog;
using Nop.Services.Logging;
using Nop.Services.ScheduleTasks;

namespace Nop.Plugin.Misc.BetterSearch.Tasks
{
    /// <summary>
    /// Rebuilds the whole search index from the catalogue on a fixed schedule (see
    /// <see cref="BetterSearchDefaults.REBUILD_TASK_PERIOD_SECONDS"/>). Registered on install by
    /// <see cref="BetterSearchPlugin"/> and must keep living at exactly this namespace and name,
    /// because <see cref="BetterSearchDefaults.REBUILD_TASK_TYPE"/> names it as a bare string
    /// the scheduler resolves at run time - nothing checks that string against this class at
    /// compile time.
    ///
    /// A full rebuild also doubles as the safety net that repairs anything the event consumers
    /// missed - a dropped event, a bug in
    /// <see cref="Infrastructure.Cache.ProductIndexEventConsumer"/>, a product changed by a
    /// direct database write that never raised an event. Left alone, that safety net is also a
    /// concealer: the index quietly heals itself and the underlying sync bug never produces a
    /// visible symptom. So before the live index is replaced, this task records how many
    /// documents it holds, compares that against how many documents the rebuild produced, and
    /// writes a warning to the nopCommerce log naming both counts when they differ - turning
    /// silent self-repair into a detected, logged event.
    /// </summary>
    public class RebuildSearchIndexTask : IScheduleTask
    {
        private readonly ILogger _logger;
        private readonly IProductService _productService;
        private readonly ProductIndexInputFactory _productIndexInputFactory;
        private readonly SearchIndexManager _searchIndexManager;

        public RebuildSearchIndexTask(ILogger logger,
            IProductService productService,
            ProductIndexInputFactory productIndexInputFactory,
            SearchIndexManager searchIndexManager)
        {
            _logger = logger;
            _productService = productService;
            _productIndexInputFactory = productIndexInputFactory;
            _searchIndexManager = searchIndexManager;
        }

        public virtual async Task ExecuteAsync()
        {
            //recorded before the rebuild, while it still reflects what shoppers have been
            //searching against - the whole point of the comparison below. The checksum catches
            //what the count alone cannot: a product whose SKU or name changed via a path that
            //raised no event, where the document count before and after is identical.
            var liveCount = await _searchIndexManager.DocumentCountAsync();
            var liveChecksum = await _searchIndexManager.ContentChecksumAsync();

            //keyword-free: IProductService resolves to this plugin's own
            //BetterSearchProductService, and a keyword here would search the very index this
            //task is about to replace. showHidden: true so the index holds the full catalogue;
            //BetterSearchProductService applies publish/ACL/store filters at search time, not
            //the index.
            var products = await _productService.SearchProductsAsync(showHidden: true);

            var inputs = new List<ProductIndexInput>(products.Count);
            foreach (var product in products)
                inputs.Add(await _productIndexInputFactory.BuildAsync(product));

            var rebuilt = await _searchIndexManager.RebuildAsync(inputs);
            if (!rebuilt)
            {
                //comparing counts after a failed rebuild would produce a meaningless drift
                //report, so say what actually happened and stop
                await _logger.WarningAsync("BetterSearch: the scheduled index rebuild failed; the drift check was skipped.");
                return;
            }

            var rebuiltCount = await _searchIndexManager.DocumentCountAsync();
            var rebuiltChecksum = await _searchIndexManager.ContentChecksumAsync();

            var report = DriftDetector.Compare(liveCount, rebuiltCount, liveChecksum, rebuiltChecksum);
            if (report.HasDrifted)
                await _logger.WarningAsync($"BetterSearch: {report.Message}");
        }
    }
}
