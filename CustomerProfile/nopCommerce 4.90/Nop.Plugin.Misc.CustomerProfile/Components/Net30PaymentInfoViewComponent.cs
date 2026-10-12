using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.CustomerProfile.Services;
using Nop.Services.Localization;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Misc.CustomerProfile.Components;

/// <summary>Payment info for Net 30. The step is skipped (SkipPaymentInfo), but the interface requires one.</summary>
public class Net30PaymentInfoViewComponent : NopViewComponent
{
    private readonly CustomerProfileSettings _settings;
    private readonly IBusinessProfileService _profileService;
    private readonly ILocalizationService _localizationService;
    private readonly IWorkContext _workContext;

    public Net30PaymentInfoViewComponent(CustomerProfileSettings settings, IBusinessProfileService profileService,
        ILocalizationService localizationService, IWorkContext workContext)
    {
        _settings = settings;
        _profileService = profileService;
        _localizationService = localizationService;
        _workContext = workContext;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var profile = await _profileService.GetAsync(customer.Id);
        var text = string.Format(await _localizationService.GetResourceAsync(CustomerProfileDefaults.LocalePrefix + "Net30.PaymentInfo"),
            profile?.Net30TermsDays ?? _settings.Net30DefaultTermsDays);

        return View(CustomerProfileDefaults.ViewsPath + "/Public/Net30PaymentInfo.cshtml", text);
    }
}
