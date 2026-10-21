using System.Net;
using Nop.Core.Domain.Shipping;
using Nop.Plugin.Misc.XpsShip.Logic;
using Nop.Services.Orders;

namespace Nop.Plugin.Misc.XpsShip.Services;

/// <summary>The address behind %Shipment.XpsShipToAddress%</summary>
public class ShipToResolver
{
    private readonly IOrderService _orderService;
    private readonly XpsOrderBuilder _orderBuilder;
    private readonly XpsShipDataService _dataService;

    public ShipToResolver(IOrderService orderService, XpsOrderBuilder orderBuilder, XpsShipDataService dataService)
    {
        _orderService = orderService;
        _orderBuilder = orderBuilder;
        _dataService = dataService;
    }

    /// <summary>
    /// The address XPS printed on the shipment's label, so an address fixed in XPS shows as shipped. Falls back to
    /// the order's shipping address for shipments made by hand or labels from before the address was kept.
    /// HTML, one line per part.
    /// </summary>
    public async Task<string> ResolveHtmlAsync(Shipment shipment)
    {
        var order = await _orderService.GetOrderByIdAsync(shipment.OrderId);
        if (order == null)
            return string.Empty;

        var number = shipment.TrackingNumber?.Trim().ToUpperInvariant();
        var text = string.IsNullOrEmpty(number) ? null : (await _dataService.FindMapAsync(order.OrderGuid, number))?.ShipToAddress;
        if (string.IsNullOrEmpty(text))
        {
            var a = await _orderBuilder.ReceiverAsync(order);
            text = a == null ? null : ShipTo.Format(a.Name, a.Company, a.Address1, a.Address2, a.City, a.State, a.Zip, a.Country);
        }

        return text == null ? string.Empty : string.Join("<br />", text.Split('\n').Select(WebUtility.HtmlEncode));
    }
}
