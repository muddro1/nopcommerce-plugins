using Nop.Core;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Plugin.Misc.XpsShip.Logic;
using Microsoft.AspNetCore.Routing;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Menu;

namespace Nop.Plugin.Misc.XpsShip;

/// <summary>
/// XPS Ship webhooks: XPS pulls ready-to-ship orders from /xps/orders and posts each label to /xps/update,
/// which becomes a shipment marked shipped. The tables are created by Data/SchemaMigration and dropped on uninstall.
/// </summary>
public class XpsShipPlugin : BasePlugin, IMiscPlugin, IAdminMenuPlugin
{
    private const string SalesSystemName = "Sales";

    private readonly ILocalizationService _localizationService;
    private readonly IPermissionService _permissionService;
    private readonly IScheduleTaskService _scheduleTaskService;
    private readonly ISettingService _settingService;
    private readonly IWebHelper _webHelper;

    public XpsShipPlugin(ILocalizationService localizationService,
        IPermissionService permissionService,
        IScheduleTaskService scheduleTaskService,
        ISettingService settingService,
        IWebHelper webHelper)
    {
        _localizationService = localizationService;
        _permissionService = permissionService;
        _scheduleTaskService = scheduleTaskService;
        _settingService = settingService;
        _webHelper = webHelper;
    }

    public override string GetConfigurationPageUrl() => _webHelper.GetStoreLocation() + "Admin/XpsShip/Configure";

    /// <summary>Puts the XPS Ship page under Sales, where the spec has it</summary>
    public async Task ManageSiteMapAsync(SiteMapNode rootNode)
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageOrders))
            return;

        var sales = rootNode.ChildNodes.FirstOrDefault(node => node.SystemName == SalesSystemName);
        if (sales == null || sales.ChildNodes.Any(node => node.SystemName == XpsShipDefaults.MenuSales))
            return;

        sales.ChildNodes.Add(new SiteMapNode
        {
            SystemName = XpsShipDefaults.MenuSales,
            Title = XpsShipDefaults.MenuTitle,
            ControllerName = "XpsShip",
            ActionName = "Configure",
            IconClass = "far fa-dot-circle",
            Visible = true,
            RouteValues = new RouteValueDictionary { { "area", AreaNames.Admin } }
        });
    }

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
