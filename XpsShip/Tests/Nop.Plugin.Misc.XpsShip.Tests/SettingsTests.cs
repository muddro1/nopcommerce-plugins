using FluentAssertions;
using Nop.Core;
using NUnit.Framework;

namespace Nop.Plugin.Misc.XpsShip.Tests;

public class SettingsTests
{
    // nopCommerce stores settings as strings through CommonHelper.To; the date cutoff must survive the round trip
    [Test]
    public void OrdersPlacedFromUtc_round_trips_through_setting_storage()
    {
        var value = (DateTime?)new DateTime(2026, 10, 19, 4, 0, 0, DateTimeKind.Utc);

        var stored = CommonHelper.To<string>(value);

        CommonHelper.To(stored, typeof(DateTime?)).Should().Be(value);
    }
}
