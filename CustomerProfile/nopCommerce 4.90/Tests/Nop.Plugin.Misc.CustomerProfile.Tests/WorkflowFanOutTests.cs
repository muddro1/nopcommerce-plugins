using System.Linq.Expressions;
using FluentAssertions;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Messages;
using Nop.Plugin.Misc.CustomerProfile.Domain;
using Nop.Plugin.Misc.CustomerProfile.Services;
using Nop.Services.Affiliates;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Core.Events;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Stores;
using NUnit.Framework;

namespace Nop.Plugin.Misc.CustomerProfile.Tests;

public class WorkflowFanOutTests
{
    private const int CustomerId = 5;
    private List<QueuedEmail> _queued;
    private List<CustomerContact> _contacts;
    private CustomerProfileWorkflowMessageService _service;
    private MessageTemplate _template;
    private EmailAccount _account;

    [SetUp]
    public void SetUp()
    {
        _queued = new List<QueuedEmail>();
        _contacts = new List<CustomerContact>();

        var queue = new Mock<IQueuedEmailService>();
        queue.Setup(q => q.InsertQueuedEmailAsync(It.IsAny<QueuedEmail>())).Returns((QueuedEmail e) =>
        {
            e.Id = _queued.Count + 1;
            _queued.Add(e);
            return Task.CompletedTask;
        });
        queue.Setup(q => q.GetQueuedEmailByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => _queued.SingleOrDefault(e => e.Id == id));
        queue.Setup(q => q.UpdateQueuedEmailAsync(It.IsAny<QueuedEmail>())).Returns(Task.CompletedTask);

        // Return the template's own value for whichever property is asked for
        var localization = new Mock<ILocalizationService>();
        localization.Setup(l => l.GetLocalizedAsync(It.IsAny<MessageTemplate>(), It.IsAny<Expression<Func<MessageTemplate, string>>>(),
                It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns((MessageTemplate t, Expression<Func<MessageTemplate, string>> selector, int? _, bool _, bool _) =>
                Task.FromResult(selector.Compile()(t)));

        var contactService = new Mock<ICustomerContactService>();
        contactService.Setup(c => c.GetForCustomerAsync(CustomerId, true))
            .ReturnsAsync(() => _contacts.Where(c => c.Active).ToList());

        _service = new CustomerProfileWorkflowMessageService(new CommonSettings(), new EmailAccountSettings(),
            Mock.Of<IAddressService>(), Mock.Of<IAffiliateService>(), Mock.Of<ICustomerService>(), Mock.Of<IEmailAccountService>(),
            Mock.Of<IEventPublisher>(), Mock.Of<ILanguageService>(), localization.Object, Mock.Of<IMessageTemplateService>(),
            Mock.Of<IMessageTokenProvider>(), Mock.Of<IOrderService>(), Mock.Of<IProductService>(), queue.Object,
            Mock.Of<IStoreContext>(), Mock.Of<IStoreService>(), new Tokenizer(new MessageTemplatesSettings()), new MessagesSettings(),
            contactService.Object);

        _template = new MessageTemplate { Subject = "Order %Order.Number% shipped", Body = "Body", BccEmailAddresses = "store@shop.com" };
        _account = new EmailAccount { Id = 1, Email = "noreply@shop.com", DisplayName = "Shop" };
    }

    private Task<int> Send() => _service.SendNotificationAsync(_template, _account, 1,
        new List<Token> { new("Order.Number", "1001") }, "owner@dealer.com", "Owner", "/tmp/invoice.pdf", "invoice.pdf");

    private Task<int> SendInScope(MessageCategory category) =>
        CustomerProfileWorkflowMessageService.RunInScopeAsync(CustomerId, category, Send);

    [Test]
    public async Task Outside_a_scope_sends_only_the_original()
    {
        _contacts.Add(new CustomerContact { CustomerId = CustomerId, Email = "ap@dealer.com", Categories = MessageCategory.Shipping });

        await Send();

        _queued.Should().ContainSingle().Which.To.Should().Be("owner@dealer.com");
    }

    [Test]
    public async Task Subscribed_contacts_get_their_own_copy()
    {
        _contacts.Add(new CustomerContact { CustomerId = CustomerId, Name = "AP", Email = "ap@dealer.com", Categories = MessageCategory.Shipping });
        _contacts.Add(new CustomerContact { CustomerId = CustomerId, Name = "Parts", Email = "parts@dealer.com", Categories = MessageCategory.Shipping | MessageCategory.Returns });

        var id = await SendInScope(MessageCategory.Shipping);

        _queued.Should().HaveCount(3);
        _queued.Single(e => e.Id == id).To.Should().Be("owner@dealer.com");

        var copies = _queued.Where(e => e.Id != id).ToList();
        copies.Select(e => (e.To, e.ToName)).Should().BeEquivalentTo(new[] { ("ap@dealer.com", "AP"), ("parts@dealer.com", "Parts") });
        copies.Should().AllSatisfy(e =>
        {
            e.Subject.Should().Be("Order 1001 shipped");
            e.AttachmentFilePath.Should().Be("/tmp/invoice.pdf");
            e.AttachmentFileName.Should().Be("invoice.pdf");
        });
    }

    [Test]
    public async Task Copies_drop_the_template_bcc_but_the_original_keeps_it()
    {
        _contacts.Add(new CustomerContact { CustomerId = CustomerId, Email = "ap@dealer.com", Categories = MessageCategory.Shipping });

        var id = await SendInScope(MessageCategory.Shipping);

        _queued.Single(e => e.Id == id).Bcc.Should().Be("store@shop.com");
        _queued.Single(e => e.Id != id).Bcc.Should().BeNullOrEmpty();
    }

    [Test]
    public async Task Contact_with_the_original_address_is_not_duplicated()
    {
        _contacts.Add(new CustomerContact { CustomerId = CustomerId, Email = "OWNER@dealer.com", Categories = MessageCategory.Shipping });

        await SendInScope(MessageCategory.Shipping);

        _queued.Should().ContainSingle();
    }

    [Test]
    public async Task Unsubscribed_and_inactive_contacts_get_nothing()
    {
        _contacts.Add(new CustomerContact { CustomerId = CustomerId, Email = "ap@dealer.com", Categories = MessageCategory.InvoicesPayments });
        _contacts.Add(new CustomerContact { CustomerId = CustomerId, Email = "paused@dealer.com", Categories = MessageCategory.Shipping, Active = false });

        await SendInScope(MessageCategory.Shipping);

        _queued.Should().ContainSingle();
    }

    [Test]
    public async Task Scope_ends_when_the_send_returns()
    {
        _contacts.Add(new CustomerContact { CustomerId = CustomerId, Email = "ap@dealer.com", Categories = MessageCategory.Shipping });

        await SendInScope(MessageCategory.Shipping);
        _queued.Clear();
        await Send();

        _queued.Should().ContainSingle();
    }
}
