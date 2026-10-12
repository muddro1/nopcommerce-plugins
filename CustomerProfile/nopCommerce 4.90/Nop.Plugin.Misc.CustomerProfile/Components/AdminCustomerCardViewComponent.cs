using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.CustomerProfile.Models;
using Nop.Plugin.Misc.CustomerProfile.Services;
using Nop.Services.Security;
using Nop.Web.Areas.Admin.Models.Customers;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Misc.CustomerProfile.Components;

/// <summary>The "Business profile" card on the admin customer edit page</summary>
public class AdminCustomerCardViewComponent : NopViewComponent
{
    private readonly CustomerProfileSettings _settings;
    private readonly IBusinessProfileService _profileService;
    private readonly IPermissionService _permissionService;
    private readonly IWorkContext _workContext;

    public AdminCustomerCardViewComponent(CustomerProfileSettings settings,
        IBusinessProfileService profileService,
        IPermissionService permissionService,
        IWorkContext workContext)
    {
        _settings = settings;
        _profileService = profileService;
        _permissionService = permissionService;
        _workContext = workContext;
    }

    public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
    {
        if (additionalData is not CustomerModel customer
            || await _workContext.GetCurrentVendorAsync() != null
            || !await _permissionService.AuthorizeAsync(StandardPermission.Customers.CUSTOMERS_VIEW))
            return Content(string.Empty);

        var profile = await _profileService.GetOrNewAsync(customer.Id);
        var model = new AdminCustomerCardModel
        {
            CustomerId = customer.Id,
            CanEdit = await _permissionService.AuthorizeAsync(StandardPermission.Customers.CUSTOMERS_CREATE_EDIT_DELETE),
            BmwDealership = profile.BmwDealership,
            Net30Enabled = profile.Net30Enabled,
            Net30TermsDays = profile.Net30TermsDays,
            MaxUploadSizeMb = _settings.MaxUploadSizeMb
        };

        return View(CustomerProfileDefaults.ViewsPath + "/Admin/CustomerCard.cshtml", model);
    }
}
