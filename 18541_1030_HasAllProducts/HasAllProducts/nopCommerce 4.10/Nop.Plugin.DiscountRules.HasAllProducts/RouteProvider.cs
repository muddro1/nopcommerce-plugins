using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.DiscountRules.HasAllProducts
{
    public partial class RouteProvider : IRouteProvider
    {
        public void RegisterRoutes(IRouteBuilder routeBuilder)
        {
            routeBuilder.MapRoute("Plugin.DiscountRules.HasAllProducts.Configure",
                "Plugins/DiscountRulesHasAllProducts/Configure",
                new { controller = "DiscountRulesHasAllProducts", action = "Configure" });

            routeBuilder.MapRoute("Plugin.DiscountRules.HasAllProducts.ProductAddPopup",
                "Plugins/DiscountRulesHasAllProducts/ProductAddPopup",
                new { controller = "DiscountRulesHasAllProducts", action = "ProductAddPopup" });

            routeBuilder.MapRoute("Plugin.DiscountRules.HasAllProducts.ProductAddPopupList",
                "Plugins/DiscountRulesHasAllProducts/ProductAddPopupList",
                new { controller = "DiscountRulesHasAllProducts", action = "ProductAddPopupList" });

            routeBuilder.MapRoute("Plugin.DiscountRules.HasAllProducts.LoadProductFriendlyNames",
                "Plugins/DiscountRulesHasAllProducts/LoadProductFriendlyNames",
                new { controller = "DiscountRulesHasAllProducts", action = "LoadProductFriendlyNames" });
        }

        public int Priority
        {
            get
            {
                return 0;
            }
        }
    }
}
