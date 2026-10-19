using FluentAssertions;
using Nop.Plugin.Misc.XpsShip.Logic;
using NUnit.Framework;

namespace Nop.Plugin.Misc.XpsShip.Tests;

public class TrackingNumbersTests
{
    [Test]
    public void Clean_splits_trims_uppercases_and_dedupes_keeping_the_master_first()
    {
        // T10: the hand-typed entry seen in Sept 2026
        TrackingNumbers.Clean("1zc602x70219206948, 1ZC602X70220017391,1ZC602X70224542806, ,1ZC602X70220017391")
            .Should().Equal("1ZC602X70219206948", "1ZC602X70220017391", "1ZC602X70224542806");
    }

    [Test]
    public void Clean_of_nothing_is_empty()
    {
        TrackingNumbers.Clean(null).Should().BeEmpty();
        TrackingNumbers.Clean(" , ").Should().BeEmpty();
    }

    [TestCase("UPS", "ups")]
    [TestCase(" usps ", "usps")]
    [TestCase("FedEx", "fedex")]
    [TestCase("dhl", "dhl")]
    [TestCase("ontrac", null)]
    [TestCase(null, null)]
    public void NormalizeCarrier_accepts_only_xps_carriers(string input, string expected) =>
        TrackingNumbers.NormalizeCarrier(input).Should().Be(expected);

    // T21: shipments typed by hand before XPS
    [TestCase("UPS 1ZC602X71321659005", "1ZC602X71321659005", "ups")]
    [TestCase("UPSD b1ZC602X71321659005", "1ZC602X71321659005", "ups")]
    [TestCase("USPS 9334611045000004779320", "9334611045000004779320", "usps")]
    [TestCase("FEDEX 876435110017", "876435110017", "fedex")]
    [TestCase("1ZC602X70219206948", "1ZC602X70219206948", "ups")]
    [TestCase("Hand Deliver", "HAND DELIVER", null)]
    public void Typed_numbers_are_stripped_and_their_carrier_detected(string typed, string number, string carrier)
    {
        var stripped = TrackingNumbers.StripCarrierText(typed);
        stripped.Should().Be(number);
        TrackingNumbers.DetectCarrier(stripped).Should().Be(carrier);
    }

    [Test]
    public void A_fifteen_digit_number_is_fedex()
    {
        TrackingNumbers.DetectCarrier("123456789012345").Should().Be("fedex");
    }

    [Test]
    public void Urls_follow_each_carriers_pattern()
    {
        TrackingNumbers.Url("ups", "1Z1").Should().Be("https://www.ups.com/track?tracknum=1Z1");
        TrackingNumbers.Url("usps", "9400").Should().Be("https://tools.usps.com/go/TrackConfirmAction?tLabels=9400");
        TrackingNumbers.Url("fedex", "8764").Should().Be("https://www.fedex.com/fedextrack/?trknbr=8764");
        TrackingNumbers.Url("dhl", "JD01").Should().Be("https://www.dhl.com/us-en/home/tracking.html?tracking-id=JD01");
        TrackingNumbers.Url(null, "X").Should().BeNull();
    }
}
