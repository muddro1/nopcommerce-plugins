using Nop.Core.Domain.Orders;
using Nop.Core.Events;
using Nop.Plugin.Misc.CustomerProfile.Domain;
using Nop.Plugin.Misc.CustomerProfile.Logic;
using Nop.Plugin.Misc.CustomerProfile.Services;
using Nop.Services.Events;
using Nop.Services.Localization;
using Nop.Services.Orders;

namespace Nop.Plugin.Misc.CustomerProfile.Infrastructure;

/// <summary>Records the due date of each Net 30 order</summary>
public class Net30OrderConsumer : IConsumer<EntityInsertedEvent<Order>>, IConsumer<OrderPlacedEvent>
{
    private readonly CustomerProfileSettings _settings;
    private readonly IBusinessProfileService _profileService;
    private readonly ILocalizationService _localizationService;
    private readonly INet30Service _net30Service;
    private readonly IOrderService _orderService;
    private readonly IStoreClock _clock;

    public Net30OrderConsumer(CustomerProfileSettings settings,
        IBusinessProfileService profileService,
        ILocalizationService localizationService,
        INet30Service net30Service,
        IOrderService orderService,
        IStoreClock clock)
    {
        _settings = settings;
        _profileService = profileService;
        _localizationService = localizationService;
        _net30Service = net30Service;
        _orderService = orderService;
        _clock = clock;
    }

    // Order emails go out before OrderPlacedEvent (OrderProcessingService), so the due date must exist
    // from the moment the order row does, for %Order.Net30DueDate% to work in the order placed email
    public async Task HandleEventAsync(EntityInsertedEvent<Order> eventMessage)
    {
        var order = eventMessage.Entity;
        if (order.PaymentMethodSystemName != CustomerProfileDefaults.SystemName
            || await _net30Service.GetByOrderIdAsync(order.Id) != null)
            return;

        var profile = await _profileService.GetAsync(order.CustomerId);
        var terms = profile?.Net30TermsDays ?? _settings.Net30DefaultTermsDays;

        await _net30Service.InsertAsync(new Net30Order
        {
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            TermsDays = terms,
            DueOn = _clock.ToStoreDate(order.CreatedOnUtc).AddDays(terms)
        });
    }

    // The note keeps the due date on the order itself, visible to the customer, independent of this plugin
    public async Task HandleEventAsync(OrderPlacedEvent eventMessage)
    {
        var order = eventMessage.Order;
        var net30 = await _net30Service.GetByOrderIdAsync(order.Id);
        if (net30 == null)
            return;

        await _orderService.InsertOrderNoteAsync(new OrderNote
        {
            OrderId = order.Id,
            Note = string.Format(await _localizationService.GetResourceAsync(CustomerProfileDefaults.LocalePrefix + "Net30.OrderNote"),
                net30.DueOn.ToShortDateString()),
            DisplayToCustomer = true,
            CreatedOnUtc = DateTime.UtcNow
        });
    }
}
