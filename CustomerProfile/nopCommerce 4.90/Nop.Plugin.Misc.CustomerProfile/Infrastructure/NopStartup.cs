using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.CustomerProfile.Logic;
using Nop.Plugin.Misc.CustomerProfile.Services;
using Nop.Services.Messages;

namespace Nop.Plugin.Misc.CustomerProfile.Infrastructure;

public class NopStartup : INopStartup
{
    // After core (2000), so registrations here replace core's
    public int Order => 3000;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IStoreClock, StoreClock>();
        services.AddScoped<IBusinessProfileService, BusinessProfileService>();
        services.AddScoped<ICustomerContactService, CustomerContactService>();
        services.AddScoped<ITaxExemptionService, TaxExemptionService>();
        services.AddScoped<INet30Service, Net30Service>();
        services.AddScoped<MessageTemplateInstaller>();
        services.AddScoped<ContactEditor>();

        // Replaces core's registration (Nop.Web.Framework NopStartup, order 2000) to copy customer emails to contacts
        services.AddScoped<IWorkflowMessageService, CustomerProfileWorkflowMessageService>();
    }

    public void Configure(IApplicationBuilder application)
    {
    }
}
