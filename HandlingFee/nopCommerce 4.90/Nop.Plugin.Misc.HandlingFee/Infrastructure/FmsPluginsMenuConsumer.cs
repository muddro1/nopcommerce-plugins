using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Services.Events;
using Nop.Services.Plugins;
using Nop.Services.Security;
using Nop.Web.Framework.Events;
using Nop.Web.Framework.Menu;

namespace Nop.Plugin.Misc.HandlingFee.Infrastructure
{
    /// <summary>
    /// Puts this plugin's settings page under the shared "FMS Plugins" admin menu.
    /// Every FMS plugin carries a copy of this consumer and looks the parent up by the same system name,
    /// so whichever runs first creates it and the rest add themselves underneath.
    /// </summary>
    public class FmsPluginsMenuConsumer : IConsumer<AdminMenuCreatedEvent>
    {
        // Shared by all FMS plugins - keep identical in every copy
        private const string ParentSystemName = "FmsPlugins";
        private const string ParentTitle = "FMS Plugins";
        private const string ParentIconClass = "fas fa-puzzle-piece";
        private const string InsertBeforeSystemName = "Third party plugins";

        // This plugin's entry
        private const string PluginSystemName = "Misc.HandlingFee";
        private const string ItemSystemName = "FmsPlugins.HandlingFee";
        private const string ItemTitle = "Handling Fee";
        private const string ItemUrl = "~/Admin/MiscHandlingFee/Configure";

        private readonly IPluginManager<IPlugin> _pluginManager;

        public FmsPluginsMenuConsumer(IPluginManager<IPlugin> pluginManager)
        {
            _pluginManager = pluginManager;
        }

        public async Task HandleEventAsync(AdminMenuCreatedEvent eventMessage)
        {
            // Only fully installed plugins load here; the event can fire mid-install
            if (await _pluginManager.LoadPluginBySystemNameAsync(PluginSystemName) == null)
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

            if (parent.ChildNodes.Any(node => node.SystemName == ItemSystemName))
                return;

            parent.ChildNodes.Add(new AdminMenuItem
            {
                SystemName = ItemSystemName,
                Title = ItemTitle,
                IconClass = "far fa-dot-circle",
                Url = ItemUrl,
                Visible = true,
                // nopCommerce hides the item (and the parent, once it has no visible children) without this permission
                PermissionNames = new List<string> { StandardPermission.Configuration.MANAGE_PLUGINS }
            });

            parent.ChildNodes = parent.ChildNodes.OrderBy(node => node.Title).ToList();
        }
    }
}
