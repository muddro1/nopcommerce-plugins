using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Events;
using Nop.Core.Http.Extensions;
using Nop.Plugin.Misc.CustomerProfile.Services;
using Nop.Services.Events;
using Nop.Services.Plugins;
using Nop.Services.Security;
using Nop.Web.Framework;

namespace Nop.Plugin.Misc.CustomerProfile.Infrastructure;

/// <summary>
/// Saves the admin card's dealership and Net 30 fields when nopCommerce saves the customer from the edit page.
/// </summary>
/// <remarks>
/// This listens for the customer being updated rather than for the posted form, because the core Edit action
/// validates roles and email after the form arrives: saving on arrival would switch on Net 30 for a save that fails.
/// </remarks>
public class AdminCustomerSavedConsumer : IConsumer<EntityUpdatedEvent<Customer>>
{
    private readonly IBusinessProfileService _profileService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPermissionService _permissionService;
    private readonly IPluginManager<IPlugin> _pluginManager;
    private readonly IWorkContext _workContext;

    public AdminCustomerSavedConsumer(IBusinessProfileService profileService,
        IHttpContextAccessor httpContextAccessor,
        IPermissionService permissionService,
        IPluginManager<IPlugin> pluginManager,
        IWorkContext workContext)
    {
        _profileService = profileService;
        _httpContextAccessor = httpContextAccessor;
        _permissionService = permissionService;
        _pluginManager = pluginManager;
        _workContext = workContext;
    }

    public async Task HandleEventAsync(EntityUpdatedEvent<Customer> eventMessage)
    {
        if (_httpContextAccessor.HttpContext is not { } httpContext)
            return;

        var request = httpContext.Request;
        if (!HttpMethods.IsPost(request.Method) || !request.HasFormContentType)
            return;

        // Only the admin customer Edit post, and only for the customer that page is editing
        if (!string.Equals(httpContext.GetRouteValue("area") as string, AreaNames.ADMIN, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(httpContext.GetRouteValue("controller") as string, "Customer", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(httpContext.GetRouteValue("action") as string, "Edit", StringComparison.OrdinalIgnoreCase))
            return;

        var (_, postedId) = await request.TryGetFormValueAsync("Id");
        if (!int.TryParse(postedId.FirstOrDefault(), out var customerId) || customerId != eventMessage.Entity.Id)
            return;

        // Only posts that carry the card have this field
        var (hasCard, dealership) = await request.TryGetFormValueAsync(CustomerProfileDefaults.FieldDealership);
        if (!hasCard)
            return;

        if (await _workContext.GetCurrentVendorAsync() != null
            || !await _permissionService.AuthorizeAsync(StandardPermission.Customers.CUSTOMERS_CREATE_EDIT_DELETE)
            || await _pluginManager.LoadPluginBySystemNameAsync(CustomerProfileDefaults.SystemName) == null)
            return;

        var (_, net30) = await request.TryGetFormValueAsync(CustomerProfileDefaults.FieldNet30Enabled);
        var (_, terms) = await request.TryGetFormValueAsync(CustomerProfileDefaults.FieldNet30TermsDays);

        var profile = await _profileService.GetOrNewAsync(customerId);
        profile.BmwDealership = dealership.ToString();

        // A ticked checkbox posts "true" then the hidden "false"; unticked posts only "false"
        profile.Net30Enabled = string.Equals(net30.FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase);
        if (int.TryParse(terms.FirstOrDefault(), out var days))
            profile.Net30TermsDays = days;

        await _profileService.SaveAsync(profile);
    }
}

/// <summary>Saves the dealership entered on the registration page</summary>
public class CustomerRegisteredConsumer : IConsumer<Nop.Core.Domain.Customers.CustomerRegisteredEvent>
{
    private readonly IBusinessProfileService _profileService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CustomerRegisteredConsumer(IBusinessProfileService profileService, IHttpContextAccessor httpContextAccessor)
    {
        _profileService = profileService;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task HandleEventAsync(Nop.Core.Domain.Customers.CustomerRegisteredEvent eventMessage)
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request == null || !request.HasFormContentType)
            return;

        var (exists, dealership) = await request.TryGetFormValueAsync(CustomerProfileDefaults.FieldDealership);
        if (!exists || string.IsNullOrWhiteSpace(dealership))
            return;

        var profile = await _profileService.GetOrNewAsync(eventMessage.Customer.Id);
        profile.BmwDealership = dealership.ToString();
        await _profileService.SaveAsync(profile);
    }
}
