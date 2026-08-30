using System.Collections.Generic;
using System.Threading.Tasks;
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

        #endregion

        #region Ctor

        public BetterSearchPlugin(ILocalizationService localizationService,
            INopFileProvider fileProvider,
            IScheduleTaskService scheduleTaskService,
            ISettingService settingService)
        {
            _localizationService = localizationService;
            _fileProvider = fileProvider;
            _scheduleTaskService = scheduleTaskService;
            _settingService = settingService;
        }

        #endregion

        #region Methods

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
                ["Plugins.Misc.BetterSearch.Configuration.Saved"] = "The settings have been saved."
            });
        }

        #endregion
    }
}
