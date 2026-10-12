using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.CustomerProfile.Domain;
using Nop.Plugin.Misc.CustomerProfile.Logic;
using Nop.Plugin.Misc.CustomerProfile.Models;
using Nop.Plugin.Misc.CustomerProfile.Services;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Web.Framework.Controllers;

namespace Nop.Plugin.Misc.CustomerProfile.Controllers;

/// <summary>My Account pages: Business details (dealership, contacts) and Tax exemption</summary>
[AutoValidateAntiforgeryToken]
public partial class CustomerProfilePublicController : BasePluginController
{
    private const string BusinessDetailsView = CustomerProfileDefaults.ViewsPath + "/Public/BusinessDetails.cshtml";
    private const string ContactEditView = CustomerProfileDefaults.ViewsPath + "/Public/ContactEdit.cshtml";
    private const string TaxExemptionView = CustomerProfileDefaults.ViewsPath + "/Public/TaxExemption.cshtml";

    private readonly CustomerProfileSettings _settings;
    private readonly ContactEditor _contactEditor;
    private readonly IBusinessProfileService _profileService;
    private readonly ICustomerContactService _contactService;
    private readonly ICustomerService _customerService;
    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly IStoreClock _clock;
    private readonly ITaxExemptionService _taxExemptionService;
    private readonly IWorkContext _workContext;

    public CustomerProfilePublicController(CustomerProfileSettings settings,
        ContactEditor contactEditor,
        IBusinessProfileService profileService,
        ICustomerContactService contactService,
        ICustomerService customerService,
        ILocalizationService localizationService,
        INotificationService notificationService,
        IStoreClock clock,
        ITaxExemptionService taxExemptionService,
        IWorkContext workContext)
    {
        _settings = settings;
        _contactEditor = contactEditor;
        _profileService = profileService;
        _contactService = contactService;
        _customerService = customerService;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _clock = clock;
        _taxExemptionService = taxExemptionService;
        _workContext = workContext;
    }

    private Task<string> TAsync(string key) => _localizationService.GetResourceAsync(CustomerProfileDefaults.LocalePrefix + key);

    /// <returns>The current customer when registered, otherwise null (the caller returns Challenge)</returns>
    private async Task<Customer?> GetRegisteredCustomerAsync()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        return await _customerService.IsRegisteredAsync(customer) ? customer : null;
    }

    #region Business details

    private async Task<BusinessDetailsModel> PrepareBusinessDetailsAsync(Customer customer, ContactModel? newContact = null)
    {
        var profile = await _profileService.GetAsync(customer.Id);
        var contacts = await _contactService.GetForCustomerAsync(customer.Id);
        return new BusinessDetailsModel
        {
            BmwDealership = profile?.BmwDealership,
            Contacts = contacts,
            NewContact = newContact ?? new ContactModel(),
            CanAddContact = contacts.Count < CustomerProfileDefaults.MaxContactsPerCustomer
        };
    }

    [HttpGet]
    public async Task<IActionResult> BusinessDetails()
    {
        var customer = await GetRegisteredCustomerAsync();
        if (customer == null)
            return Challenge();

        return View(BusinessDetailsView, await PrepareBusinessDetailsAsync(customer));
    }

    [HttpPost]
    public async Task<IActionResult> BusinessDetails(string? bmwDealership)
    {
        var customer = await GetRegisteredCustomerAsync();
        if (customer == null)
            return Challenge();

        var profile = await _profileService.GetOrNewAsync(customer.Id);
        profile.BmwDealership = bmwDealership;
        await _profileService.SaveAsync(profile);

        _notificationService.SuccessNotification(await TAsync("BusinessDetails.Saved"));
        return RedirectToRoute(CustomerProfileDefaults.RouteBusinessDetails);
    }

    [HttpPost]
    public async Task<IActionResult> ContactAdd(ContactModel model)
    {
        var customer = await GetRegisteredCustomerAsync();
        if (customer == null)
            return Challenge();

        model.Id = 0;
        var errors = await _contactEditor.SaveAsync(customer.Id, model, customer.Id);
        if (errors.Any())
        {
            foreach (var error in errors)
                ModelState.AddModelError(string.Empty, error);
            return View(BusinessDetailsView, await PrepareBusinessDetailsAsync(customer, model));
        }

        _notificationService.SuccessNotification(await TAsync("Contacts.Saved"));
        return RedirectToRoute(CustomerProfileDefaults.RouteBusinessDetails);
    }

    [HttpGet]
    public async Task<IActionResult> ContactEdit(int id)
    {
        var customer = await GetRegisteredCustomerAsync();
        if (customer == null)
            return Challenge();

        var contact = await _contactService.GetByIdAsync(id);
        if (contact == null || contact.CustomerId != customer.Id)
            return NotFound();

        return View(ContactEditView, ContactModel.From(contact));
    }

    [HttpPost]
    public async Task<IActionResult> ContactEdit(int id, ContactModel model)
    {
        var customer = await GetRegisteredCustomerAsync();
        if (customer == null)
            return Challenge();

        var contact = await _contactService.GetByIdAsync(id);
        if (contact == null || contact.CustomerId != customer.Id)
            return NotFound();

        model.Id = id;
        var errors = await _contactEditor.SaveAsync(customer.Id, model, customer.Id);
        if (errors.Any())
        {
            foreach (var error in errors)
                ModelState.AddModelError(string.Empty, error);
            return View(ContactEditView, model);
        }

        _notificationService.SuccessNotification(await TAsync("Contacts.Saved"));
        return RedirectToRoute(CustomerProfileDefaults.RouteBusinessDetails);
    }

    [HttpPost]
    public async Task<IActionResult> ContactDelete(int id)
    {
        var customer = await GetRegisteredCustomerAsync();
        if (customer == null)
            return Challenge();

        if (!await _contactEditor.DeleteAsync(customer.Id, id))
            return NotFound();

        _notificationService.SuccessNotification(await TAsync("Contacts.Deleted"));
        return RedirectToRoute(CustomerProfileDefaults.RouteBusinessDetails);
    }

    #endregion

    #region Tax exemption

    private async Task<TaxExemptionPageModel> PrepareTaxExemptionAsync(Customer customer)
    {
        var certificates = await _taxExemptionService.GetForCustomerAsync(customer.Id);
        return new TaxExemptionPageModel
        {
            CurrentValid = await _taxExemptionService.GetCurrentValidAsync(customer.Id),
            LatestExpired = certificates.Where(c => c.State == CertificateState.Expired).MaxBy(c => c.ExpiresOn),
            HasPending = certificates.Any(c => c.State == CertificateState.PendingReview),
            Certificates = certificates,
            MaxUploadSizeMb = _settings.MaxUploadSizeMb,
            Today = _clock.Today
        };
    }

    [HttpGet]
    public async Task<IActionResult> TaxExemption()
    {
        var customer = await GetRegisteredCustomerAsync();
        if (customer == null)
            return Challenge();

        return View(TaxExemptionView, await PrepareTaxExemptionAsync(customer));
    }

    [HttpPost]
    [RequestFormLimits(MultipartBodyLengthLimit = 26 * 1024 * 1024)]
    [RequestSizeLimit(26 * 1024 * 1024)]
    public async Task<IActionResult> TaxExemptionUpload(IFormFile? file, DateTime? expiresOn, string? certificateNumber, string? issuingState)
    {
        var customer = await GetRegisteredCustomerAsync();
        if (customer == null)
            return Challenge();

        var errors = await _taxExemptionService.UploadAsync(customer.Id, file, expiresOn, certificateNumber, issuingState,
            customer.Id, approveImmediately: false);
        if (errors.Any())
        {
            foreach (var error in errors)
                ModelState.AddModelError(string.Empty, error);
            return View(TaxExemptionView, await PrepareTaxExemptionAsync(customer));
        }

        _notificationService.SuccessNotification(await TAsync("TaxExemption.Uploaded"));
        return RedirectToRoute(CustomerProfileDefaults.RouteTaxExemption);
    }

    [HttpGet]
    public async Task<IActionResult> CertificateFile(int id)
    {
        var customer = await GetRegisteredCustomerAsync();
        if (customer == null)
            return Challenge();

        // 404 rather than 403 for someone else's certificate, so ids can't be probed
        var (certificate, download) = await _taxExemptionService.GetFileAsync(id);
        if (certificate == null || download == null || certificate.CustomerId != customer.Id)
            return NotFound();

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(download.DownloadBinary, certificate.ContentType, certificate.FileName);
    }

    #endregion
}
