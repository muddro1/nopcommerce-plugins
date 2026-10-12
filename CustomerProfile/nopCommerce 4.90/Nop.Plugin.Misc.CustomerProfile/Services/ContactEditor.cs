using Nop.Core;
using Nop.Plugin.Misc.CustomerProfile.Domain;
using Nop.Plugin.Misc.CustomerProfile.Models;
using Nop.Services.Localization;

namespace Nop.Plugin.Misc.CustomerProfile.Services;

/// <summary>Validates and saves contacts the same way for admins and customers</summary>
public class ContactEditor
{
    private readonly ICustomerContactService _contactService;
    private readonly ILocalizationService _localizationService;

    public ContactEditor(ICustomerContactService contactService, ILocalizationService localizationService)
    {
        _contactService = contactService;
        _localizationService = localizationService;
    }

    private Task<string> TAsync(string key) => _localizationService.GetResourceAsync(CustomerProfileDefaults.LocalePrefix + key);

    /// <summary>Adds or updates a contact for <paramref name="customerId"/></summary>
    /// <returns>Errors to show; empty when saved. A contact id belonging to another customer is treated as not found.</returns>
    public async Task<IList<string>> SaveAsync(int customerId, ContactModel model, int editorCustomerId)
    {
        var errors = new List<string>();
        var name = model.Name?.Trim() ?? string.Empty;
        var email = model.Email?.Trim() ?? string.Empty;
        var title = model.Title?.Trim();

        if (name.Length == 0)
            errors.Add(await TAsync("Contacts.Error.NameRequired"));
        else if (name.Length > CustomerProfileDefaults.ContactNameMaxLength)
            errors.Add(string.Format(await TAsync("Contacts.Error.TooLong"), await TAsync("Contacts.Name")));

        if (email.Length > CustomerProfileDefaults.ContactEmailMaxLength || !CommonHelper.IsValidEmail(email))
            errors.Add(await TAsync("Contacts.Error.EmailInvalid"));

        if (title?.Length > CustomerProfileDefaults.ContactTitleMaxLength)
            errors.Add(string.Format(await TAsync("Contacts.Error.TooLong"), await TAsync("Contacts.Title")));

        if (model.CategoryFlags == MessageCategory.None)
            errors.Add(await TAsync("Contacts.Error.NoCategories"));

        CustomerContact? contact = null;
        if (model.Id > 0)
        {
            contact = await _contactService.GetByIdAsync(model.Id);
            if (contact == null || contact.CustomerId != customerId)
                return new List<string> { await TAsync("NotFound") };
        }
        else if (await _contactService.CountForCustomerAsync(customerId) >= CustomerProfileDefaults.MaxContactsPerCustomer)
        {
            errors.Add(string.Format(await TAsync("Contacts.Error.TooMany"), CustomerProfileDefaults.MaxContactsPerCustomer));
        }

        if (errors.Count > 0)
            return errors;

        contact ??= new CustomerContact { CustomerId = customerId, CreatedByCustomerId = editorCustomerId };
        contact.Name = name;
        contact.Email = email;
        contact.Title = title;
        contact.Active = model.Active;
        contact.Categories = model.CategoryFlags;

        if (contact.Id == 0)
        {
            await _contactService.InsertAsync(contact);
        }
        else
        {
            contact.UpdatedByCustomerId = editorCustomerId;
            await _contactService.UpdateAsync(contact);
        }

        return errors;
    }

    /// <returns>False when the contact doesn't exist or belongs to another customer</returns>
    public async Task<bool> DeleteAsync(int customerId, int contactId)
    {
        var contact = await _contactService.GetByIdAsync(contactId);
        if (contact == null || contact.CustomerId != customerId)
            return false;

        await _contactService.DeleteAsync(contact);
        return true;
    }
}
