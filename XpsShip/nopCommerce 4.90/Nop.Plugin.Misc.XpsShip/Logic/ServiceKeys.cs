namespace Nop.Plugin.Misc.XpsShip.Logic;

/// <summary>The "customer shipping method = service key" map from settings</summary>
public static class ServiceKeys
{
    /// <summary>Lines of "method = KEY". Lines without '=' or with an empty side are ignored; later lines win.</summary>
    public static IReadOnlyDictionary<string, string> Parse(string? map)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in (map ?? string.Empty).Split('\n'))
        {
            var separator = line.LastIndexOf('=');
            if (separator <= 0)
                continue;

            var method = line.Substring(0, separator).Trim();
            var key = line.Substring(separator + 1).Trim();
            if (method.Length > 0 && key.Length > 0)
                result[method] = key;
        }

        return result;
    }

    /// <summary>The key for the customer's shipping method, or the default when it isn't mapped</summary>
    public static string Resolve(string? shippingMethod, IReadOnlyDictionary<string, string> map, string defaultKey)
    {
        if (!string.IsNullOrWhiteSpace(shippingMethod) && map.TryGetValue(shippingMethod.Trim(), out var key))
            return key;

        return defaultKey;
    }
}
