using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Plugin.Misc.CustomerProfile.Domain;
using Nop.Plugin.Misc.CustomerProfile.Logic;
using Nop.Services.Affiliates;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Core.Events;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Stores;

namespace Nop.Plugin.Misc.CustomerProfile.Services;

/// <summary>
/// Core's workflow message service, plus a copy of each customer order email for every secondary contact
/// subscribed to that kind of email.
/// </summary>
/// <remarks>
/// Each customer-facing Send…Async override opens a "fan-out scope" naming the customer and category, then calls base.
/// Base builds the email and calls <see cref="SendNotificationAsync"/>; that override queues the original, then one
/// copy per contact through base again, so a copy can never fan out itself.
/// The constructor repeats core's; re-check it after upgrading nopCommerce.
/// </remarks>
public class CustomerProfileWorkflowMessageService : WorkflowMessageService
{
    private sealed record FanOutScope(int CustomerId, MessageCategory Category);

    private static readonly AsyncLocal<FanOutScope?> _scope = new();

    private readonly ICustomerContactService _contactService;

    public CustomerProfileWorkflowMessageService(CommonSettings commonSettings,
        EmailAccountSettings emailAccountSettings,
        IAddressService addressService,
        IAffiliateService affiliateService,
        ICustomerService customerService,
        IEmailAccountService emailAccountService,
        IEventPublisher eventPublisher,
        ILanguageService languageService,
        ILocalizationService localizationService,
        IMessageTemplateService messageTemplateService,
        IMessageTokenProvider messageTokenProvider,
        IOrderService orderService,
        IProductService productService,
        IQueuedEmailService queuedEmailService,
        IStoreContext storeContext,
        IStoreService storeService,
        ITokenizer tokenizer,
        MessagesSettings messagesSettings,
        ICustomerContactService contactService)
        : base(commonSettings, emailAccountSettings, addressService, affiliateService, customerService, emailAccountService,
            eventPublisher, languageService, localizationService, messageTemplateService, messageTokenProvider, orderService,
            productService, queuedEmailService, storeContext, storeService, tokenizer, messagesSettings)
    {
        _contactService = contactService;
    }

    internal static async Task<T> RunInScopeAsync<T>(int customerId, MessageCategory category, Func<Task<T>> send)
    {
        var previous = _scope.Value;
        _scope.Value = new FanOutScope(customerId, category);
        try
        {
            return await send();
        }
        finally
        {
            _scope.Value = previous;
        }
    }

    public override async Task<int> SendNotificationAsync(MessageTemplate messageTemplate, EmailAccount emailAccount, int languageId,
        IList<Token> tokens, string toEmailAddress, string toName, string? attachmentFilePath = null, string? attachmentFileName = null,
        string? replyToEmailAddress = null, string? replyToName = null, string? fromEmail = null, string? fromName = null,
        string? subject = null, bool ignoreDelayBeforeSend = false)
    {
        var id = await base.SendNotificationAsync(messageTemplate, emailAccount, languageId, tokens, toEmailAddress, toName,
            attachmentFilePath, attachmentFileName, replyToEmailAddress, replyToName, fromEmail, fromName, subject, ignoreDelayBeforeSend);

        var scope = _scope.Value;
        if (scope == null)
            return id;

        var contacts = await _contactService.GetForCustomerAsync(scope.CustomerId, activeOnly: true);
        foreach (var recipient in ContactRecipientResolver.Resolve(contacts, scope.Category, toEmailAddress))
        {
            var copyId = await base.SendNotificationAsync(messageTemplate, emailAccount, languageId, tokens, recipient.Email, recipient.Name,
                attachmentFilePath, attachmentFileName, replyToEmailAddress, replyToName, fromEmail, fromName, subject, ignoreDelayBeforeSend);

            // One store BCC per message, not one per contact
            var copy = await _queuedEmailService.GetQueuedEmailByIdAsync(copyId);
            if (copy != null && !string.IsNullOrEmpty(copy.Bcc))
            {
                copy.Bcc = null;
                await _queuedEmailService.UpdateQueuedEmailAsync(copy);
            }
        }

        return id;
    }

    private async Task<int> CustomerIdOfOrderAsync(int orderId) =>
        (await _orderService.GetOrderByIdAsync(orderId))?.CustomerId ?? 0;

    #region Customer-facing emails

    public override Task<IList<int>> SendOrderPlacedCustomerNotificationAsync(Order order, int languageId,
        string? attachmentFilePath = null, string? attachmentFileName = null) =>
        RunInScopeAsync(order.CustomerId, MessageCategory.OrderConfirmations,
            () => base.SendOrderPlacedCustomerNotificationAsync(order, languageId, attachmentFilePath, attachmentFileName));

    public override Task<IList<int>> SendOrderProcessingCustomerNotificationAsync(Order order, int languageId,
        string? attachmentFilePath = null, string? attachmentFileName = null) =>
        RunInScopeAsync(order.CustomerId, MessageCategory.OrderConfirmations,
            () => base.SendOrderProcessingCustomerNotificationAsync(order, languageId, attachmentFilePath, attachmentFileName));

    public override Task<IList<int>> SendOrderPaidCustomerNotificationAsync(Order order, int languageId,
        string? attachmentFilePath = null, string? attachmentFileName = null) =>
        RunInScopeAsync(order.CustomerId, MessageCategory.InvoicesPayments,
            () => base.SendOrderPaidCustomerNotificationAsync(order, languageId, attachmentFilePath, attachmentFileName));

    public override Task<IList<int>> SendOrderCompletedCustomerNotificationAsync(Order order, int languageId,
        string? attachmentFilePath = null, string? attachmentFileName = null) =>
        RunInScopeAsync(order.CustomerId, MessageCategory.InvoicesPayments,
            () => base.SendOrderCompletedCustomerNotificationAsync(order, languageId, attachmentFilePath, attachmentFileName));

    public override Task<IList<int>> SendOrderRefundedCustomerNotificationAsync(Order order, decimal refundedAmount, int languageId) =>
        RunInScopeAsync(order.CustomerId, MessageCategory.InvoicesPayments,
            () => base.SendOrderRefundedCustomerNotificationAsync(order, refundedAmount, languageId));

    public override Task<IList<int>> SendOrderCancelledCustomerNotificationAsync(Order order, int languageId) =>
        RunInScopeAsync(order.CustomerId, MessageCategory.Cancellations,
            () => base.SendOrderCancelledCustomerNotificationAsync(order, languageId));

    public override async Task<IList<int>> SendShipmentSentCustomerNotificationAsync(Shipment shipment, int languageId) =>
        await RunInScopeAsync(await CustomerIdOfOrderAsync(shipment.OrderId), MessageCategory.Shipping,
            () => base.SendShipmentSentCustomerNotificationAsync(shipment, languageId));

    public override async Task<IList<int>> SendShipmentReadyForPickupNotificationAsync(Shipment shipment, int languageId) =>
        await RunInScopeAsync(await CustomerIdOfOrderAsync(shipment.OrderId), MessageCategory.Shipping,
            () => base.SendShipmentReadyForPickupNotificationAsync(shipment, languageId));

    public override async Task<IList<int>> SendShipmentDeliveredCustomerNotificationAsync(Shipment shipment, int languageId) =>
        await RunInScopeAsync(await CustomerIdOfOrderAsync(shipment.OrderId), MessageCategory.Shipping,
            () => base.SendShipmentDeliveredCustomerNotificationAsync(shipment, languageId));

    public override async Task<IList<int>> SendNewOrderNoteAddedCustomerNotificationAsync(OrderNote orderNote, int languageId) =>
        await RunInScopeAsync(await CustomerIdOfOrderAsync(orderNote.OrderId), MessageCategory.OrderNotes,
            () => base.SendNewOrderNoteAddedCustomerNotificationAsync(orderNote, languageId));

    public override Task<IList<int>> SendNewReturnRequestCustomerNotificationAsync(ReturnRequest returnRequest, OrderItem orderItem, Order order) =>
        RunInScopeAsync(order.CustomerId, MessageCategory.Returns,
            () => base.SendNewReturnRequestCustomerNotificationAsync(returnRequest, orderItem, order));

    public override Task<IList<int>> SendReturnRequestStatusChangedCustomerNotificationAsync(ReturnRequest returnRequest, OrderItem orderItem, Order order) =>
        RunInScopeAsync(order.CustomerId, MessageCategory.Returns,
            () => base.SendReturnRequestStatusChangedCustomerNotificationAsync(returnRequest, orderItem, order));

    #endregion
}
