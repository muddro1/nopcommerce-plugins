using FluentAssertions;
using Nop.Plugin.Misc.CustomerProfile.Logic;
using NUnit.Framework;

namespace Nop.Plugin.Misc.CustomerProfile.Tests;

public class OverduePlannerTests
{
    private static readonly DateTime Today = new(2026, 10, 12);

    private static Net30Snapshot Order(int id, DateTime due, bool paid = false, bool cancelled = false, DateTime? noticeSent = null) =>
        new(id, 1, due, paid, cancelled, noticeSent);

    [Test]
    public void Unpaid_order_due_yesterday_is_reminded()
    {
        OverduePlanner.OrdersToRemind(new[] { Order(7, Today.AddDays(-1)) }, Today, true).Should().Equal(7);
    }

    [Test]
    public void Order_due_today_is_not_overdue_yet()
    {
        OverduePlanner.OrdersToRemind(new[] { Order(7, Today) }, Today, true).Should().BeEmpty();
    }

    [Test]
    public void Paid_cancelled_or_already_reminded_orders_are_skipped()
    {
        OverduePlanner.OrdersToRemind(new[]
        {
            Order(1, Today.AddDays(-5), paid: true),
            Order(2, Today.AddDays(-5), cancelled: true),
            Order(3, Today.AddDays(-5), noticeSent: Today.AddDays(-1))
        }, Today, true).Should().BeEmpty();
    }

    [Test]
    public void Disabled_sends_nothing()
    {
        OverduePlanner.OrdersToRemind(new[] { Order(7, Today.AddDays(-10)) }, Today, false).Should().BeEmpty();
    }
}
