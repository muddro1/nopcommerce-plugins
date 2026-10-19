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
        if (order == null)
            return string.Empty;

        // A label XPS posted: its number is stored as sent, and the carrier is known. Look it up before any
        // stripping, which would cut the letters off a DHL number like JD014600006281230704.
        var stored = shipment.TrackingNumber?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(stored))
        {
            var map = await _dataService.FindMapAsync(order.OrderGuid, stored);
            var url = map == null ? null : TrackingNumbers.Url(map.Carrier, stored);
            if (url != null)
                return url;

            // Typed by hand ("UPS 1ZC602X..."): strip the carrier text and go by the number's format
            var number = TrackingNumbers.StripCarrierText(stored);
            url = TrackingNumbers.Url(TrackingNumbers.DetectCarrier(number), number);
            if (url != null)
                return url;
        }

        var store = await _storeService.GetStoreByIdAsync(order.StoreId);
        var storeUrl = (store?.Url ?? string.Empty).TrimEnd('/');
        return $"{storeUrl}/orderdetails/{order.Id}";
    }
}
