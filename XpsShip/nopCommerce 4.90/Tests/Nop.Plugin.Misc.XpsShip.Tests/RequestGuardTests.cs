using FluentAssertions;
using Moq;
using Nop.Core;
using Nop.Plugin.Misc.XpsShip.Logic;
using Nop.Plugin.Misc.XpsShip.Services;
using Nop.Services.Logging;
using NUnit.Framework;

namespace Nop.Plugin.Misc.XpsShip.Tests;

public class RequestGuardTests
{
    private const string Key = "k3yk3yk3yk3yk3yk3yk3yk3yk3yk3yk3";

    private static XpsRequestGuard Guard(bool https = true, bool enabled = true, string allowlist = "", FailedKeyLimiter limiter = null)
    {
        var web = new Mock<IWebHelper>();
        web.Setup(w => w.IsCurrentConnectionSecured()).Returns(https);
        web.Setup(w => w.GetCurrentIpAddress()).Returns("10.0.0.9");

        return new XpsRequestGuard(limiter ?? new FailedKeyLimiter(), Mock.Of<ILogger>(), web.Object,
            new XpsShipSettings { Enabled = enabled, SecretKey = Key, IpAllowlist = allowlist });
    }

    [Test]
    public async Task The_right_key_over_https_is_allowed() =>
        (await Guard().CheckAsync(Key)).Allowed.Should().BeTrue();

    [Test]
    public async Task Disabled_is_503() =>
        (await Guard(enabled: false).CheckAsync(Key)).HttpStatus.Should().Be(503);

    [Test]
    public async Task Plain_http_is_403_even_with_the_key() =>
        (await Guard(https: false).CheckAsync(Key)).HttpStatus.Should().Be(403);

    [Test]
    public async Task Wrong_or_missing_key_is_401()
    {
        // T11
        (await Guard().CheckAsync("nope")).HttpStatus.Should().Be(401);
        (await Guard().CheckAsync(null)).HttpStatus.Should().Be(401);
    }

    [Test]
    public async Task The_allowlist_is_enforced_when_set()
    {
        (await Guard(allowlist: "1.1.1.1, 2.2.2.2").CheckAsync(Key)).HttpStatus.Should().Be(403);
        (await Guard(allowlist: "1.1.1.1,10.0.0.9").CheckAsync(Key)).Allowed.Should().BeTrue();
    }

    [Test]
    public async Task Too_many_wrong_keys_block_the_ip_even_for_the_right_key()
    {
        var limiter = new FailedKeyLimiter(2, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(60));
        var guard = Guard(limiter: limiter);

        for (var i = 0; i < 3; i++)
            await guard.CheckAsync("guess" + i);

        (await guard.CheckAsync(Key)).HttpStatus.Should().Be(403);
    }
}
