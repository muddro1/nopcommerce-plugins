using System;
using System.Threading.Tasks;
using Nop.Core.Domain.Catalog;
using Nop.Core.Events;
using Nop.Plugin.Misc.BetterSearch.Services;
using Nop.Services.Configuration;
using Nop.Services.Events;
using Nop.Services.Logging;

namespace Nop.Plugin.Misc.BetterSearch.Infrastructure.Cache
{
    /// <summary>
    /// Keeps the Lucene index current as products are saved. nopCommerce's generic repository
    /// raises <see cref="EntityInsertedEvent{T}"/>, <see cref="EntityUpdatedEvent{T}"/> and
    /// <see cref="EntityDeletedEvent{T}"/> straight off every insert, update and delete, and
    /// this consumer is discovered and wired up automatically by nopCommerce's startup - see
    /// the "event consumers" block in Nop.Web.Framework's own <c>NopStartup</c>.
    ///
    /// Two rules matter more than the mapping itself:
    ///  - when the plugin is disabled this is a no-op. Nobody is reading the index, so there is
    ///    nothing to keep current.
    ///  - a failed index write must never fail the product save that triggered it. These
    ///    handlers run inline with the save, so letting an exception through here would roll
    ///    back the save. Every path below is defensive; a write this consumer misses is instead
    ///    caught by the periodic rebuild's drift check.
    /// </summary>
    public class ProductIndexEventConsumer :
        IConsumer<EntityInsertedEvent<Product>>,
        IConsumer<EntityUpdatedEvent<Product>>,
        IConsumer<EntityDeletedEvent<Product>>
    {
        private readonly ILogger _logger;
        private readonly ProductIndexInputFactory _productIndexInputFactory;
        private readonly SearchIndexManager _searchIndexManager;
        private readonly ISettingService _settingService;

        public ProductIndexEventConsumer(ILogger logger,
            ProductIndexInputFactory productIndexInputFactory,
            SearchIndexManager searchIndexManager,
            ISettingService settingService)
        {
            _logger = logger;
            _productIndexInputFactory = productIndexInputFactory;
            _searchIndexManager = searchIndexManager;
            _settingService = settingService;
        }

        public Task HandleEventAsync(EntityInsertedEvent<Product> eventMessage) => UpsertAsync(eventMessage.Entity);

        public Task HandleEventAsync(EntityUpdatedEvent<Product> eventMessage) => UpsertAsync(eventMessage.Entity);

        public async Task HandleEventAsync(EntityDeletedEvent<Product> eventMessage)
        {
            if (!await IsEnabledAsync())
                return;

            try
            {
                await _searchIndexManager.DeleteAsync(eventMessage.Entity.Id);
            }
            catch (Exception exception)
            {
                await LogFailureAsync(eventMessage.Entity.Id, "delete", exception);
            }
        }

        private async Task UpsertAsync(Product product)
        {
            if (!await IsEnabledAsync())
                return;

            try
            {
                var input = await _productIndexInputFactory.BuildAsync(product);
                await _searchIndexManager.UpsertAsync(input);
            }
            catch (Exception exception)
            {
                await LogFailureAsync(product.Id, "update", exception);
            }
        }

        /// <summary>
        /// The master switch, read globally rather than for a particular store: a product save
        /// in the admin has no shopper-facing store context to key the setting off, and the
        /// index itself is not partitioned per store either.
        /// </summary>
        private async Task<bool> IsEnabledAsync()
        {
            try
            {
                var settings = await _settingService.LoadSettingAsync<BetterSearchSettings>();
                return settings.Enabled;
            }
            catch
            {
                //an unreadable setting is treated the same as disabled - never let a settings
                //failure escape into the entity save path either
                return false;
            }
        }

        private async Task LogFailureAsync(int productId, string operation, Exception exception)
        {
            try
            {
                await _logger.WarningAsync(
                    $"BetterSearch: failed to {operation} product {productId} in the search index. The periodic rebuild will repair this.",
                    exception);
            }
            catch
            {
                //logging must not throw into the entity save path either
            }
        }
    }
}
