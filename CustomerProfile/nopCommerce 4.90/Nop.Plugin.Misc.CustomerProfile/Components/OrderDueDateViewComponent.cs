using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Misc.CustomerProfile.Logic;
using Nop.Plugin.Misc.CustomerProfile.Models;
using Nop.Plugin.Misc.CustomerProfile.Services;
using Nop.Services.Orders;
using Nop.Web.Framework.Components;
using Nop.Web.Models.Order;

namespace Nop.Plugin.Misc.CustomerProfile.Components;

/// <summary>"Payment due" line on the storefront order details page for unpaid Net 30 orders</summary>
public class OrderDueDateViewComponent : NopViewComponent
{
    private readonly INet30Service _net30Service;
    private readonly IOrderService _orderService;
    private readonly IStoreClock _clock;

    public OrderDueDateViewComponent(INet30Service net30Service, IOrderService orderService, IStoreClock clock)
    {
        _net30Service = net30Service;
        _orderService = orderService;
        _clock = clock;
    }

    public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
    {
        if (additionalData is not OrderDetailsModel details)
            return Content(string.Empty);

        var net30 = await _net30Service.GetByOrderIdAsync(details.Id);
        if (net30 == null)
            return Content(string.Empty);

        var order = await _orderService.GetOrderByIdAsync(details.Id);
        if (order == null || order.PaymentStatus == PaymentStatus.Paid)
            return Content(string.Empty);

        return View(CustomerProfileDefaults.ViewsPath + "/Public/OrderDueDate.cshtml",
            new OrderDueDateModel { DueOn = net30.DueOn, Overdue = net30.DueOn.Date < _clock.Today });
    }
}
