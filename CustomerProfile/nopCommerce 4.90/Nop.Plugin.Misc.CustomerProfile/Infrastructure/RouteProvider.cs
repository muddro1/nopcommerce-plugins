using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.CustomerProfile.Infrastructure;

public class RouteProvider : IRouteProvider
{
    public int Priority => 0;

    public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapControllerRoute(CustomerProfileDefaults.RouteBusinessDetails, "customer/business-details",
            new { controller = "CustomerProfilePublic", action = "BusinessDetails" });

        endpointRouteBuilder.MapControllerRoute(CustomerProfileDefaults.RouteContactEdit, "customer/business-details/contact/{id:int}",
            new { controller = "CustomerProfilePublic", action = "ContactEdit" });

        endpointRouteBuilder.MapControllerRoute("CustomerProfile.ContactAdd", "customer/business-details/contact/add",
            new { controller = "CustomerProfilePublic", action = "ContactAdd" });

        endpointRouteBuilder.MapControllerRoute("CustomerProfile.ContactDelete", "customer/business-details/contact/{id:int}/delete",
            new { controller = "CustomerProfilePublic", action = "ContactDelete" });

        endpointRouteBuilder.MapControllerRoute(CustomerProfileDefaults.RouteTaxExemption, "customer/tax-exemption",
            new { controller = "CustomerProfilePublic", action = "TaxExemption" });

        endpointRouteBuilder.MapControllerRoute("CustomerProfile.TaxExemptionUpload", "customer/tax-exemption/upload",
            new { controller = "CustomerProfilePublic", action = "TaxExemptionUpload" });

        endpointRouteBuilder.MapControllerRoute(CustomerProfileDefaults.RouteCertificateFile, "customer/tax-exemption/file/{id:int}",
            new { controller = "CustomerProfilePublic", action = "CertificateFile" });
    }
}
