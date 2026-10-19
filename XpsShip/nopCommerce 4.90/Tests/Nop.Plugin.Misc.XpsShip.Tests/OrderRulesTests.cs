using FluentAssertions;
using Nop.Plugin.Misc.XpsShip.Logic;
using NUnit.Framework;

namespace Nop.Plugin.Misc.XpsShip.Tests;

public class OrderRulesTests
{
    private static readonly RuleSettings Settings = new(new[] { "Open Payment" }, true, "-EXCH");

    private static LineSnapshot Line(string sku, int remaining = 2, StockState stock = StockState.InStock) =>
        new(Guid.NewGuid(), sku, remaining, stock);

    private static OrderSnapshot Order(int paymentStatus = OrderRules.Paid, string paymentMethod = "Payments.Manual",
        string instructions = "", bool held = false, bool holdCleared = false, bool released = false,
        int orderStatus = OrderRules.OrderProcessing, int shippingStatus = OrderRules.NotYetShipped,
        bool pickup = false, bool hasAddress = true, params LineSnapshot[] lines) =>
        new(orderStatus, shippingStatus, paymentStatus, paymentMethod, pickup, hasAddress, instructions,
            lines.Length == 0 ? new[] { Line("FMSA-MT-KAST") } : lines, held ? "staff" : null, held, holdCleared, released);

    [Test]
    public void A_paid_unshipped_order_is_included() =>
        OrderRules.Evaluate(Order(), Settings).Include.Should().BeTrue();

    [Test]
    public void Open_payment_ships_before_it_is_paid()
    {
        // T3: #8108 Arizona DPS sits at Pending/Pending
        OrderRules.Evaluate(Order(paymentStatus: 10, paymentMethod: "Payments.OpenPayment"), Settings).Include.Should().BeTrue();
    }

    [Test]
    public void An_unpaid_card_or_paypal_order_is_left_out()
    {
        // T14
        var decision = OrderRules.Evaluate(Order(paymentStatus: 10, paymentMethod: "Payments.PayPalStandard"), Settings);
        decision.Include.Should().BeFalse();
        decision.Reason.Should().Be("unpaid (Payments.PayPalStandard)");
    }

    [TestCase("Open Payment", "Payments.OpenPayment", true)]
    [TestCase("Payments.OpenPayment", "payments.openpayment", true)]
    [TestCase("OpenPayment", "Payments.OpenPayment", true)]
    [TestCase("Open Payment", "Payments.CheckMoneyOrder", false)]
    public void Ship_before_payment_matching(string setting, string method, bool expected) =>
        OrderRules.ShipsBeforePayment(method, new[] { setting }).Should().Be(expected);

    [TestCase(30)]
    [TestCase(40)]
    public void Complete_and_cancelled_orders_are_left_out(int orderStatus) =>
        OrderRules.Evaluate(Order(orderStatus: orderStatus), Settings).Include.Should().BeFalse();

    [Test]
    public void A_partially_shipped_order_stays_in_while_lines_remain()
    {
        OrderRules.Evaluate(Order(shippingStatus: OrderRules.PartiallyShipped, lines: new[] { Line("A", 0), Line("B", 1) }), Settings)
            .Include.Should().BeTrue();
        OrderRules.Evaluate(Order(shippingStatus: OrderRules.PartiallyShipped, lines: new[] { Line("A", 0) }), Settings)
            .Reason.Should().Be("nothing left to ship");
    }

    [Test]
    public void Pickups_and_orders_without_a_shipping_address_stay_manual()
    {
        OrderRules.Evaluate(Order(pickup: true), Settings).Reason.Should().Be("pickup in store");
        OrderRules.Evaluate(Order(hasAddress: false), Settings).Reason.Should().Be("no shipping address");
    }

    [Test]
    public void A_held_order_is_left_out() =>
        OrderRules.Evaluate(Order(held: true), Settings).Reason.Should().Be("on hold: staff");

    [Test]
    public void Do_not_ship_holds_the_order_the_first_time_only()
    {
        // T7: #8090
        const string text = "Special Instruction: This product has already been fulfilled! Do not ship";

        var first = OrderRules.Evaluate(Order(instructions: text), Settings);
        first.Include.Should().BeFalse();
        first.AutoHold.Should().BeTrue();

        OrderRules.Evaluate(Order(instructions: text, holdCleared: true), Settings).Include.Should().BeTrue();
    }

    [Test]
    public void Exchange_orders_wait_for_release()
    {
        // T5: #8103, 2 x FMSA-GHMT-KA3R-EXCH
        var exchange = Line("FMSA-GHMT-KA3R-EXCH");

        OrderRules.Evaluate(Order(lines: exchange), Settings).Reason.Should().Be("exchange order, not released");
        OrderRules.Evaluate(Order(released: true, lines: exchange), Settings).Include.Should().BeTrue();
    }

    [Test]
    public void Fully_backordered_orders_are_hidden_when_the_setting_is_on()
    {
        // T4: #7611, FMSA-GSP4-MOTORKIT x10 at stock -10
        var order = Order(lines: Line("FMSA-GSP4-MOTORKIT", 10, StockState.Backordered));

        OrderRules.Evaluate(order, Settings).Reason.Should().Be("backordered");
        OrderRules.Evaluate(order, Settings with { HideFullyBackordered = false }).Include.Should().BeTrue();
    }

    [Test]
    public void A_partly_backordered_order_is_sent()
    {
        // T3: #8108 has stock for some lines
        OrderRules.Evaluate(Order(lines: new[] { Line("FMSA-MT-V2"), Line("FMSA-HBKS", 2, StockState.Backordered) }), Settings)
            .Include.Should().BeTrue();
    }

    [TestCase(false, -10, StockState.NotTracked)]
    [TestCase(true, 5, StockState.InStock)]
    [TestCase(true, 0, StockState.Backordered)]
    [TestCase(true, -10, StockState.Backordered)]
    [TestCase(true, 9998, StockState.InStock)]
    [TestCase(true, 10000, StockState.InStock)]
    public void Stock_classification(bool tracked, int stock, StockState expected) =>
        OrderRules.Classify(tracked, stock).Should().Be(expected);

    [TestCase(OrderRules.Shipped, false, OrderRules.NotYetShipped)]
    [TestCase(OrderRules.Shipped, true, OrderRules.PartiallyShipped)]
    [TestCase(OrderRules.PartiallyShipped, false, OrderRules.NotYetShipped)]
    [TestCase(OrderRules.NotYetShipped, false, OrderRules.NotYetShipped)]
    public void Voiding_puts_the_shipping_status_back(int current, bool otherShipped, int expected)
    {
        // T18 and the gap in nopCommerce's own Delete shipment
        OrderRules.ShippingStatusAfterVoid(current, otherShipped).Should().Be(expected);
    }
}
