using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Core.Infrastructure;
using Nop.Services.Configuration;
using Nop.Services.Common;
using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;

namespace Nop.Plugin.Misc.BetterSearch
{
    /// <summary>
    /// Plugin lifecycle: install, update and uninstall. The plugin ships disabled, like the
    /// handling fee plugin, so installing it never changes search behaviour before an index
    /// exists - the rebuild task has to run first.
    /// </summary>
    public class BetterSearchPlugin : BasePlugin, IMiscPlugin
    {
        #region Fields

        private readonly ILocalizationService _localizationService;
        private readonly INopFileProvider _fileProvider;
        private readonly IScheduleTaskService _scheduleTaskService;
        private readonly ISettingService _settingService;
        private readonly IWebHelper _webHelper;

        #endregion

        #region Ctor

        public BetterSearchPlugin(ILocalizationService localizationService,
            INopFileProvider fileProvider,
            IScheduleTaskService scheduleTaskService,
            ISettingService settingService,
            IWebHelper webHelper)
        {
            _localizationService = localizationService;
            _fileProvider = fileProvider;
            _scheduleTaskService = scheduleTaskService;
            _settingService = settingService;
            _webHelper = webHelper;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Route matches the controller name/action below: <c>MiscBetterSearchController.Configure</c>.
        /// </summary>
        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/MiscBetterSearch/Configure";
        }

        public override async Task InstallAsync()
        {
            //ships disabled: installing must never change search behaviour before an index exists
            await _settingService.SaveSettingAsync(new BetterSearchSettings
            {
                Enabled = false
            });

            if (await _scheduleTaskService.GetTaskByTypeAsync(BetterSearchDefaults.REBUILD_TASK_TYPE) == null)
            {
                await _scheduleTaskService.InsertTaskAsync(new ScheduleTask
                {
                    Name = BetterSearchDefaults.REBUILD_TASK_NAME,
                    Seconds = BetterSearchDefaults.REBUILD_TASK_PERIOD_SECONDS,
                    Type = BetterSearchDefaults.REBUILD_TASK_TYPE,
                    Enabled = true,
                    StopOnError = false
                });
            }

            await AddOrUpdateLocalesAsync();

            await base.InstallAsync();
        }

        public override async Task UpdateAsync(string currentVersion, string targetVersion)
        {
            //locale resources added in later versions are missing on sites that installed
            //an earlier one, so re-register them all on every upgrade
            await AddOrUpdateLocalesAsync();

            await base.UpdateAsync(currentVersion, targetVersion);
        }

        public override async Task UninstallAsync()
        {
            await _settingService.DeleteSettingAsync<BetterSearchSettings>();

            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Misc.BetterSearch");

            var task = await _scheduleTaskService.GetTaskByTypeAsync(BetterSearchDefaults.REBUILD_TASK_TYPE);
            if (task != null)
                await _scheduleTaskService.DeleteTaskAsync(task);

            var indexPath = _fileProvider.MapPath($"~/App_Data/{BetterSearchDefaults.INDEX_FOLDER}");
            if (_fileProvider.DirectoryExists(indexPath))
                _fileProvider.DeleteDirectory(indexPath);

            await base.UninstallAsync();
        }

        #endregion

        #region Utilities

        private async Task AddOrUpdateLocalesAsync()
        {
            await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
            {
                ["Plugins.Misc.BetterSearch.Fields.Enabled"] = "Enabled",
                ["Plugins.Misc.BetterSearch.Fields.Enabled.Hint"] = "Use the Lucene-backed index for storefront product search. Leave off until the index has been built at least once.",
                ["Plugins.Misc.BetterSearch.Fields.MaxIndexResults"] = "Maximum index results",
                ["Plugins.Misc.BetterSearch.Fields.MaxIndexResults.Hint"] = "The maximum number of product ids taken from the index before nopCommerce applies its own filters.",
                ["Plugins.Misc.BetterSearch.Fields.AllowApproximateFallback"] = "Allow approximate identifier matches",
                ["Plugins.Misc.BetterSearch.Fields.AllowApproximateFallback.Hint"] = "When an exact SKU or part number search finds nothing, allow a fuzzy fallback that can return a DIFFERENT part number than the one typed - two part numbers one digit apart are different parts. Nothing in the storefront currently labels such a result as approximate, so a customer has no way to tell a guess from a confirmed match and may order the wrong part. Leave this off until a widget exists that marks approximate results as such.",
                ["Plugins.Misc.BetterSearch.Configuration.Saved"] = "The settings have been saved.",
                ["Plugins.Misc.BetterSearch.IndexStatus.Title"] = "Index status",
                ["Plugins.Misc.BetterSearch.IndexStatus.DocumentCount"] = "Documents in index",
                ["Plugins.Misc.BetterSearch.IndexStatus.Available"] = "Index available",
                ["Plugins.Misc.BetterSearch.IndexStatus.RebuildNow"] = "Rebuild now",
                ["Plugins.Misc.BetterSearch.IndexStatus.RebuildNow.Success"] = "The search index has been rebuilt. {0} product(s) are now indexed.",
                ["Plugins.Misc.BetterSearch.IndexStatus.RebuildNow.Failed"] = "The search index rebuild failed. The index may now be incomplete. Check the system log for the underlying error, then rebuild again.",
                ["Plugins.Misc.BetterSearch.IndexStatus.RebuildNow.CountMismatch"] = "The rebuild finished but the index holds {1} document(s) for {0} product(s). Check the system log, then rebuild again.",
                ["Plugins.Misc.BetterSearch.MinimumSearchTermWarning"] = "The store's minimum search term length is currently {0} characters. nopCommerce rejects any search term shorter than this in CatalogModelFactory before Better Search is ever consulted, so this plugin cannot see - let alone fix - a query that never reaches it. The store's SKU pattern (for example fmsa-xx-xxxx) has a two-character middle segment that staff regularly search by, so set Minimum search term length to 2 under Configuration > Settings > Catalog settings."
            });
        }

        #endregion
    }
}
