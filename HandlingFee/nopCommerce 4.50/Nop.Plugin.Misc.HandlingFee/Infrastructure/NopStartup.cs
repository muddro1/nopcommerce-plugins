using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.HandlingFee.Services;
using Nop.Services.Orders;
using Nop.Services.Payments;

namespace Nop.Plugin.Misc.HandlingFee.Infrastructure
{
    /// <summary>
    /// Replaces two core services so the handling fee joins the order total.
    /// Order is above NopStartup's 2000 so these registrations win.
    /// </summary>
    public class NopStartup : INopStartup
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<IPaymentService, HandlingFeePaymentService>();
            services.AddScoped<IOrderTotalCalculationService, HandlingFeeOrderTotalCalculationService>();
        }

        public void Configure(IApplicationBuilder application)
        {
        }

        public int Order => 3000;
    }
}
