using FluentAssertions;
using Moq;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Core.Events;
using Nop.Data;
using Nop.Plugin.Misc.XpsShip.Domain;
using Nop.Plugin.Misc.XpsShip.Logic;
using Nop.Plugin.Misc.XpsShip.Services;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Logging;
using Nop.Services.Media;
using Nop.Services.Orders;
using Nop.Services.Shipping;
using NUnit.Framework;

namespace Nop.Plugin.Misc.XpsShip.Tests;

/// <summary>The Update Order flow against mocked nopCommerce services: #8109, FMSA-MT-KAST x2</summary>
public class UpdateProcessorTests
{
    private static readonly Guid LineGuid = Guid.NewGuid();

    private Order _order;
    private OrderItem _item;
    private List<XpsShipTrackingMap> _maps;
    private List<OrderNote> _notes;
    private List<Shipment> _shipments;
    private List<ShipmentItem> _shipmentItems;
    private Mock<IOrderProcessingService> _processing;
    private XpsUpdateProcessor _processor;

    [SetUp]
    public void SetUp()
    {
        _order = new Order { Id = 7, OrderGuid = Guid.NewGuid(), CustomOrderNumber = "8109" };
        _item = new OrderItem { Id = 70, OrderId = 7, OrderItemGuid = LineGuid, ProductId = 5, Quantity = 2, ItemWeight = 1.5m };
        var product = new Product { Id = 5, Name = "Mount", Sku = "FMSA-MT-KAST", ManageInventoryMethod = ManageInventoryMethod.DontManageStock };

        _maps = new List<XpsShipTrackingMap>();
        _notes = new List<OrderNote>();
        _shipments = new List<Shipment>();
        _shipmentItems = new List<ShipmentItem>();

        var orders = new Mock<IOrderService>();
        orders.Setup(o => o.GetOrderByGuidAsync(_order.OrderGuid)).ReturnsAsync(_order);
        orders.Setup(o => o.GetOrderItemsAsync(7, It.IsAny<bool?>(), true, It.IsAny<int>())).ReturnsAsync(new List<OrderItem> { _item });
        orders.Setup(o => o.GetTotalNumberOfItemsCanBeAddedToShipmentAsync(_item)).ReturnsAsync(() => 2 - _shipmentItems.Sum(i => i.Quantity));
        orders.Setup(o => o.InsertOrderNoteAsync(It.IsAny<OrderNote>())).Callback((OrderNote n) => _notes.Add(n)).Returns(Task.CompletedTask);

        var products = new Mock<IProductService>();
        products.Setup(p => p.GetProductByIdAsync(5)).ReturnsAsync(product);
        products.Setup(p => p.FormatSkuAsync(product, It.IsAny<string>())).ReturnsAsync("FMSA-MT-KAST");

        var shipments = new Mock<IShipmentService>();
        shipments.Setup(s => s.GetShipmentsByOrderIdAsync(7, It.IsAny<bool?>(), It.IsAny<bool?>(), It.IsAny<int>())).ReturnsAsync(() => _shipments);
        shipments.Setup(s => s.InsertShipmentAsync(It.IsAny<Shipment>()))
            .Callback((Shipment s) => { s.Id = 100 + _shipments.Count; _shipments.Add(s); }).Returns(Task.CompletedTask);
        shipments.Setup(s => s.InsertShipmentItemAsync(It.IsAny<ShipmentItem>()))
            .Callback((ShipmentItem i) => _shipmentItems.Add(i)).Returns(Task.CompletedTask);

        var measures = new Mock<IMeasureService>();
        measures.Setup(m => m.GetMeasureWeightByIdAsync(It.IsAny<int>())).ReturnsAsync(new MeasureWeight { SystemKeyword = "lb" });

        var mapRepository = new Mock<IRepository<XpsShipTrackingMap>>();
        mapRepository.Setup(r => r.Table).Returns(() => _maps.AsQueryable());
        mapRepository.Setup(r => r.InsertAsync(It.IsAny<XpsShipTrackingMap>(), It.IsAny<bool>()))
            .Callback((XpsShipTrackingMap m, bool _) => _maps.Add(m)).Returns(Task.CompletedTask);

        var dataService = new XpsShipDataService(new Mock<IRepository<XpsShipOrderFlag>>().Object, mapRepository.Object,
            new Mock<IRepository<XpsShipWebhookLog>>().Object);

        var settings = new XpsShipSettings { NotifyCustomerOnShip = true };
        var builder = new XpsOrderBuilder(Mock.Of<IAddressService>(), Mock.Of<ICountryService>(), Mock.Of<ICustomerService>(),
            Mock.Of<ILogger>(), measures.Object, orders.Object, Mock.Of<IPictureService>(), Mock.Of<IProductAttributeParser>(),
            products.Object, Mock.Of<IStateProvinceService>(), new MeasureSettings(), dataService, settings);

        _processing = new Mock<IOrderProcessingService>();
        _processor = new XpsUpdateProcessor(Mock.Of<IEventPublisher>(), Mock.Of<ILogger>(), _processing.Object, orders.Object,
            products.Object, shipments.Object, builder, dataService, settings);
    }

    private UpdateRequest Post(string trackingNumbers, params (string Id, int Quantity)[] items)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("orderId", _order.OrderGuid.ToString()),
            new("carrier", "ups"),
            new("trackingNumbers", trackingNumbers)
        };
        for (var i = 0; i < items.Length; i++)
        {
            form.Add(new($"items[{i}][id]", items[i].Id));
            form.Add(new($"items[{i}][quantity]", items[i].Quantity.ToString()));
        }

        return UpdateRequest.Parse(form);
    }

    [Test]
    public async Task Ships_the_order_once_and_records_the_label()
    {
        // T1
        var result = await _processor.ProcessAsync(Post("1zc602x70219206948", (LineGuid.ToString(), 2)), new XpsShipWebhookLog());

        result.HttpStatus.Should().Be(200);
        _shipments.Should().ContainSingle().Which.TrackingNumber.Should().Be("1ZC602X70219206948");
        _shipments[0].TotalWeight.Should().Be(3m);
        _shipmentItems.Should().ContainSingle().Which.Quantity.Should().Be(2);
        _processing.Verify(p => p.ShipAsync(_shipments[0], true), Times.Once);
        _maps.Should().ContainSingle().Which.Carrier.Should().Be("ups");
        _notes.Should().NotContain(n => n.DisplayToCustomer);
    }

    [Test]
    public async Task A_repeated_post_ships_nothing_more()
    {
        // T9
        await _processor.ProcessAsync(Post("1Z1", (LineGuid.ToString(), 2)), new XpsShipWebhookLog());
        var second = await _processor.ProcessAsync(Post("1Z1", (LineGuid.ToString(), 2)), new XpsShipWebhookLog());

        second.HttpStatus.Should().Be(200);
        second.Summary.Should().StartWith("duplicate");
        _shipments.Should().ContainSingle();
        _processing.Verify(p => p.ShipAsync(It.IsAny<Shipment>(), It.IsAny<bool>()), Times.Once);
    }

    [Test]
    public async Task An_unknown_line_creates_nothing()
    {
        // T12
        var result = await _processor.ProcessAsync(Post("1Z2", (Guid.NewGuid().ToString(), 1)), new XpsShipWebhookLog());

        result.HttpStatus.Should().Be(422);
        _shipments.Should().BeEmpty();
        _maps.Should().BeEmpty();
    }

    [Test]
    public async Task Several_packages_add_one_customer_visible_note()
    {
        // T10 and T22
        var log = new XpsShipWebhookLog();
        var result = await _processor.ProcessAsync(Post("1ZC602X70219206948, 1ZC602X70220017391,1ZC602X70224542806", (LineGuid.ToString(), 2)), log);

        result.HttpStatus.Should().Be(200);
        _shipments.Single().TrackingNumber.Should().Be("1ZC602X70219206948");
        _notes.Where(n => n.DisplayToCustomer).Should().ContainSingle()
            .Which.Note.Should().Be("Shipped via UPS in 3 packages: 1ZC602X70219206948, 1ZC602X70220017391, 1ZC602X70224542806");
        _maps.Single().PackageCount.Should().Be(3);
        log.CustomOrderNumber.Should().Be("8109");
    }

    [Test]
    public async Task An_unknown_order_is_404()
    {
        var request = UpdateRequest.Parse(new List<KeyValuePair<string, string>>
        {
            new("orderId", Guid.NewGuid().ToString()), new("carrier", "ups"), new("trackingNumbers", "1Z")
        });

        (await _processor.ProcessAsync(request, new XpsShipWebhookLog())).HttpStatus.Should().Be(404);
    }
}
