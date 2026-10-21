namespace Nop.Plugin.Misc.XpsShip.Logic;

/// <summary>A ship-to address as text: one part per line, "City, ST Zip" together, empty parts left out</summary>
public static class ShipTo
{
    public const int MaxLength = 1000;

    public static string? Format(string? name, string? company, string? address1, string? address2,
        string? city, string? state, string? zip, string? country)
    {
        static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        var cityState = string.Join(", ", new[] { Clean(city), Clean(state) }.Where(p => p != null));
        var cityLine = string.Join(" ", new[] { cityState, Clean(zip) }.Where(p => !string.IsNullOrEmpty(p)));
        var lines = new[] { Clean(name), Clean(company), Clean(address1), Clean(address2), cityLine, Clean(country) }
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();
        if (lines.Count == 0)
            return null;

        var text = string.Join("\n", lines);
        return text.Length > MaxLength ? text.Substring(0, MaxLength) : text;
    }
}
