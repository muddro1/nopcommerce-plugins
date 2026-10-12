using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Misc.CustomerProfile.Components;

/// <summary>Dealership field on the registration page</summary>
public class RegisterDealershipViewComponent : NopViewComponent
{
    public IViewComponentResult Invoke(string widgetZone, object additionalData) =>
        View(CustomerProfileDefaults.ViewsPath + "/Public/RegisterDealership.cshtml");
}
