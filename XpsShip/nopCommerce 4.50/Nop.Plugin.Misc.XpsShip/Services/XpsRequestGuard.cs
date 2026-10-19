using Nop.Core;
using Nop.Plugin.Misc.XpsShip.Logic;
using Nop.Services.Logging;

namespace Nop.Plugin.Misc.XpsShip.Services;

/// <summary>The outcome of the checks every webhook call goes through before it is handled</summary>
public record GuardResult(bool Allowed, int HttpStatus, string? Message, string Ip)
{
    public static GuardResult Allow(string ip) => new(true, 200, null, ip);
}

/// <summary>
/// List Orders hands out customer names and addresses, so the webhooks are locked down like the admin:
/// HTTPS only, an optional IP allowlist, the secret key compared in constant time, and a lockout for key guessing.
/// </summary>
public class XpsRequestGuard
{
    private readonly FailedKeyLimiter _limiter;
    private readonly ILogger _logger;
    private readonly IWebHelper _webHelper;
    private readonly XpsShipSettings _settings;

    public XpsRequestGuard(FailedKeyLimiter limiter, ILogger logger, IWebHelper webHelper, XpsShipSettings settings)
    {
        _limiter = limiter;
        _logger = logger;
        _webHelper = webHelper;
        _settings = settings;
    }

    public async Task<GuardResult> CheckAsync(string? suppliedKey)
    {
        var ip = _webHelper.GetCurrentIpAddress() ?? string.Empty;

        if (!_settings.Enabled)
            return new GuardResult(false, 503, "integration is disabled", ip);

        // Never redirect: a redirect from http would send the key in the clear a second time
        if (!_webHelper.IsCurrentConnectionSecured())
            return new GuardResult(false, 403, "HTTPS required", ip);

        var allowlist = XpsFormat.SplitList(_settings.IpAllowlist);
        if (allowlist.Count > 0 && !allowlist.Contains(ip, StringComparer.OrdinalIgnoreCase))
            return new GuardResult(false, 403, $"IP {ip} is not on the allowlist", ip);

        var now = DateTime.UtcNow;
        if (_limiter.IsBlocked(ip, now))
            return new GuardResult(false, 403, $"IP {ip} is blocked after repeated wrong keys", ip);

        if (XpsFormat.KeysMatch(suppliedKey, _settings.SecretKey))
            return GuardResult.Allow(ip);

        if (_limiter.RecordFailure(ip, now))
        {
            await _logger.ErrorAsync($"XPS Ship: blocked {ip} for {XpsShipDefaults.BlockMinutes} minutes after more than " +
                $"{XpsShipDefaults.FailedKeyLimit} wrong-key requests in {XpsShipDefaults.FailedKeyWindowMinutes} minutes");
        }

        return new GuardResult(false, 401, string.IsNullOrEmpty(suppliedKey) ? "missing key" : "wrong key", ip);
    }
}
