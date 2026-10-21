using FluentAssertions;
using Moq;
using Nop.Core.Caching;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Data;
using Nop.Plugin.Misc.XpsShip.Domain;
using Nop.Plugin.Misc.XpsShip.Logic;
using Nop.Plugin.Misc.XpsShip.Services;
using Nop.Services.Catalog;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Shipping;
using NUnit.Framework;

namespace Nop.Plugin.Misc.XpsShip.Tests;

public class VoidServiceTests
{
    [Test]
    public async Task Void_deletes_only_the_unsent_email_for_that_label()
    {
        var posted = new DateTime(2026, 10, 19, 19, 10, 0, DateTimeKind.Utc);
        var order = new Order { Id = 7, OrderGuid = Guid.NewGuid(), CustomOrderNumber = "6540" };
        var map = new XpsShipTrackingMap { Id = 1, OrderGuid = order.OrderGuid, CustomOrderNumber = "6540", PrimaryTrackingNumber = "9334611045000006135315", CreatedOnUtc = posted };

        var queued = new QueuedEmail { Id = 1, Body = "Track it: https://tools.usps.com/?tLabels=9334611045000006135315", CreatedOnUtc = posted };
        var emails = new List<QueuedEmail>
        {
            queued,
            new() { Id = 2, Body = "...9334611045000006135315...", CreatedOnUtc = posted, SentOnUtc = posted.AddMinutes(1) }, // already sent
            new() { Id = 3, Body = "Another order's label 1Z999", CreatedOnUtc = posted }
        };
        var emailRepository = new Mock<IRepository<QueuedEmail>>();
        emailRepository.Setup(r => r.Table).Returns(() => emails.AsQueryable());
        emailRepository.Setup(r => r.DeleteAsync(It.IsAny<QueuedEmail>(), It.IsAny<bool>()))
            .Callback((QueuedEmail e, bool _) => emails.Remove(e)).Returns(Task.CompletedTask);

        var mapRepository = new Mock<IRepository<XpsShipTrackingMap>>();
        mapRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<Func<IStaticCacheManager, CacheKey>>(), It.IsAny<bool>())).ReturnsAsync(map);
        var dataService = new XpsShipDataService(Mock.Of<IRepository<XpsShipOrderFlag>>(), mapRepository.Object, Mock.Of<IRepository<XpsShipWebhookLog>>());

        var orders = new Mock<IOrderService>();
        orders.Setup(o => o.GetOrderByGuidAsync(order.OrderGuid)).ReturnsAsync(order);
        var shipments = new Mock<IShipmentService>();
        shipments.Setup(s => s.GetShipmentsByOrderIdAsync(7, It.IsAny<bool?>(), It.IsAny<bool?>(), It.IsAny<int>())).ReturnsAsync(new List<Shipment>());

        var service = new XpsVoidService(Mock.Of<ILocalizationService>(), Mock.Of<IOrderProcessingService>(), orders.Object,
            Mock.Of<IProductService>(), shipments.Object, emailRepository.Object, dataService);

        (await service.VoidAsync(1, "staff")).Should().BeNull();

        emails.Select(e => e.Id).Should().BeEquivalentTo(new[] { 2, 3 });
    }

    [Test]
    public async Task Void_that_reopens_a_complete_order_deletes_its_unsent_completed_emails()
    {
        var posted = new DateTime(2026, 10, 21, 14, 0, 0, DateTimeKind.Utc);
        var order = new Order { Id = 7, OrderGuid = Guid.NewGuid(), CustomOrderNumber = "8108",
            OrderStatusId = OrderRules.OrderComplete, ShippingStatusId = OrderRules.Shipped };
        var map = new XpsShipTrackingMap { Id = 1, OrderGuid = order.OrderGuid, CustomOrderNumber = "8108", PrimaryTrackingNumber = "1ZTEST", CreatedOnUtc = posted };

        var emails = new List<QueuedEmail>
        {
            new() { Id = 41, Body = "Your order is complete", CreatedOnUtc = posted },
            new() { Id = 42, Body = "Order completed (store owner)", CreatedOnUtc = posted },
            new() { Id = 43, Body = "Your order is complete", CreatedOnUtc = posted, SentOnUtc = posted }, // already sent
            new() { Id = 44, Body = "Someone else's order", CreatedOnUtc = posted }
        };
        var emailRepository = new Mock<IRepository<QueuedEmail>>();
        emailRepository.Setup(r => r.Table).Returns(() => emails.AsQueryable());
        emailRepository.Setup(r => r.DeleteAsync(It.IsAny<QueuedEmail>(), It.IsAny<bool>()))
            .Callback((QueuedEmail e, bool _) => emails.Remove(e)).Returns(Task.CompletedTask);

        var mapRepository = new Mock<IRepository<XpsShipTrackingMap>>();
        mapRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<Func<IStaticCacheManager, CacheKey>>(), It.IsAny<bool>())).ReturnsAsync(map);
        var dataService = new XpsShipDataService(Mock.Of<IRepository<XpsShipOrderFlag>>(), mapRepository.Object, Mock.Of<IRepository<XpsShipWebhookLog>>());

        var notes = new List<OrderNote>
        {
            new() { Note = "\"Order completed\" email (to customer) has been queued. Queued email identifiers: 41, 43." },
            new() { Note = "\"Order completed\" email (to store owner) has been queued. Queued email identifiers: 42." }
        };
        var orders = new Mock<IOrderService>();
        orders.Setup(o => o.GetOrderByGuidAsync(order.OrderGuid)).ReturnsAsync(order);
        orders.Setup(o => o.GetOrderNotesByOrderIdAsync(7, It.IsAny<bool?>())).ReturnsAsync(notes);
        orders.Setup(o => o.InsertOrderNoteAsync(It.IsAny<OrderNote>())).Callback((OrderNote n) => notes.Add(n)).Returns(Task.CompletedTask);
        var shipments = new Mock<IShipmentService>();
        shipments.Setup(s => s.GetShipmentsByOrderIdAsync(7, It.IsAny<bool?>(), It.IsAny<bool?>(), It.IsAny<int>())).ReturnsAsync(new List<Shipment>());

        var service = new XpsVoidService(Mock.Of<ILocalizationService>(), Mock.Of<IOrderProcessingService>(), orders.Object,
            Mock.Of<IProductService>(), shipments.Object, emailRepository.Object, dataService);

        (await service.VoidAsync(1, "staff")).Should().BeNull();

        order.OrderStatusId.Should().Be(OrderRules.OrderProcessing);
        emails.Select(e => e.Id).Should().BeEquivalentTo(new[] { 43, 44 });
        notes.Last().Note.Should().EndWith("Deleted 2 unsent order completed email(s).");
    }
}
