using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.BetterSearch.Services;
using Nop.Services.Catalog;

namespace Nop.Plugin.Misc.BetterSearch.Infrastructure
{
    /// <summary>
    /// Replaces <see cref="IProductService"/> so storefront search comes from the Lucene index.
    /// Nop.Web registers IProductService at Order 2002; we are 3000 so ours wins.
    /// </summary>
    public class NopStartup : INopStartup
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            //scoped, not singleton: SearchIndexManager exposes mutable per-search state
            //(LastSearchWasApproximate). A singleton would let concurrent shoppers race on that
            //flag. Every Lucene IndexWriter it opens is scoped to a single method call and
            //disposed before returning, so there is nothing here that benefits from living
            //longer than a request - the cost of a scoped registration is one extra reader open
            //per request, which is negligible below 5,000 products.
            services.AddScoped(serviceProvider =>
            {
                var fileProvider = serviceProvider.GetRequiredService<INopFileProvider>();
                var indexPath = fileProvider.MapPath($"~/App_Data/{BetterSearchDefaults.INDEX_FOLDER}");
                return new SearchIndexManager(indexPath);
            });

            services.AddScoped<IProductService, BetterSearchProductService>();

            //not auto-discovered like the event consumers: ProductIndexEventConsumer and
            //RebuildSearchIndexTask are both resolved by nopCommerce's own container (the
            //latter via ResolveUnregistered, since schedule tasks are looked up by type name),
            //and that resolution fails for a constructor parameter the container has never
            //heard of
            services.AddScoped<ProductIndexInputFactory>();
        }

        public void Configure(IApplicationBuilder application)
        {
        }

        public int Order => 3000;
    }
}
