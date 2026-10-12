using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.AttributeCombinationPricing.Factories;
using Nop.Plugin.Misc.AttributeCombinationPricing.Services;
using Nop.Services.Catalog;
using Nop.Web.Factories;

namespace Nop.Plugin.Misc.AttributeCombinationPricing.Infrastructure
{
    /// <summary>
    /// Replaces the storefront product model factory so "Associated to product" attribute value
    /// labels reflect attribute combination overrides, and the price calculation service so every
    /// other consumer of the underlying price-adjustment calculation (cart, checkout review, and
    /// newly-placed orders' stored attribute description) gets the same correction. Order is above
    /// core's Nop.Web NopStartup (2002, registers IProductModelFactory) and core's Nop.Web.Framework
    /// NopStartup (2000, registers IPriceCalculationService), so both registrations here win.
    /// </summary>
    public class NopStartup : INopStartup
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<IProductModelFactory, AttributeCombinationProductModelFactory>();
            services.AddScoped<IPriceCalculationService, AttributeCombinationPriceCalculationService>();
        }

        public void Configure(IApplicationBuilder application)
        {
        }

        public int Order => 3000;
    }
}
