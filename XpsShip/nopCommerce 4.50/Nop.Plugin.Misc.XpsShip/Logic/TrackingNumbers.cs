using System.Text.RegularExpressions;

namespace Nop.Plugin.Misc.XpsShip.Logic;

/// <summary>Cleaning, carrier detection and tracking links. Carrier codes are XPS's: ups, usps, fedex, dhl.</summary>
public static class TrackingNumbers
{
    public const string Ups = "ups";
    public const string Usps = "usps";
    public const string FedEx = "fedex";
    public const string Dhl = "dhl";

    private static readonly Regex UpsNumber = new(@"^1Z[0-9A-Z]{16}$", RegexOptions.Compiled);
    private static readonly Regex UpsAnywhere = new(@"1Z[0-9A-Z]{16}", RegexOptions.Compiled);
    private static readonly Regex FedExNumber = new(@"^(\d{12}|\d{15})$", RegexOptions.Compiled);
    private static readonly Regex UspsNumber = new(@"^9[2-5]\d{18,20}$", RegexOptions.Compiled);
    private static readonly Regex LongDigits = new(@"\d{12,}", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// XPS's comma-separated trackingNumbers, split, trimmed, uppercased, with blanks and duplicates dropped.
    /// Order is kept: the first is the master number of a multi-piece shipment.
    /// </summary>
    public static IReadOnlyList<string> Clean(string? raw)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        foreach (var part in raw.Split(','))
        {
            var number = Whitespace.Replace(part, string.Empty).ToUpperInvariant();
            if (number.Length > 0 && !result.Contains(number))
                result.Add(number);
        }

        return result;
    }

    /// <summary>The carrier code XPS sent, lowercased, or null when it isn't one of the four</summary>
    public static string? NormalizeCarrier(string? carrier)
    {
        var code = carrier?.Trim().ToLowerInvariant();
        return code is Ups or Usps or FedEx or Dhl ? code : null;
    }

    /// <summary>
    /// The tracking number inside text typed by hand before XPS ("UPS 1ZC602X...", "USPS 9334...", "UPSD b1Z...").
    /// Returns the input trimmed and uppercased when no number pattern is found.
    /// </summary>
    public static string StripCarrierText(string? text)
    {
        var upper = (text ?? string.Empty).Trim().ToUpperInvariant();

        var ups = UpsAnywhere.Match(upper);
        if (ups.Success)
            return ups.Value;

        var digits = LongDigits.Matches(upper).Select(m => m.Value).OrderByDescending(v => v.Length).FirstOrDefault();
        return digits ?? upper;
    }

    /// <summary>The carrier implied by a number's format, or null: 1Z is UPS, 12 or 15 digits FedEx, 92-95 with 20-22 digits USPS</summary>
    public static string? DetectCarrier(string? number)
    {
        if (string.IsNullOrEmpty(number))
            return null;
        if (UpsNumber.IsMatch(number))
            return Ups;
        if (UspsNumber.IsMatch(number))
            return Usps;
        if (FedExNumber.IsMatch(number))
            return FedEx;

        return null;
    }

    /// <summary>The carrier's public tracking page for a number, or null for an unknown carrier</summary>
    public static string? Url(string? carrier, string number)
    {
        var n = Uri.EscapeDataString(number);
        return NormalizeCarrier(carrier) switch
        {
            Ups => $"https://www.ups.com/track?tracknum={n}",
            Usps => $"https://tools.usps.com/go/TrackConfirmAction?tLabels={n}",
            FedEx => $"https://www.fedex.com/fedextrack/?trknbr={n}",
            Dhl => $"https://www.dhl.com/us-en/home/tracking.html?tracking-id={n}",
            _ => null
        };
    }

    /// <summary>"UPS", "USPS", "FedEx", "DHL" for notes and comments</summary>
    public static string DisplayName(string? carrier) => NormalizeCarrier(carrier) switch
    {
        Ups => "UPS",
        Usps => "USPS",
        FedEx => "FedEx",
        Dhl => "DHL",
        _ => carrier ?? string.Empty
    };
}
