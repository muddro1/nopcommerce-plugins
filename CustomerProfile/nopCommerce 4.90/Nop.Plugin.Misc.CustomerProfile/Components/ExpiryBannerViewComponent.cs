using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.CustomerProfile.Domain;
using Nop.Plugin.Misc.CustomerProfile.Logic;
using Nop.Plugin.Misc.CustomerProfile.Services;
using Nop.Services.Customers;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Misc.CustomerProfile.Components;

/// <summary>Notice at the top of the My Account menu when the customer's certificate is about to expire or has expired</summary>
public class ExpiryBannerViewComponent : NopViewComponent
{
    private readonly CustomerProfileSettings _settings;
    private readonly ICustomerService _customerService;
    private readonly IStoreClock _clock;
    private readonly ITaxExemptionService _taxExemptionService;
    private readonly IWorkContext _workContext;

    public ExpiryBannerViewComponent(CustomerProfileSettings settings, ICustomerService customerService, IStoreClock clock,
        ITaxExemptionService taxExemptionService, IWorkContext workContext)
    {
        _settings = settings;
        _customerService = customerService;
        _clock = clock;
        _taxExemptionService = taxExemptionService;
        _workContext = workContext;
    }

    public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (!await _customerService.IsRegisteredAsync(customer))
            return Content(string.Empty);

        var certificates = await _taxExemptionService.GetForCustomerAsync(customer.Id);
        if (certificates.Any(c => c.State == CertificateState.PendingReview))
            return Content(string.Empty); // they've already sent a new one

        var today = _clock.Today;
        var current = certificates.Where(c => c.State == CertificateState.Approved && c.ExpiresOn >= today).MaxBy(c => c.ExpiresOn);

        string? key = null;
        DateTime? date = null;
        if (current != null)
        {
            if ((current.ExpiresOn!.Value.Date - today).TotalDays <= _settings.ReminderDaysBeforeExpiry)
                (key, date) = ("TaxExemption.Banner.Expiring", current.ExpiresOn);
        }
        else
        {
            var expired = certificates.Where(c => c.State == CertificateState.Expired).MaxBy(c => c.ExpiresOn);
            if (expired != null)
                (key, date) = ("TaxExemption.Banner.Expired", expired.ExpiresOn);
        }

        if (key == null)
            return Content(string.Empty);

        return View(CustomerProfileDefaults.ViewsPath + "/Public/ExpiryBanner.cshtml", (key, date!.Value));
    }
}
