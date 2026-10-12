using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Core.Http.Extensions;
using Nop.Plugin.Misc.CustomerProfile.Services;
using Nop.Services.Events;
using Nop.Services.Plugins;
using Nop.Services.Security;
using Nop.Web.Areas.Admin.Models.Customers;
using Nop.Web.Framework.Events;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.CustomerProfile.Infrastructure;

/// <summary>Saves the admin card's dealership and Net 30 fields when the core customer form is saved</summary>
public class AdminCustomerSavedConsumer : IConsumer<ModelReceivedEvent<BaseNopModel>>
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

    public async Task HandleEventAsync(ModelReceivedEvent<BaseNopModel> eventMessage)
    {
        // A customer with validation errors is redisplayed unsaved; leave ours unsaved too
        if (eventMessage.Model is not CustomerModel model || model.Id == 0 || !eventMessage.ModelState.IsValid)
            return;

        var request = _httpContextAccessor.HttpContext?.Request;
        if (request == null || !request.HasFormContentType)
            return;

        // Only posts that carry the card (the customer Save buttons) have this field
        var (hasCard, dealership) = await request.TryGetFormValueAsync(CustomerProfileDefaults.FieldDealership);
        if (!hasCard)
            return;

        if (await _workContext.GetCurrentVendorAsync() != null
            || !await _permissionService.AuthorizeAsync(StandardPermission.Customers.CUSTOMERS_CREATE_EDIT_DELETE)
            || await _pluginManager.LoadPluginBySystemNameAsync(CustomerProfileDefaults.SystemName) == null)
            return;

        var (_, net30) = await request.TryGetFormValueAsync(CustomerProfileDefaults.FieldNet30Enabled);
        var (_, terms) = await request.TryGetFormValueAsync(CustomerProfileDefaults.FieldNet30TermsDays);

        var profile = await _profileService.GetOrNewAsync(model.Id);
        profile.BmwDealership = dealership.ToString();

        // A ticked checkbox posts "true" then the hidden "false"; unticked posts only "false"
        profile.Net30Enabled = string.Equals(net30.FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase);
        if (int.TryParse(terms.FirstOrDefault(), out var days))
            profile.Net30TermsDays = days;

        await _profileService.SaveAsync(profile);
    }
}
