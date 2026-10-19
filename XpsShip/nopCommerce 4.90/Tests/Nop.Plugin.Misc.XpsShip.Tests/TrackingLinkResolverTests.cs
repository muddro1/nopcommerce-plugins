using FluentAssertions;
using Moq;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Stores;
using Nop.Data;
using Nop.Plugin.Misc.XpsShip.Domain;
using Nop.Plugin.Misc.XpsShip.Services;
using Nop.Services.Orders;
using Nop.Services.Stores;
using NUnit.Framework;

namespace Nop.Plugin.Misc.XpsShip.Tests;

/// <summary>%Shipment.XpsTrackingUrl% (T16, T21)</summary>
public class TrackingLinkResolverTests
{
    private readonly Order _order = new() { Id = 7, OrderGuid = Guid.NewGuid(), StoreId = 1 };
    private List<XpsShipTrackingMap> _maps;
    private TrackingLinkResolver _resolver;

    [SetUp]
    public void SetUp()
    {
        _maps = new List<XpsShipTrackingMap>();

        var orders = new Mock<IOrderService>();
        orders.Setup(o => o.GetOrderByIdAsync(7)).ReturnsAsync(_order);
        var stores = new Mock<IStoreService>();
        stores.Setup(s => s.GetStoreByIdAsync(1)).ReturnsAsync(new Store { Url = "https://shop.example/" });
        var mapRepository = new Mock<IRepository<XpsShipTrackingMap>>();
        mapRepository.Setup(r => r.Table).Returns(() => _maps.AsQueryable());

        _resolver = new TrackingLinkResolver(orders.Object, stores.Object, new XpsShipDataService(
            new Mock<IRepository<XpsShipOrderFlag>>().Object, mapRepository.Object, new Mock<IRepository<XpsShipWebhookLog>>().Object));
    }

    private Task<string> Link(string trackingNumber) => _resolver.ResolveAsync(new Shipment { OrderId = 7, TrackingNumber = trackingNumber });

    [Test]
    public async Task A_posted_dhl_label_links_to_dhl()
    {
        _maps.Add(new XpsShipTrackingMap { OrderGuid = _order.OrderGuid, PrimaryTrackingNumber = "JD014600006281230704", Carrier = "dhl" });

        (await Link("JD014600006281230704")).Should().Be("https://www.dhl.com/us-en/home/tracking.html?tracking-id=JD014600006281230704");
    }

    [Test]
    public async Task The_posted_carrier_wins_over_the_numbers_format()
    {
        // A 12-digit number looks like FedEx, but XPS said USPS
        _maps.Add(new XpsShipTrackingMap { OrderGuid = _order.OrderGuid, PrimaryTrackingNumber = "123456789012", Carrier = "usps" });

        (await Link("123456789012")).Should().StartWith("https://tools.usps.com/");
    }

    [TestCase("UPS 1ZC602X71321659005", "https://www.ups.com/track?tracknum=1ZC602X71321659005")]
    [TestCase("USPS 9334611045000004779320", "https://tools.usps.com/go/TrackConfirmAction?tLabels=9334611045000004779320")]
    [TestCase("FEDEX 876435110017", "https://www.fedex.com/fedextrack/?trknbr=876435110017")]
    [TestCase("Hand Deliver", "https://shop.example/orderdetails/7")]
    public async Task Hand_typed_numbers_go_by_their_format(string typed, string expected) =>
        (await Link(typed)).Should().Be(expected);
}
