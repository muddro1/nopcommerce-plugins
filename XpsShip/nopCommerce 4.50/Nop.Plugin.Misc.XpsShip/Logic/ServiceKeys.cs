namespace Nop.Plugin.Misc.XpsShip.Logic;

/// <summary>The "customer shipping method = service key" map from settings</summary>
public static class ServiceKeys
{
    /// <summary>Lines of "method = KEY". Lines without '=' or with an empty side are ignored; later lines win.</summary>
    public static IReadOnlyDictionary<string, string> Parse(string? map)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (method, key) in Pairs(map))
            result[method] = key;

        return result;
    }

    /// <summary>The key of the first "role = KEY" line naming one of the customer's roles (any case), or null</summary>
    public static string? ResolveByRole(IEnumerable<string?> roleNames, string? map)
    {
        var roles = new HashSet<string>(roleNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()), StringComparer.OrdinalIgnoreCase);
        return Pairs(map).Where(p => roles.Contains(p.Left)).Select(p => p.Right).FirstOrDefault();
    }

    private static IEnumerable<(string Left, string Right)> Pairs(string? map)
    {
        foreach (var line in (map ?? string.Empty).Split('\n'))
        {
            var separator = line.LastIndexOf('=');
            if (separator <= 0)
                continue;

            var left = line.Substring(0, separator).Trim();
            var right = line.Substring(separator + 1).Trim();
            if (left.Length > 0 && right.Length > 0)
                yield return (left, right);
        }
    }

    /// <summary>The key for the customer's shipping method, or the default when it isn't mapped</summary>
    public static string Resolve(string? shippingMethod, IReadOnlyDictionary<string, string> map, string defaultKey)
    {
        if (!string.IsNullOrWhiteSpace(shippingMethod) && map.TryGetValue(shippingMethod.Trim(), out var key))
            return key;

        return defaultKey;
    }
}
