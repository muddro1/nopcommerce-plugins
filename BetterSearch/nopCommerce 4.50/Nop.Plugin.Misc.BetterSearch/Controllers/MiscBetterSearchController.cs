using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Plugin.Misc.BetterSearch.Models;
using Nop.Plugin.Misc.BetterSearch.Services;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.BetterSearch.Controllers
{
    [AuthorizeAdmin]
    [Area(AreaNames.Admin)]
    [AutoValidateAntiforgeryToken]
    public class MiscBetterSearchController : BasePluginController
    {
        #region Fields

        private readonly CatalogSettings _catalogSettings;
        private readonly ILocalizationService _localizationService;
        private readonly INotificationService _notificationService;
        private readonly IPermissionService _permissionService;
        private readonly IProductService _productService;
        private readonly ISettingService _settingService;
        private readonly IStoreContext _storeContext;
        private readonly ProductIndexInputFactory _productIndexInputFactory;
        private readonly SearchIndexManager _searchIndexManager;

        #endregion

        #region Ctor

        public MiscBetterSearchController(CatalogSettings catalogSettings,
            ILocalizationService localizationService,
            INotificationService notificationService,
            IPermissionService permissionService,
            IProductService productService,
            ISettingService settingService,
            IStoreContext storeContext,
            ProductIndexInputFactory productIndexInputFactory,
            SearchIndexManager searchIndexManager)
        {
            _catalogSettings = catalogSettings;
            _localizationService = localizationService;
            _notificationService = notificationService;
            _permissionService = permissionService;
            _productService = productService;
            _settingService = settingService;
            _storeContext = storeContext;
            _productIndexInputFactory = productIndexInputFactory;
            _searchIndexManager = searchIndexManager;
        }

        #endregion

        #region Utilities

        private async Task<ConfigurationModel> PrepareConfigurationModelAsync()
        {
            //load settings for a chosen store scope
            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var settings = await _settingService.LoadSettingAsync<BetterSearchSettings>(storeScope);

            var model = new ConfigurationModel
            {
                Enabled = settings.Enabled,
                MaxIndexResults = settings.MaxIndexResults,
                AllowApproximateFallback = settings.AllowApproximateFallback,
                ActiveStoreScopeConfiguration = storeScope,

                //index status: never allowed to throw into this page - both members already
                //degrade to safe defaults (false / 0) if the index is missing or unreadable
                DocumentCount = await _searchIndexManager.DocumentCountAsync(),
                IndexAvailable = await _searchIndexManager.IsAvailableAsync(),

                //nopCommerce rejects search terms shorter than this in CatalogModelFactory
                //before this plugin is ever consulted - see the locale resource for detail
                ProductSearchTermMinimumLength = _catalogSettings.ProductSearchTermMinimumLength,
                ShowMinimumSearchTermWarning = _catalogSettings.ProductSearchTermMinimumLength > 2
            };

            if (storeScope > 0)
            {
                model.Enabled_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.Enabled, storeScope);
                model.MaxIndexResults_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.MaxIndexResults, storeScope);
                model.AllowApproximateFallback_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.AllowApproximateFallback, storeScope);
            }

            return model;
        }

        #endregion

        #region Methods

        public async Task<IActionResult> Configure()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePlugins))
                return AccessDeniedView();

            var model = await PrepareConfigurationModelAsync();

            return View("~/Plugins/Misc.BetterSearch/Views/Configure.cshtml", model);
        }

        [HttpPost]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePlugins))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return await Configure();

            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var settings = await _settingService.LoadSettingAsync<BetterSearchSettings>(storeScope);

            settings.Enabled = model.Enabled;
            settings.MaxIndexResults = model.MaxIndexResults;
            settings.AllowApproximateFallback = model.AllowApproximateFallback;

            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.Enabled, model.Enabled_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.MaxIndexResults, model.MaxIndexResults_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.AllowApproximateFallback, model.AllowApproximateFallback_OverrideForStore, storeScope, false);

            await _settingService.ClearCacheAsync();

            _notificationService.SuccessNotification(
                await _localizationService.GetResourceAsync("Plugins.Misc.BetterSearch.Configuration.Saved"));

            return await Configure();
        }

        /// <summary>
        /// Rebuilds the whole search index synchronously from the current catalogue, the same
        /// way the scheduled <see cref="Tasks.RebuildSearchIndexTask"/> does, and reports the
        /// resulting document count. An admin clicking this expects to see the result
        /// immediately, so this deliberately does not queue a background task.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> RebuildIndex()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePlugins))
                return AccessDeniedView();

            //keyword-free: IProductService resolves to this plugin's own
            //BetterSearchProductService, and a keyword here would search the very index this
            //action is about to replace. showHidden: true so the index holds the full catalogue;
            //BetterSearchProductService applies publish/ACL/store filters at search time, not
            //the index.
            var products = await _productService.SearchProductsAsync(showHidden: true);

            //one batch call, not one set of queries per product: a per-product build
            //made this loop six round trips per product, which is a standing load on
            //the scheduled path and long enough to time out the admin's Rebuild button
            var inputs = await _productIndexInputFactory.BuildManyAsync(products.ToList());

            var rebuilt = await _searchIndexManager.RebuildAsync(inputs);
            if (!rebuilt)
            {
                //the index manager never throws, so without this the admin would be shown a
                //success message for a rebuild that did nothing - and the document count it
                //quoted could be the stale pre-rebuild figure, making it look convincing
                _notificationService.ErrorNotification(
                    await _localizationService.GetResourceAsync("Plugins.Misc.BetterSearch.IndexStatus.RebuildNow.Failed"));

                return await Configure();
            }

            var documentCount = await _searchIndexManager.DocumentCountAsync();

            if (documentCount != inputs.Count)
            {
                //rebuild reported success but the index does not hold what we gave it
                _notificationService.WarningNotification(string.Format(
                    await _localizationService.GetResourceAsync("Plugins.Misc.BetterSearch.IndexStatus.RebuildNow.CountMismatch"),
                    inputs.Count, documentCount));

                return await Configure();
            }

            _notificationService.SuccessNotification(string.Format(
                await _localizationService.GetResourceAsync("Plugins.Misc.BetterSearch.IndexStatus.RebuildNow.Success"),
                documentCount));

            return await Configure();
        }

        #endregion
    }
}
