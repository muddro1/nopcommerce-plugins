using Nop.Core;
using Nop.Services.Events;
using Nop.Services.Plugins;
using Nop.Services.Security;
using Nop.Web.Framework.Events;
using Nop.Web.Framework.Menu;

namespace Nop.Plugin.Misc.CustomerProfile.Infrastructure;

/// <summary>
/// Adds a "Customer Profile" group (Tax exemptions, Net 30 orders, Settings) under the shared "FMS Plugins"
/// admin menu. Every FMS plugin carries a consumer like this and looks the parent up by the same system name,
/// so whichever runs first creates it and the rest add themselves underneath.
/// </summary>
public class FmsPluginsMenuConsumer : IConsumer<AdminMenuCreatedEvent>
{
    // Shared by all FMS plugins - keep identical in every copy
    private const string ParentSystemName = "FmsPlugins";
    private const string ParentTitle = "FMS Plugins";
    private const string ParentIconClass = "fas fa-puzzle-piece";
    private const string InsertBeforeSystemName = "Third party plugins";

    private readonly IPluginManager<IPlugin> _pluginManager;
    private readonly IWorkContext _workContext;

    public FmsPluginsMenuConsumer(IPluginManager<IPlugin> pluginManager, IWorkContext workContext)
    {
        _pluginManager = pluginManager;
        _workContext = workContext;
    }

    public async Task HandleEventAsync(AdminMenuCreatedEvent eventMessage)
    {
        // Only fully installed plugins load here; the event can fire mid-install
        if (await _pluginManager.LoadPluginBySystemNameAsync(CustomerProfileDefaults.SystemName) == null)
            return;

        var root = eventMessage.RootMenuItem;
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

        if (parent.ChildNodes.Any(node => node.SystemName == CustomerProfileDefaults.MenuGroup))
            return;

        // These lists span every customer, so vendors never get them.
        // nopCommerce hides each item the current admin lacks the permission for, and the group once none remain.
        var isVendor = await _workContext.GetCurrentVendorAsync() != null;

        parent.ChildNodes.Add(new AdminMenuItem
        {
            SystemName = CustomerProfileDefaults.MenuGroup,
            Title = "Customer Profile",
            IconClass = "fas fa-id-card",
            Visible = true,
            ChildNodes = new List<AdminMenuItem>
            {
                new()
                {
                    SystemName = CustomerProfileDefaults.MenuTaxExemptions,
                    Title = "Tax exemptions",
                    IconClass = "far fa-dot-circle",
                    Url = "~/Admin/CustomerProfileAdmin/TaxExemptions",
                    Visible = !isVendor,
                    PermissionNames = new List<string> { StandardPermission.Customers.CUSTOMERS_VIEW }
                },
                new()
                {
                    SystemName = CustomerProfileDefaults.MenuNet30Orders,
                    Title = "Net 30 orders",
                    IconClass = "far fa-dot-circle",
                    Url = "~/Admin/CustomerProfileAdmin/Net30Orders",
                    Visible = !isVendor,
                    PermissionNames = new List<string> { StandardPermission.Orders.ORDERS_VIEW }
                },
                new()
                {
                    SystemName = CustomerProfileDefaults.MenuSettings,
                    Title = "Settings",
                    IconClass = "far fa-dot-circle",
                    Url = "~/Admin/CustomerProfileAdmin/Configure",
                    Visible = !isVendor,
                    PermissionNames = new List<string> { StandardPermission.Configuration.MANAGE_PLUGINS }
                }
            }
        });

        parent.ChildNodes = parent.ChildNodes.OrderBy(node => node.Title).ToList();
    }
}
