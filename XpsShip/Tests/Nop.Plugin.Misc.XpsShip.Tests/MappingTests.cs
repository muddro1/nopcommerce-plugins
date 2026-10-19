using FluentAssertions;
using Nop.Plugin.Misc.XpsShip.Logic;
using Nop.Plugin.Misc.XpsShip.Models;
using NUnit.Framework;

namespace Nop.Plugin.Misc.XpsShip.Tests;

public class MappingTests
{
    // nopCommerce's CheckoutAttributeDescription for #8102's special instruction
    private const string Instruction8102 = "Special Instruction: PO# 36591, 1 unit for the City of Glendale<br />";

    [Test]
    public void Instructions_become_plain_text_with_line_breaks()
    {
        Instructions.ToPlainText("Special Instruction: Leave at&nbsp;dock<br />Gift wrap: <b>No</b>", "\n")
            .Should().Be("Special Instruction: Leave at dock\nGift wrap: No");
    }

    [Test]
    public void The_po_is_read_from_the_special_instruction()
    {
        var text = Instructions.ToPlainText(Instruction8102, "\n");
        Instructions.FindPo(text).Should().Be("36591");
        Instructions.ShipperReference("8102", Instructions.FindPo(text)).Should().Be("8102 PO 36591");
    }

    [TestCase("po 4500-A1", "4500-A1")]
    [TestCase("PO#77", "77")]
    [TestCase("Ship to PO Box 12", null)]
    [TestCase("Deliver to the depot", null)]
    public void Po_variants(string text, string expected) => Instructions.FindPo(text).Should().Be(expected);

    [Test]
    public void Shipper_reference_is_cut_to_35_characters()
    {
        Instructions.ShipperReference("8102", "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789").Should().HaveLength(35);
        Instructions.ShipperReference("8102", null).Should().Be("8102");
    }

    [Test]
    public void Customer_notes_start_with_the_customers_shipping_choice()
    {
        // T20
        Instructions.CustomerNotes("FedEx Standard Overnight", "Special Instruction: PO# 36591")
            .Should().Be("Customer selected: FedEx Standard Overnight\nSpecial Instruction: PO# 36591");
        Instructions.CustomerNotes(null, string.Empty).Should().BeEmpty();
    }

    [TestCase("This product has already been fulfilled! Do not ship", true)]
    [TestCase("DO  NOT SHIP until paid", true)]
    [TestCase("Ship ASAP", false)]
    public void Do_not_ship_is_spotted(string text, bool expected) => Instructions.SaysDoNotShip(text).Should().Be(expected);

    [Test]
    public void Line_title_carries_the_attributes_and_the_linked_mount_sku()
    {
        // T6
        var title = Instructions.LineTitle("ProLaser 4 Lidar Holster",
            "Platform: R 1200/1250 RT-P (2015-2024)<br />LH Mount K52: Right-Handed [FMSA-GHMT-K52R] [+$59.00]", false);

        title.Should().Be("ProLaser 4 Lidar Holster | Platform: R 1200/1250 RT-P (2015-2024); LH Mount K52: Right-Handed [FMSA-GHMT-K52R] [+$59.00]");
    }

    [Test]
    public void Backordered_lines_are_prefixed()
    {
        Instructions.LineTitle("Handlebar kit", null, true).Should().Be("BACKORDERED: Handlebar kit");
    }

    [Test]
    public void Bin_label_lists_the_main_bin_then_each_linked_bin()
    {
        Instructions.BinLabel(" A3-2 ", new[] { ("Holster mount", (string?)"B7"), ("No-bin mount", (string?)" "), ("Strap", (string?)null) })
            .Should().Be("[BIN A3-2; Holster mount: B7]");
        Instructions.BinLabel(null, new[] { ("Holster mount", (string?)"B7") }).Should().Be("[BIN Holster mount: B7]");
        Instructions.BinLabel("", Array.Empty<(string, string?)>()).Should().BeNull();
    }

    [Test]
    public void Bin_goes_after_the_backordered_prefix_and_before_the_name()
    {
        Instructions.LineTitle("Handlebar kit", null, true, "[BIN A3]").Should().Be("BACKORDERED: [BIN A3] Handlebar kit");
        Instructions.LineTitle("Handlebar kit", null, false, null).Should().Be("Handlebar kit");
    }

    [Test]
    public void A_voided_order_goes_out_under_a_new_id_that_still_finds_the_order()
    {
        var guid = Guid.Parse("930b7cac-84aa-4cb9-8823-b98695c39f07");

        XpsFormat.OrderId(guid, 0).Should().Be("930b7cac-84aa-4cb9-8823-b98695c39f07");
        XpsFormat.OrderId(guid, 2).Should().Be("930b7cac-84aa-4cb9-8823-b98695c39f07-r2");

        foreach (var id in new[] { "930b7cac-84aa-4cb9-8823-b98695c39f07", "930b7cac-84aa-4cb9-8823-b98695c39f07-r1", " 930B7CAC-84AA-4CB9-8823-B98695C39F07-R12 " })
        {
            XpsFormat.TryParseOrderId(id, out var parsed).Should().BeTrue(id);
            parsed.Should().Be(guid);
        }

        XpsFormat.TryParseOrderId("6540", out _).Should().BeFalse();
        XpsFormat.TryParseOrderId(null, out _).Should().BeFalse();
    }

    [Test]
    public void Decimals_go_out_as_invariant_strings()
    {
        XpsFormat.Money(8.2m).Should().Be("8.20");
        XpsFormat.Weight(2.91m).Should().Be("2.9100");
        XpsFormat.UnixSeconds(new DateTime(2015, 10, 6, 16, 24, 10, DateTimeKind.Utc)).Should().Be(1444148650);
    }

    [TestCase("+1 (801) 555-1234", "8015551234")]
    [TestCase("801.555.1234", "8015551234")]
    [TestCase("", null)]
    [TestCase("n/a", null)]
    public void Phones_keep_the_last_ten_digits(string phone, string expected) => XpsFormat.Phone(phone).Should().Be(expected);

    [Test]
    public void Service_keys_come_from_the_map_with_a_default()
    {
        var map = ServiceKeys.Parse(XpsShipSettings.DefaultServiceKeyMap);

        ServiceKeys.Resolve("FedEx Standard Overnight", map, "FREE").Should().Be("EXP-UPS-NEXTDAY");
        ServiceKeys.Resolve("fedex 2day", map, "FREE").Should().Be("EXP-UPS-2DAY");
        ServiceKeys.Resolve("FedEx International Economy", map, "FREE").Should().Be("INTL-UPS");
        ServiceKeys.Resolve("Free", map, "X").Should().Be("FREE");
        ServiceKeys.Resolve("Ground (new)", map, "FREE").Should().Be("FREE");
        ServiceKeys.Resolve(null, map, "FREE").Should().Be("FREE");
    }

    [Test]
    public void Order_json_uses_xps_field_names()
    {
        var json = XpsJson.Serialize(new XpsOrderListResponse
        {
            Orders = { new XpsOrder { OrderId = "g", ShippingTotal = "8.24", Items = { new XpsItem { ProductId = "line", Quantity = 2 } } } }
        });

        json.Should().Contain("\"orders\":[");
        json.Should().Contain("\"shipping_total\":\"8.24\"");
        json.Should().Contain("\"productId\":\"line\"");
        json.Should().Contain("\"quantity\":2");
        json.Should().Contain("\"sku\":null");
    }

    [Test]
    public void Keys_compare_exactly()
    {
        XpsFormat.KeysMatch("abc", "abc").Should().BeTrue();
        XpsFormat.KeysMatch("abd", "abc").Should().BeFalse();
        XpsFormat.KeysMatch(null, "abc").Should().BeFalse();
        XpsFormat.KeysMatch("abc", "").Should().BeFalse();
        XpsFormat.NewSecretKey().Should().HaveLength(32);
    }
}
