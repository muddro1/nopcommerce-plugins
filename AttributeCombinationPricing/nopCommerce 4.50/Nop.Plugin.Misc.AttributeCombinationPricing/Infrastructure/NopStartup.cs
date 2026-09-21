using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.AttributeCombinationPricing.Factories;
using Nop.Web.Factories;

namespace Nop.Plugin.Misc.AttributeCombinationPricing.Infrastructure
{
    /// <summary>
    /// Replaces the storefront product model factory so "Associated to product" attribute value
    /// labels reflect attribute combination overrides. Order is above core NopStartup's 2002 so this
    /// registration wins.
    /// </summary>
    public class NopStartup : INopStartup
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<IProductModelFactory, AttributeCombinationProductModelFactory>();
        }

        public void Configure(IApplicationBuilder application)
        {
        }

        public int Order => 3000;
    }
}
