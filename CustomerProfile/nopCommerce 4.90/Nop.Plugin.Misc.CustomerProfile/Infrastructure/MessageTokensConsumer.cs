using Nop.Services.Events;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Plugin.Misc.CustomerProfile.Services;

namespace Nop.Plugin.Misc.CustomerProfile.Infrastructure;

/// <summary>
/// Adds %Order.Net30DueDate%, %Order.Net30Terms% and %Customer.BmwDealership% to order emails.
/// They're always added, empty when they don't apply, so templates can use them unconditionally.
/// </summary>
public class MessageTokensConsumer : IConsumer<MessageTokensAddedEvent>
{
    public const string DueDateToken = "Order.Net30DueDate";
    public const string TermsToken = "Order.Net30Terms";
    public const string DealershipToken = "Customer.BmwDealership";

    private readonly IBusinessProfileService _profileService;
    private readonly INet30Service _net30Service;
    private readonly IOrderService _orderService;

    public MessageTokensConsumer(IBusinessProfileService profileService, INet30Service net30Service, IOrderService orderService)
    {
        _profileService = profileService;
        _net30Service = net30Service;
        _orderService = orderService;
    }

    public async Task HandleEventAsync(MessageTokensAddedEvent eventMessage)
    {
        var tokens = eventMessage.Tokens;
        bool Has(string key) => tokens.Any(t => t.Key == key);

        var orderIdToken = tokens.FirstOrDefault(t => t.Key == "Order.OrderId");
        var orderId = orderIdToken?.Value is int id ? id : 0;

        if (!Has(DueDateToken))
        {
            var net30 = orderId > 0 ? await _net30Service.GetByOrderIdAsync(orderId) : null;
            tokens.Add(new Token(DueDateToken, net30?.DueOn.ToShortDateString() ?? string.Empty));
            tokens.Add(new Token(TermsToken, net30?.TermsDays.ToString() ?? string.Empty));
        }

        if (!Has(DealershipToken))
        {
            var order = orderId > 0 ? await _orderService.GetOrderByIdAsync(orderId) : null;
            var profile = order != null ? await _profileService.GetAsync(order.CustomerId) : null;
            tokens.Add(new Token(DealershipToken, profile?.BmwDealership ?? string.Empty));
        }
    }
}
