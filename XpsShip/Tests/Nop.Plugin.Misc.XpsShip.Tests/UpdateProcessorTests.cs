using FluentAssertions;
using Moq;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Localization;
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
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Media;
using Nop.Services.Messages;
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
    private Mock<IWorkflowMessageService> _workflow;
    private Mock<IEventPublisher> _events;
    private int _shipFailuresLeft;
    private bool _mapExistedAtShip;
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
        orders.Setup(o => o.GetOrderByIdAsync(7)).ReturnsAsync(_order);
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
        // Deleting a shipment cascades to its items, as the database does
        shipments.Setup(s => s.DeleteShipmentAsync(It.IsAny<Shipment>()))
            .Callback((Shipment s) => { _shipments.Remove(s); _shipmentItems.RemoveAll(i => i.ShipmentId == s.Id); })
            .Returns(Task.CompletedTask);

        var measures = new Mock<IMeasureService>();
        measures.Setup(m => m.GetMeasureWeightByIdAsync(It.IsAny<int>())).ReturnsAsync(new MeasureWeight { SystemKeyword = "lb" });

        var mapRepository = new Mock<IRepository<XpsShipTrackingMap>>();
        mapRepository.Setup(r => r.Table).Returns(() => _maps.AsQueryable());
        mapRepository.Setup(r => r.InsertAsync(It.IsAny<XpsShipTrackingMap>(), It.IsAny<bool>()))
            .Callback((XpsShipTrackingMap m, bool _) => { m.Id = _maps.Count + 1; _maps.Add(m); }).Returns(Task.CompletedTask);
        mapRepository.Setup(r => r.DeleteAsync(It.IsAny<XpsShipTrackingMap>(), It.IsAny<bool>()))
            .Callback((XpsShipTrackingMap m, bool _) => _maps.Remove(m)).Returns(Task.CompletedTask);

        var dataService = new XpsShipDataService(new Mock<IRepository<XpsShipOrderFlag>>().Object, mapRepository.Object,
            new Mock<IRepository<XpsShipWebhookLog>>().Object);

        var settings = new XpsShipSettings { NotifyCustomerOnShip = true };
        var builder = new XpsOrderBuilder(Mock.Of<IAddressService>(), Mock.Of<ICountryService>(), Mock.Of<ICustomerService>(),
            Mock.Of<ILogger>(), measures.Object, Mock.Of<INopDataProvider>(), orders.Object, Mock.Of<IRepository<Order>>(), Mock.Of<IPictureService>(), Mock.Of<IProductAttributeParser>(),
            products.Object, Mock.Of<IStateProvinceService>(), new MeasureSettings(), dataService, settings);

        _shipFailuresLeft = 0;
        _mapExistedAtShip = false;
        _processing = new Mock<IOrderProcessingService>();
        // Like the real ShipAsync: refuses before marking anything when it fails, otherwise sets the shipped date
        _processing.Setup(p => p.ShipAsync(It.IsAny<Shipment>(), It.IsAny<bool>()))
            .Returns((Shipment s, bool _) =>
            {
                _mapExistedAtShip = _maps.Any(m => m.PrimaryTrackingNumber == s.TrackingNumber);
                if (_shipFailuresLeft-- > 0)
                    throw new InvalidOperationException("database timeout");

                s.ShippedDateUtc = DateTime.UtcNow;
                _order.ShippingStatusId = _shipmentItems.Sum(i => i.Quantity) >= 2 ? OrderRules.Shipped : OrderRules.PartiallyShipped;
                return Task.CompletedTask;
            });
        _workflow = new Mock<IWorkflowMessageService>();
        _workflow.Setup(w => w.SendOrderCompletedCustomerNotificationAsync(_order, It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<int> { 41 });
        _events = new Mock<IEventPublisher>();
        _processor = new XpsUpdateProcessor(_events.Object, Mock.Of<ILocalizationService>(), Mock.Of<ILogger>(), _processing.Object, orders.Object,
            products.Object, Mock.Of<IPdfService>(), shipments.Object, _workflow.Object,
            new OrderSettings(), builder, dataService, settings);
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
    public async Task The_label_is_recorded_before_the_shipped_email_is_built()
    {
        // The email's %Shipment.XpsTrackingUrl% reads the carrier from the label row while ShipAsync runs
        await _processor.ProcessAsync(Post("JD014600006281230704", (LineGuid.ToString(), 2)), new XpsShipWebhookLog());

        _mapExistedAtShip.Should().BeTrue();
    }

    [Test]
    public async Task A_post_that_fails_before_shipping_is_undone_and_the_retry_ships()
    {
        _shipFailuresLeft = 1;

        var first = async () => await _processor.ProcessAsync(Post("1Z1", (LineGuid.ToString(), 2)), new XpsShipWebhookLog());
        await first.Should().ThrowAsync<InvalidOperationException>();
        _shipments.Should().BeEmpty();
        _shipmentItems.Should().BeEmpty();
        _maps.Should().BeEmpty();

        var retry = await _processor.ProcessAsync(Post("1Z1", (LineGuid.ToString(), 2)), new XpsShipWebhookLog());

        retry.HttpStatus.Should().Be(200);
        retry.Summary.Should().StartWith("shipped");
        _shipments.Should().ContainSingle().Which.ShippedDateUtc.Should().NotBeNull();
        _shipmentItems.Should().ContainSingle().Which.Quantity.Should().Be(2);
        _maps.Should().ContainSingle();
    }

    [Test]
    public async Task A_retry_finds_an_unshipped_leftover_and_starts_again()
    {
        // As if the undo itself failed: an XPS shipment with no items and no label row
        _shipments.Add(new Shipment { Id = 50, OrderId = 7, TrackingNumber = "1Z1", AdminComment = "XPS: UPS, 1 package: 1Z1" });

        var result = await _processor.ProcessAsync(Post("1Z1", (LineGuid.ToString(), 2)), new XpsShipWebhookLog());

        result.Summary.Should().StartWith("shipped");
        _shipments.Should().ContainSingle().Which.Id.Should().NotBe(50);
        _shipmentItems.Should().ContainSingle();
    }

    [Test]
    public async Task A_shipped_label_with_no_row_gets_its_row_on_the_retry()
    {
        _shipments.Add(new Shipment { Id = 50, OrderId = 7, TrackingNumber = "1Z1", ShippedDateUtc = DateTime.UtcNow });

        var result = await _processor.ProcessAsync(Post("1Z1", (LineGuid.ToString(), 2)), new XpsShipWebhookLog());

        result.Summary.Should().StartWith("duplicate");
        _maps.Should().ContainSingle().Which.Carrier.Should().Be("ups");
        _processing.Verify(p => p.ShipAsync(It.IsAny<Shipment>(), It.IsAny<bool>()), Times.Never);
    }

    [Test]
    public async Task An_unshipped_shipment_made_by_hand_is_left_alone()
    {
        _shipments.Add(new Shipment { Id = 50, OrderId = 7, TrackingNumber = "1Z1", AdminComment = "packed by Sam" });

        var result = await _processor.ProcessAsync(Post("1Z1", (LineGuid.ToString(), 2)), new XpsShipWebhookLog());

        result.HttpStatus.Should().Be(422);
        _shipments.Should().ContainSingle().Which.Id.Should().Be(50);
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

    [Test]
    public async Task An_unpaid_order_shipped_in_full_is_completed_and_gets_the_completed_email_once()
    {
        // Open Payment PO: nopCommerce's own rule never completes it
        _order.OrderStatusId = OrderRules.OrderProcessing;

        await _processor.ProcessAsync(Post("1zc602x70219206948", (LineGuid.ToString(), 2)), new XpsShipWebhookLog());
        await _processor.ProcessAsync(Post("1zc602x70219206948", (LineGuid.ToString(), 2)), new XpsShipWebhookLog());

        _order.OrderStatusId.Should().Be(OrderRules.OrderComplete);
        _workflow.Verify(w => w.SendOrderCompletedCustomerNotificationAsync(_order, It.IsAny<int>(), null, null), Times.Once);
        _notes.Should().Contain(n => n.Note == "\"Order completed\" email (to customer) has been queued. Queued email identifiers: 41.");
    }

    [TestCase(OrderRules.OrderComplete, 2)] // paid: nopCommerce completed it and sent its own email
    [TestCase(OrderRules.OrderProcessing, 1)] // partial shipment
    public async Task No_completed_email_unless_the_plugin_completes_the_order(int orderStatus, int quantity)
    {
        _order.OrderStatusId = orderStatus;

        await _processor.ProcessAsync(Post("1zc602x70219206948", (LineGuid.ToString(), quantity)), new XpsShipWebhookLog());

        _order.OrderStatusId.Should().Be(orderStatus);
        _workflow.Verify(w => w.SendOrderCompletedCustomerNotificationAsync(It.IsAny<Order>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task A_failed_completed_email_still_answers_200()
    {
        _order.OrderStatusId = OrderRules.OrderProcessing;
        _workflow.Setup(w => w.SendOrderCompletedCustomerNotificationAsync(_order, It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        var result = await _processor.ProcessAsync(Post("1zc602x70219206948", (LineGuid.ToString(), 2)), new XpsShipWebhookLog());

        result.HttpStatus.Should().Be(200);
        _order.OrderStatusId.Should().Be(OrderRules.OrderComplete);
    }
}
