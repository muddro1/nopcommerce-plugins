using FluentAssertions;
using Nop.Plugin.Misc.XpsShip.Logic;
using Nop.Plugin.Misc.XpsShip.Services;
using NUnit.Framework;

namespace Nop.Plugin.Misc.XpsShip.Tests;

public class UpdateRequestTests
{
    private static List<KeyValuePair<string, string>> Form(params (string Key, string Value)[] fields) =>
        fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).ToList();

    [Test]
    public void Parses_xps_example_post()
    {
        var request = UpdateRequest.Parse(Form(
            ("orderId", "12345"), ("carrier", "ups"), ("trackingNumbers", "AAAA,BBBB,CCCC"),
            ("items[0][id]", "1234"), ("items[0][quantity]", "2"),
            ("items[1][id]", "4321"), ("items[1][quantity]", "1"),
            ("receiver[name]", "Al Borland"),
            ("quote[charges][total]", "14.82"), ("quote[details][quotedWeight]", "6.5"), ("quote[details][quotedWeightUnit]", "lb")));

        request.Errors.Should().BeEmpty();
        request.OrderId.Should().Be("12345");
        request.Carrier.Should().Be("ups");
        request.TrackingNumbers.Should().Equal("AAAA", "BBBB", "CCCC");
        request.Items.Should().Equal(new UpdateItem("1234", 2), new UpdateItem("4321", 1));
        request.LabelCost.Should().Be(14.82m);
        request.BilledWeightLb.Should().Be(6.5m);
    }

    [Test]
    public void Billed_weight_in_kilograms_is_not_taken_as_pounds()
    {
        var request = UpdateRequest.Parse(Form(("orderId", "1"), ("carrier", "ups"), ("trackingNumbers", "A"),
            ("quote[details][quotedWeight]", "3"), ("quote[details][quotedWeightUnit]", "kg")));

        request.QuotedWeight.Should().Be(3m);
        request.BilledWeightLb.Should().BeNull();
    }

    [Test]
    public void Dotted_field_names_are_accepted_too()
    {
        var request = UpdateRequest.Parse(Form(("orderId", "1"), ("carrier", "usps"), ("trackingNumbers", "9400"),
            ("items.0.id", "x"), ("items.0.quantity", "3"), ("quote.charges.total", "7.10")));

        request.Items.Should().Equal(new UpdateItem("x", 3));
        request.LabelCost.Should().Be(7.10m);
    }

    [Test]
    public void Missing_or_bad_fields_are_errors()
    {
        var request = UpdateRequest.Parse(Form(("carrier", "ontrac"), ("trackingNumbers", " "),
            ("items[0][id]", "x"), ("items[0][quantity]", "two"), ("items[1][quantity]", "1")));

        request.Errors.Should().HaveCount(5);
        request.Errors.Should().Contain(e => e.Contains("orderId"));
        request.Errors.Should().Contain(e => e.Contains("ontrac"));
        request.Errors.Should().Contain(e => e.Contains("trackingNumbers"));
        request.Errors.Should().Contain(e => e.Contains("'two'"));
        request.Errors.Should().Contain(e => e.Contains("items[1] has no id"));
    }

    [Test]
    public void A_tracking_number_too_long_to_store_is_an_error()
    {
        var request = UpdateRequest.Parse(Form(("orderId", "1"), ("carrier", "ups"), ("trackingNumbers", "1Z1," + new string('9', 101))));

        request.Errors.Should().ContainSingle().Which.Should().Contain("longer than 100");
    }

    [Test]
    public void Admin_comment_lists_carrier_packages_and_label_cost()
    {
        var request = UpdateRequest.Parse(Form(("orderId", "1"), ("carrier", "ups"), ("trackingNumbers", "1Z1,1Z2"),
            ("quote[charges][total]", "14.82")));

        XpsUpdateProcessor.AdminComment("UPS", request).Should().Be("XPS: UPS, 2 packages: 1Z1, 1Z2; label $14.82");
    }
    [Test]
    public void Keeps_the_receiver_xps_printed_on_the_label()
    {
        var request = UpdateRequest.Parse(Form(("orderId", "1"), ("carrier", "ups"), ("trackingNumbers", "A"),
            ("receiver[company]", "Rocksolid Internet"), ("receiver[name]", "Al Borland"), ("receiver[address1]", "1880 FooBar Dr"),
            ("receiver[address2]", ""), ("receiver[city]", "La Cucaracha"), ("receiver[state]", "California"),
            ("receiver[zip]", "91711"), ("receiver[country]", "US")));

        request.ShipToAddress.Should().Be("Al Borland\nRocksolid Internet\n1880 FooBar Dr\nLa Cucaracha, California 91711\nUS");
        UpdateRequest.Parse(Form(("orderId", "1"), ("carrier", "ups"), ("trackingNumbers", "A"))).ShipToAddress.Should().BeNull();
    }
}

public class ShipmentPlannerTests
{
    private static readonly Guid LineA = Guid.NewGuid();
    private static readonly Guid LineB = Guid.NewGuid();

    private static readonly ShippableLine[] Lines =
    {
        new(LineA, 1, 2, 2.91m),
        new(LineB, 2, 17, null)
    };

    [Test]
    public void Ships_the_posted_lines()
    {
        // T2
        var plan = ShipmentPlanner.Plan(new[] { new UpdateItem(LineA.ToString(), 2), new UpdateItem(LineB.ToString(), 17) }, Lines);

        plan.Errors.Should().BeEmpty();
        plan.Items.Select(i => (i.Line.OrderItemId, i.Quantity)).Should().Equal((1, 2), (2, 17));
        plan.ItemWeight.Should().Be(5.82m);
    }

    [Test]
    public void An_unknown_id_rejects_the_whole_post()
    {
        // T12
        var unknown = Guid.NewGuid().ToString();
        var plan = ShipmentPlanner.Plan(new[] { new UpdateItem(LineA.ToString(), 1), new UpdateItem(unknown, 1) }, Lines);

        plan.Items.Should().BeEmpty();
        plan.Errors.Should().ContainSingle().Which.Should().Contain(unknown);
    }

    [Test]
    public void A_non_guid_id_is_unknown()
    {
        ShipmentPlanner.Plan(new[] { new UpdateItem("1234", 1) }, Lines).Errors.Should().ContainSingle();
    }

    [Test]
    public void Quantity_over_remaining_is_capped_with_a_warning()
    {
        // T13
        var plan = ShipmentPlanner.Plan(new[] { new UpdateItem(LineA.ToString(), 5) }, Lines);

        plan.Items.Should().ContainSingle().Which.Quantity.Should().Be(2);
        plan.Warnings.Should().ContainSingle();
    }

    [Test]
    public void Zero_quantity_lines_are_skipped()
    {
        // T3: staff reduce backordered lines to 0 when shipping a partial
        var plan = ShipmentPlanner.Plan(new[] { new UpdateItem(LineA.ToString(), 2), new UpdateItem(LineB.ToString(), 0) }, Lines);

        plan.Items.Should().ContainSingle().Which.Line.OrderItemGuid.Should().Be(LineA);
    }

    [Test]
    public void No_items_posted_ships_everything_remaining()
    {
        ShipmentPlanner.Plan(Array.Empty<UpdateItem>(), Lines).Items.Sum(i => i.Quantity).Should().Be(19);
    }

    [Test]
    public void Nothing_left_to_ship_is_an_error()
    {
        var shipped = new[] { new ShippableLine(LineA, 1, 0, 1m) };

        ShipmentPlanner.Plan(Array.Empty<UpdateItem>(), shipped).Errors.Should().ContainSingle();
        ShipmentPlanner.Plan(new[] { new UpdateItem(LineA.ToString(), 1) }, shipped).Errors.Should().ContainSingle();
    }
}

public class FailedKeyLimiterTests
{
    [Test]
    public void Blocks_after_the_limit_within_the_window_then_lets_go()
    {
        var limiter = new FailedKeyLimiter(3, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(60));
        var now = new DateTime(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 3; i++)
            limiter.RecordFailure("1.2.3.4", now.AddSeconds(i)).Should().BeFalse();

        limiter.RecordFailure("1.2.3.4", now.AddSeconds(4)).Should().BeTrue();
        limiter.IsBlocked("1.2.3.4", now.AddMinutes(30)).Should().BeTrue();
        limiter.IsBlocked("5.6.7.8", now.AddMinutes(30)).Should().BeFalse();
        limiter.IsBlocked("1.2.3.4", now.AddMinutes(61)).Should().BeFalse();
    }

    [Test]
    public void Failures_outside_the_window_dont_count()
    {
        var limiter = new FailedKeyLimiter(2, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(60));
        var now = new DateTime(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc);

        limiter.RecordFailure("ip", now);
        limiter.RecordFailure("ip", now.AddMinutes(1));
        limiter.RecordFailure("ip", now.AddMinutes(20)).Should().BeFalse();
    }

}
