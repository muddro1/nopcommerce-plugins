using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Nop.Core;
using Nop.Services.Customers;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Misc.CustomerProfile.Components;

/// <summary>Business details and Tax exemption links at the end of the My Account menu</summary>
public class AccountNavigationViewComponent : NopViewComponent
{
    private readonly ICustomerService _customerService;
    private readonly IWorkContext _workContext;

    public AccountNavigationViewComponent(ICustomerService customerService, IWorkContext workContext)
    {
        _customerService = customerService;
        _workContext = workContext;
    }

    public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
    {
        if (!await _customerService.IsRegisteredAsync(await _workContext.GetCurrentCustomerAsync()))
            return Content(string.Empty);

        // The route's action tells us which of our pages, if any, is showing
        var action = HttpContext.GetRouteValue("controller") as string == "CustomerProfilePublic"
            ? HttpContext.GetRouteValue("action") as string
            : null;

        return View(CustomerProfileDefaults.ViewsPath + "/Public/AccountNavigation.cshtml", action ?? string.Empty);
    }
}
