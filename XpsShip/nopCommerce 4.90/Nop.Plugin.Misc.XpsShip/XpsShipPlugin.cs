using Nop.Core;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Plugin.Misc.XpsShip.Logic;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;

namespace Nop.Plugin.Misc.XpsShip;

/// <summary>
/// XPS Ship webhooks: XPS pulls ready-to-ship orders from /xps/orders and posts each label to /xps/update,
/// which becomes a shipment marked shipped. The tables are created by Data/SchemaMigration and dropped on uninstall.
/// </summary>
public class XpsShipPlugin : BasePlugin, IMiscPlugin
{
    private readonly ILocalizationService _localizationService;
    private readonly IScheduleTaskService _scheduleTaskService;
    private readonly ISettingService _settingService;
    private readonly IWebHelper _webHelper;

    public XpsShipPlugin(ILocalizationService localizationService,
        IScheduleTaskService scheduleTaskService,
        ISettingService settingService,
        IWebHelper webHelper)
    {
        _localizationService = localizationService;
        _scheduleTaskService = scheduleTaskService;
        _settingService = settingService;
        _webHelper = webHelper;
    }

    public override string GetConfigurationPageUrl() => _webHelper.GetStoreLocation() + "Admin/XpsShip/Configure";

    public override async Task InstallAsync()
    {
        // Off until staff have checked the Preview and entered the key in XPS
        await _settingService.SaveSettingAsync(new XpsShipSettings
        {
            Enabled = false,
            SecretKey = XpsFormat.NewSecretKey()
        });

        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>(Locales.All));
        await EnsurePurgeTaskAsync();

        await base.InstallAsync();
    }

    public override async Task UpdateAsync(string currentVersion, string targetVersion)
    {
        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>(Locales.All));
        await EnsurePurgeTaskAsync();
        await base.UpdateAsync(currentVersion, targetVersion);
    }

    public override async Task UninstallAsync()
    {
        var task = await _scheduleTaskService.GetTaskByTypeAsync(XpsShipDefaults.PurgeTaskType);
        if (task != null)
            await _scheduleTaskService.DeleteTaskAsync(task);

        await _settingService.DeleteSettingAsync<XpsShipSettings>();
        await _localizationService.DeleteLocaleResourcesAsync(XpsShipDefaults.LocalePrefix);

        await base.UninstallAsync();
    }

    private async Task EnsurePurgeTaskAsync()
    {
        if (await _scheduleTaskService.GetTaskByTypeAsync(XpsShipDefaults.PurgeTaskType) != null)
            return;

        await _scheduleTaskService.InsertTaskAsync(new ScheduleTask
        {
            Name = XpsShipDefaults.PurgeTaskName,
            Type = XpsShipDefaults.PurgeTaskType,
            Seconds = XpsShipDefaults.PurgeTaskSeconds,
            Enabled = true,
            LastEnabledUtc = DateTime.UtcNow,
            StopOnError = false
        });
    }
}
