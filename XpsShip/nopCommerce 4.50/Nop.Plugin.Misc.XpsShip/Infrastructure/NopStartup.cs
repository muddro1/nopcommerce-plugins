using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.XpsShip.Logic;
using Nop.Plugin.Misc.XpsShip.Services;

namespace Nop.Plugin.Misc.XpsShip.Infrastructure;

public class NopStartup : INopStartup
{
    public int Order => 3000;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Singleton: the failed-key counts must outlive a request
        services.AddSingleton<FailedKeyLimiter>();

        services.AddScoped<XpsShipDataService>();
        services.AddScoped<XpsOrderBuilder>();
        services.AddScoped<XpsUpdateProcessor>();
        services.AddScoped<XpsVoidService>();
        services.AddScoped<XpsRequestGuard>();
        services.AddScoped<TrackingLinkResolver>();
        services.AddScoped<ShipToResolver>();
    }

    public void Configure(IApplicationBuilder application)
    {
    }
}
