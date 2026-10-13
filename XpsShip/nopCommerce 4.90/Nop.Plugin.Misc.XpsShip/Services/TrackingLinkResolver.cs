using Nop.Core.Domain.Shipping;
using Nop.Plugin.Misc.XpsShip.Logic;
using Nop.Services.Orders;
using Nop.Services.Stores;

namespace Nop.Plugin.Misc.XpsShip.Services;

/// <summary>The link behind %Shipment.XpsTrackingUrl%</summary>
public class TrackingLinkResolver
{
    private readonly IOrderService _orderService;
    private readonly IStoreService _storeService;
    private readonly XpsShipDataService _dataService;

    public TrackingLinkResolver(IOrderService orderService, IStoreService storeService, XpsShipDataService dataService)
    {
        _orderService = orderService;
        _storeService = storeService;
        _dataService = dataService;
    }

    /// <summary>
    /// The carrier's tracking page. The carrier comes from the label XPS posted, else from the number's format
    /// (for shipments made by hand, with any typed carrier text stripped). Falls back to the customer's order page
    /// so the link is never blank.
    /// </summary>
    public async Task<string> ResolveAsync(Shipment shipment)
    {
        var order = await _orderService.GetOrderByIdAsync(shipment.OrderId);
        var number = TrackingNumbers.StripCarrierText(shipment.TrackingNumber);

        if (order != null && !string.IsNullOrEmpty(number))
        {
            var map = await _dataService.FindMapAsync(order.OrderGuid, number);
            var url = TrackingNumbers.Url(map?.Carrier ?? TrackingNumbers.DetectCarrier(number), number);
            if (url != null)
                return url;
        }

        if (order == null)
            return string.Empty;

        var store = await _storeService.GetStoreByIdAsync(order.StoreId);
        var storeUrl = (store?.Url ?? string.Empty).TrimEnd('/');
        return $"{storeUrl}/orderdetails/{order.Id}";
    }
}
