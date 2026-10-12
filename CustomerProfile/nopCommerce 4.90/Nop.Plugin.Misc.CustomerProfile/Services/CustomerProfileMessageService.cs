using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Stores;
using Nop.Core.Events;
using Nop.Plugin.Misc.CustomerProfile.Domain;
using Nop.Plugin.Misc.CustomerProfile.Infrastructure;
using Nop.Plugin.Misc.CustomerProfile.Logic;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Stores;

namespace Nop.Plugin.Misc.CustomerProfile.Services;

/// <summary>The plugin's own emails: tax exemption review and expiry, pending review nudges, Net 30 overdue</summary>
public interface ICustomerProfileMessageService
{
    Task SendCertificateSubmittedAsync(TaxExemptionCertificate certificate);
    Task SendCertificateReviewedAsync(TaxExemptionCertificate certificate);
    Task SendExpiringAsync(TaxExemptionCertificate certificate);
    Task SendExpiredAsync(TaxExemptionCertificate certificate, bool exemptionRemoved);
    Task SendPendingNudgeAsync(TaxExemptionCertificate certificate);
    Task SendNet30OverdueAsync(Net30Order net30Order, Order order);
}

public class CustomerProfileMessageService : ICustomerProfileMessageService
{
    private readonly CustomerProfileSettings _settings;
    private readonly EmailAccountSettings _emailAccountSettings;
    private readonly IBusinessProfileService _profileService;
    private readonly ICustomerContactService _contactService;
    private readonly ICustomerService _customerService;
    private readonly IEmailAccountService _emailAccountService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILanguageService _languageService;
    private readonly ILocalizationService _localizationService;
    private readonly IMessageTemplateService _messageTemplateService;
    private readonly IMessageTokenProvider _messageTokenProvider;
    private readonly IQueuedEmailService _queuedEmailService;
    private readonly IStoreClock _clock;
    private readonly IStoreContext _storeContext;
    private readonly IStoreService _storeService;
    private readonly IWorkflowMessageService _workflowMessageService;

    public CustomerProfileMessageService(CustomerProfileSettings settings,
        EmailAccountSettings emailAccountSettings,
        IBusinessProfileService profileService,
        ICustomerContactService contactService,
        ICustomerService customerService,
        IEmailAccountService emailAccountService,
        IEventPublisher eventPublisher,
        ILanguageService languageService,
        ILocalizationService localizationService,
        IMessageTemplateService messageTemplateService,
        IMessageTokenProvider messageTokenProvider,
        IQueuedEmailService queuedEmailService,
        IStoreClock clock,
        IStoreContext storeContext,
        IStoreService storeService,
        IWorkflowMessageService workflowMessageService)
    {
        _settings = settings;
        _emailAccountSettings = emailAccountSettings;
        _profileService = profileService;
        _contactService = contactService;
        _customerService = customerService;
        _emailAccountService = emailAccountService;
        _eventPublisher = eventPublisher;
        _languageService = languageService;
        _localizationService = localizationService;
        _messageTemplateService = messageTemplateService;
        _messageTokenProvider = messageTokenProvider;
        _queuedEmailService = queuedEmailService;
        _clock = clock;
        _storeContext = storeContext;
        _storeService = storeService;
        _workflowMessageService = workflowMessageService;
    }

    #region Public

    public Task SendCertificateSubmittedAsync(TaxExemptionCertificate certificate) =>
        SendForCertificateAsync(certificate, CustomerProfileDefaults.TplSubmittedOwner, toCustomer: false);

    public Task SendCertificateReviewedAsync(TaxExemptionCertificate certificate) =>
        SendForCertificateAsync(certificate, CustomerProfileDefaults.TplReviewedCustomer, toCustomer: true);

    public async Task SendExpiringAsync(TaxExemptionCertificate certificate)
    {
        await SendForCertificateAsync(certificate, CustomerProfileDefaults.TplExpiringCustomer, toCustomer: true);
        await SendForCertificateAsync(certificate, CustomerProfileDefaults.TplExpiringOwner, toCustomer: false);
    }

    public async Task SendExpiredAsync(TaxExemptionCertificate certificate, bool exemptionRemoved)
    {
        await SendForCertificateAsync(certificate, CustomerProfileDefaults.TplExpiredCustomer, toCustomer: true);
        await SendForCertificateAsync(certificate, CustomerProfileDefaults.TplExpiredOwner, toCustomer: false,
            await _localizationService.GetResourceAsync(CustomerProfileDefaults.LocalePrefix
                + (exemptionRemoved ? "TaxExemption.Owner.ExemptionRemoved" : "TaxExemption.Owner.ExemptionKept")));
    }

    public Task SendPendingNudgeAsync(TaxExemptionCertificate certificate) =>
        SendForCertificateAsync(certificate, CustomerProfileDefaults.TplPendingOwner, toCustomer: false);

    public async Task SendNet30OverdueAsync(Net30Order net30Order, Order order)
    {
        var customer = await _customerService.GetCustomerByIdAsync(order.CustomerId);
        if (customer == null || string.IsNullOrEmpty(customer.Email))
            return;

        var store = await _storeService.GetStoreByIdAsync(order.StoreId) ?? await _storeContext.GetCurrentStoreAsync();
        var languageId = await LanguageIdAsync(customer, store.Id);

        await SendAsync(CustomerProfileDefaults.TplOverdueCustomer, store, languageId, customer, MessageCategory.InvoicesPayments,
            ccStore: true, async tokens => await _messageTokenProvider.AddOrderTokensAsync(tokens, order, languageId));
    }

    #endregion

    #region Sending

    private async Task SendForCertificateAsync(TaxExemptionCertificate certificate, string templateName, bool toCustomer,
        string? exemptionRemovedText = null)
    {
        var customer = await _customerService.GetCustomerByIdAsync(certificate.CustomerId);
        if (customer == null)
            return;

        var store = await _storeService.GetStoreByIdAsync(customer.RegisteredInStoreId) ?? await _storeContext.GetCurrentStoreAsync();
        var languageId = await LanguageIdAsync(customer, store.Id);
        var storeUrl = store.Url.TrimEnd('/') + "/";

        var stateName = await _localizationService.GetResourceAsync(
            CustomerProfileDefaults.LocalePrefix + "TaxExemption.State." + certificate.State, languageId);
        var daysLeft = certificate.ExpiresOn.HasValue ? Math.Max((certificate.ExpiresOn.Value.Date - _clock.Today).Days, 0) : 0;

        Task AddTokens(IList<Token> tokens)
        {
            tokens.Add(new Token("TaxExemption.ExpiresOn", certificate.ExpiresOn?.ToShortDateString() ?? string.Empty));
            tokens.Add(new Token("TaxExemption.DaysLeft", daysLeft));
            tokens.Add(new Token("TaxExemption.State", stateName.ToLowerInvariant()));
            tokens.Add(new Token("TaxExemption.ReviewNote", certificate.ReviewNote ?? string.Empty));
            tokens.Add(new Token("TaxExemption.CertificateNumber", certificate.CertificateNumber ?? string.Empty));
            tokens.Add(new Token("TaxExemption.UploadedOn", _clock.ToStoreDate(certificate.UploadedOnUtc).ToShortDateString()));
            tokens.Add(new Token("TaxExemption.UploadUrl", storeUrl + "customer/tax-exemption", true));
            tokens.Add(new Token("TaxExemption.AdminCustomerUrl", $"{storeUrl}Admin/Customer/Edit/{customer.Id}", true));
            tokens.Add(new Token("TaxExemption.ExemptionRemoved", exemptionRemovedText ?? string.Empty));
            return Task.CompletedTask;
        }

        if (toCustomer)
            await SendAsync(templateName, store, languageId, customer, MessageCategory.TaxExemption, ccStore: false, AddTokens);
        else
            await SendToStoreAsync(templateName, store, languageId, customer, AddTokens);
    }

    /// <summary>To the customer, then one copy per contact subscribed to <paramref name="category"/></summary>
    private async Task SendAsync(string templateName, Store store, int languageId, Customer customer, MessageCategory category,
        bool ccStore, Func<IList<Token>, Task> addTokens)
    {
        if (string.IsNullOrEmpty(customer.Email))
            return;

        foreach (var template in await ActiveTemplatesAsync(templateName, store.Id))
        {
            var emailAccount = await EmailAccountAsync(template);
            var tokens = await CommonTokensAsync(store, emailAccount, languageId, customer, addTokens);
            await _eventPublisher.MessageTokensAddedAsync(template, tokens);

            var name = $"{customer.FirstName} {customer.LastName}".Trim();
            var id = await _workflowMessageService.SendNotificationAsync(template, emailAccount, languageId, tokens, customer.Email, name);
            if (ccStore)
                await SetCcAsync(id, emailAccount.Email);

            var contacts = await _contactService.GetForCustomerAsync(customer.Id, activeOnly: true);
            foreach (var recipient in ContactRecipientResolver.Resolve(contacts, category, customer.Email))
                await _workflowMessageService.SendNotificationAsync(template, emailAccount, languageId, tokens, recipient.Email, recipient.Name);
        }
    }

    /// <summary>To the store's email account, with the settings CC list</summary>
    private async Task SendToStoreAsync(string templateName, Store store, int languageId, Customer customer, Func<IList<Token>, Task> addTokens)
    {
        foreach (var template in await ActiveTemplatesAsync(templateName, store.Id))
        {
            var emailAccount = await EmailAccountAsync(template);
            var tokens = await CommonTokensAsync(store, emailAccount, languageId, customer, addTokens);
            await _eventPublisher.MessageTokensAddedAsync(template, tokens);

            var id = await _workflowMessageService.SendNotificationAsync(template, emailAccount, languageId, tokens,
                emailAccount.Email, emailAccount.DisplayName);
            await SetCcAsync(id);
        }
    }

    private async Task<IList<Token>> CommonTokensAsync(Store store, EmailAccount emailAccount, int languageId, Customer customer,
        Func<IList<Token>, Task> addTokens)
    {
        var tokens = new List<Token>();
        await _messageTokenProvider.AddStoreTokensAsync(tokens, store, emailAccount, languageId);
        await _messageTokenProvider.AddCustomerTokensAsync(tokens, customer);
        var profile = await _profileService.GetAsync(customer.Id);
        tokens.Add(new Token(MessageTokensConsumer.DealershipToken, profile?.BmwDealership ?? string.Empty));
        await addTokens(tokens);
        return tokens;
    }

    /// <summary>Writes the settings CC list (plus any extra address) into a queued email's CC field</summary>
    private async Task SetCcAsync(int queuedEmailId, string? extra = null)
    {
        var cc = EmailList.ToCcField(string.Join(";", new[] { extra, _settings.NotificationCcEmails }.Where(s => !string.IsNullOrEmpty(s))));
        if (cc.Length == 0)
            return;

        var email = await _queuedEmailService.GetQueuedEmailByIdAsync(queuedEmailId);
        if (email == null)
            return;

        email.CC = cc;
        await _queuedEmailService.UpdateQueuedEmailAsync(email);
    }

    private async Task<IList<MessageTemplate>> ActiveTemplatesAsync(string name, int storeId) =>
        (await _messageTemplateService.GetMessageTemplatesByNameAsync(name, storeId)).Where(t => t.IsActive).ToList();

    private async Task<EmailAccount> EmailAccountAsync(MessageTemplate template) =>
        await _emailAccountService.GetEmailAccountByIdAsync(template.EmailAccountId)
        ?? await _emailAccountService.GetEmailAccountByIdAsync(_emailAccountSettings.DefaultEmailAccountId)
        ?? (await _emailAccountService.GetAllEmailAccountsAsync()).First();

    private async Task<int> LanguageIdAsync(Customer customer, int storeId)
    {
        var languages = await _languageService.GetAllLanguagesAsync(storeId: storeId);
        return languages.FirstOrDefault(l => l.Id == customer.LanguageId)?.Id ?? languages.FirstOrDefault()?.Id ?? 0;
    }

    #endregion
}
