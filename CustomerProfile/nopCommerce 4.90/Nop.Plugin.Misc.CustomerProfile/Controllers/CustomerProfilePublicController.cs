using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Customers;
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

    private readonly ContactEditor _contactEditor;
    private readonly IBusinessProfileService _profileService;
    private readonly ICustomerContactService _contactService;
    private readonly ICustomerService _customerService;
    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly IWorkContext _workContext;

    public CustomerProfilePublicController(ContactEditor contactEditor,
        IBusinessProfileService profileService,
        ICustomerContactService contactService,
        ICustomerService customerService,
        ILocalizationService localizationService,
        INotificationService notificationService,
        IWorkContext workContext)
    {
        _contactEditor = contactEditor;
        _profileService = profileService;
        _contactService = contactService;
        _customerService = customerService;
        _localizationService = localizationService;
        _notificationService = notificationService;
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
}
