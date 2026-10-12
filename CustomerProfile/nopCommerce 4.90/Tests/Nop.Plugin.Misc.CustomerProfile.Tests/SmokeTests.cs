using FluentAssertions;
using NUnit.Framework;

namespace Nop.Plugin.Misc.CustomerProfile.Tests;

public class SmokeTests
{
    [Test]
    public void System_name_is_stable()
    {
        CustomerProfileDefaults.SystemName.Should().Be("Misc.CustomerProfile");
    }
}
