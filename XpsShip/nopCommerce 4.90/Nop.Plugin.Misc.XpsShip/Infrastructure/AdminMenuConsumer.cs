using Nop.Services.Events;
using Nop.Services.Plugins;
using Nop.Services.Security;
using Nop.Web.Framework.Events;
using Nop.Web.Framework.Menu;

namespace Nop.Plugin.Misc.XpsShip.Infrastructure;

/// <summary>
/// Puts the XPS Ship page under Sales, where the spec has it, and under the shared "FMS Plugins" menu with the
/// other FMS plugins. The FMS Plugins part matches the copy every FMS plugin carries: whichever runs first
/// creates the parent and the rest add themselves underneath.
/// </summary>
public class AdminMenuConsumer : IConsumer<AdminMenuCreatedEvent>
{
    // Shared by all FMS plugins - keep identical in every copy
    private const string ParentSystemName = "FmsPlugins";
    private const string ParentTitle = "FMS Plugins";
    private const string ParentIconClass = "fas fa-puzzle-piece";
    private const string InsertBeforeSystemName = "Third party plugins";

    private const string SalesSystemName = "Sales";
    private const string ItemUrl = "~/Admin/XpsShip/Configure";

    private readonly IPluginManager<IPlugin> _pluginManager;

    public AdminMenuConsumer(IPluginManager<IPlugin> pluginManager)
    {
        _pluginManager = pluginManager;
    }

    public async Task HandleEventAsync(AdminMenuCreatedEvent eventMessage)
    {
        // Only fully installed plugins load here; the event can fire mid-install
        if (await _pluginManager.LoadPluginBySystemNameAsync(XpsShipDefaults.SystemName) == null)
            return;

        var root = eventMessage.RootMenuItem;

        var sales = root.GetItemBySystemName(SalesSystemName);
        if (sales != null && sales.ChildNodes.All(node => node.SystemName != XpsShipDefaults.MenuSales))
            sales.ChildNodes.Add(Item(XpsShipDefaults.MenuSales));

        var parent = root.GetItemBySystemName(ParentSystemName);
        if (parent == null)
        {
            parent = new AdminMenuItem
            {
                SystemName = ParentSystemName,
                Title = ParentTitle,
                IconClass = ParentIconClass,
                Visible = true
            };
            if (!root.InsertBefore(InsertBeforeSystemName, parent))
                root.ChildNodes.Add(parent);
        }

        if (parent.ChildNodes.Any(node => node.SystemName == XpsShipDefaults.MenuFms))
            return;

        parent.ChildNodes.Add(Item(XpsShipDefaults.MenuFms));
        parent.ChildNodes = parent.ChildNodes.OrderBy(node => node.Title).ToList();
    }

    private static AdminMenuItem Item(string systemName) => new()
    {
        SystemName = systemName,
        Title = XpsShipDefaults.MenuTitle,
        IconClass = "far fa-dot-circle",
        Url = ItemUrl,
        Visible = true,
        // nopCommerce hides the item (and an empty parent) without this permission
        PermissionNames = new List<string> { StandardPermission.Orders.ORDERS_VIEW }
    };
}
