using Nop.Core.Domain.Messages;
using Nop.Services.Messages;

namespace Nop.Plugin.Misc.CustomerProfile.Services;

/// <summary>Creates the plugin's message templates on install and removes them on uninstall</summary>
public class MessageTemplateInstaller
{
    private readonly EmailAccountSettings _emailAccountSettings;
    private readonly IMessageTemplateService _messageTemplateService;

    public MessageTemplateInstaller(EmailAccountSettings emailAccountSettings, IMessageTemplateService messageTemplateService)
    {
        _emailAccountSettings = emailAccountSettings;
        _messageTemplateService = messageTemplateService;
    }

    private const string Sign = "<p>%Store.Name%<br /><a href=\"%Store.URL%\">%Store.URL%</a></p>";

    private static readonly (string Name, string Subject, string Body)[] _templates =
    {
        (CustomerProfileDefaults.TplSubmittedOwner,
            "%Store.Name%: tax exemption certificate to review for %Customer.Email%",
            "<p>%Customer.FullName% (%Customer.Email%, dealership: %Customer.BmwDealership%) uploaded a tax exemption certificate.</p>"
            + "<p>Expiration date entered by the customer: %TaxExemption.ExpiresOn%</p>"
            + "<p><a href=\"%TaxExemption.AdminCustomerUrl%\">Review it on the customer page</a></p>"),

        (CustomerProfileDefaults.TplReviewedCustomer,
            "%Store.Name%: your tax exemption certificate was %TaxExemption.State%",
            "<p>Hello %Customer.FullName%,</p>"
            + "<p>Your tax exemption certificate was <strong>%TaxExemption.State%</strong>.</p>"
            + "<p>Expiration date: %TaxExemption.ExpiresOn%</p>"
            + "<p>%TaxExemption.ReviewNote%</p>"
            + "<p>You can see your certificates here: <a href=\"%TaxExemption.UploadUrl%\">%TaxExemption.UploadUrl%</a></p>" + Sign),

        (CustomerProfileDefaults.TplExpiringCustomer,
            "%Store.Name%: your tax exemption certificate expires on %TaxExemption.ExpiresOn%",
            "<p>Hello %Customer.FullName%,</p>"
            + "<p>The tax exemption certificate on file for your account expires on <strong>%TaxExemption.ExpiresOn%</strong> "
            + "(%TaxExemption.DaysLeft% days). To keep tax-exempt pricing after that date, upload a current certificate:</p>"
            + "<p><a href=\"%TaxExemption.UploadUrl%\">%TaxExemption.UploadUrl%</a></p>" + Sign),

        (CustomerProfileDefaults.TplExpiringOwner,
            "%Store.Name%: tax exemption for %Customer.Email% expires on %TaxExemption.ExpiresOn%",
            "<p>The tax exemption certificate for %Customer.FullName% (%Customer.Email%, dealership: %Customer.BmwDealership%) "
            + "expires on %TaxExemption.ExpiresOn% (%TaxExemption.DaysLeft% days). The customer has been sent a reminder.</p>"
            + "<p><a href=\"%TaxExemption.AdminCustomerUrl%\">Customer page</a></p>"),

        (CustomerProfileDefaults.TplExpiredCustomer,
            "%Store.Name%: your tax exemption certificate has expired",
            "<p>Hello %Customer.FullName%,</p>"
            + "<p>The tax exemption certificate on file for your account expired on <strong>%TaxExemption.ExpiresOn%</strong>. "
            + "Orders will include sales tax until a current certificate is approved. Upload one here:</p>"
            + "<p><a href=\"%TaxExemption.UploadUrl%\">%TaxExemption.UploadUrl%</a></p>" + Sign),

        (CustomerProfileDefaults.TplExpiredOwner,
            "%Store.Name%: tax exemption expired for %Customer.Email%",
            "<p>The tax exemption certificate for %Customer.FullName% (%Customer.Email%, dealership: %Customer.BmwDealership%) "
            + "expired on %TaxExemption.ExpiresOn%.</p><p>%TaxExemption.ExemptionRemoved%</p>"
            + "<p><a href=\"%TaxExemption.AdminCustomerUrl%\">Customer page</a></p>"),

        (CustomerProfileDefaults.TplPendingOwner,
            "%Store.Name%: tax exemption certificate still waiting for review (%Customer.Email%)",
            "<p>A tax exemption certificate from %Customer.FullName% (%Customer.Email%) has been waiting for review since %TaxExemption.UploadedOn%.</p>"
            + "<p><a href=\"%TaxExemption.AdminCustomerUrl%\">Review it on the customer page</a></p>"),

        (CustomerProfileDefaults.TplOverdueCustomer,
            "%Store.Name%: payment for order %Order.OrderNumber% is overdue",
            "<p>Hello %Customer.FullName%,</p>"
            + "<p>Payment for order <strong>%Order.OrderNumber%</strong> (%Order.OrderTotal%) was due on %Order.Net30DueDate%. "
            + "If you have already paid, thank you, and please disregard this email.</p>" + Sign)
    };

    public async Task InstallAsync()
    {
        foreach (var (name, subject, body) in _templates)
        {
            if ((await _messageTemplateService.GetMessageTemplatesByNameAsync(name)).Any())
                continue;

            await _messageTemplateService.InsertMessageTemplateAsync(new MessageTemplate
            {
                Name = name,
                Subject = subject,
                Body = body,
                IsActive = true,
                EmailAccountId = _emailAccountSettings.DefaultEmailAccountId,
                LimitedToStores = false
            });
        }
    }

    public async Task UninstallAsync()
    {
        foreach (var (name, _, _) in _templates)
        {
            foreach (var template in await _messageTemplateService.GetMessageTemplatesByNameAsync(name))
                await _messageTemplateService.DeleteMessageTemplateAsync(template);
        }
    }
}
