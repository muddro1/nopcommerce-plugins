using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.XpsShip.Infrastructure;

/// <summary>The three webhook URLs entered in XPS</summary>
public class RouteProvider : IRouteProvider
{
    // Ahead of nopCommerce's generic SEO slug route, so /xps/... never resolves as a product or category name
    public int Priority => 100;

    public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapControllerRoute(XpsShipDefaults.RouteListOrders, XpsShipDefaults.PathListOrders,
            new { controller = "XpsShipWebhook", action = "ListOrders" });

        endpointRouteBuilder.MapControllerRoute(XpsShipDefaults.RouteGetOrder, XpsShipDefaults.PathGetOrder,
            new { controller = "XpsShipWebhook", action = "GetOrder" });

        endpointRouteBuilder.MapControllerRoute(XpsShipDefaults.RouteUpdateOrder, XpsShipDefaults.PathUpdateOrder,
            new { controller = "XpsShipWebhook", action = "UpdateOrder" });
    }
}
