using System.Net;
using System.Text.RegularExpressions;

namespace Nop.Plugin.Misc.XpsShip.Logic;

/// <summary>
/// Reading the customer's checkout attributes (the "Special Instruction" box): plain text, PO numbers, do-not-ship.
/// </summary>
public static class Instructions
{
    private static readonly Regex LineBreak = new(@"<br\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Tag = new(@"<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"[ \t ]+", RegexOptions.Compiled);

    // "PO# 36591", "PO 36591", "po#A-77". The number must hold a digit, so "PO Box" isn't read as a PO.
    private static readonly Regex Po = new(@"\bPO\s*#?\s*([A-Za-z0-9-]*\d[A-Za-z0-9-]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DoNotShip = new(@"do\s+not\s+ship", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>HTML from nopCommerce's attribute descriptions as plain text, with &lt;br /&gt; turned into <paramref name="lineBreak"/></summary>
    public static string ToPlainText(string? html, string lineBreak)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var text = LineBreak.Replace(html, "\n");
        text = WebUtility.HtmlDecode(Tag.Replace(text, string.Empty));

        var lines = text.Split('\n')
            .Select(l => Spaces.Replace(l, " ").Trim())
            .Where(l => l.Length > 0);

        return string.Join(lineBreak, lines);
    }

    /// <summary>The PO number in the instruction text, or null</summary>
    public static string? FindPo(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        var match = Po.Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>"8102 PO 36591": the order number plus the PO if there is one, cut to UPS's 35-character reference limit</summary>
    public static string ShipperReference(string orderNumber, string? po)
    {
        var reference = string.IsNullOrEmpty(po) ? orderNumber : $"{orderNumber} PO {po}";
        return reference.Length <= XpsShipDefaults.ShipperReferenceMaxLength
            ? reference
            : reference.Substring(0, XpsShipDefaults.ShipperReferenceMaxLength);
    }

    public static bool SaysDoNotShip(string? text) => !string.IsNullOrEmpty(text) && DoNotShip.IsMatch(text);

    /// <summary>The customerNotes sent to XPS: the customer's shipping choice first, then the instruction text</summary>
    public static string CustomerNotes(string? shippingMethod, string instructions)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(shippingMethod))
            lines.Add($"Customer selected: {shippingMethod.Trim()}");
        if (!string.IsNullOrEmpty(instructions))
            lines.Add(instructions);

        return string.Join("\n", lines);
    }

    /// <summary>The item title: product name, then the attribute description, then the backordered prefix in front</summary>
    public static string LineTitle(string productName, string? attributeDescriptionHtml, bool backordered)
    {
        var attributes = ToPlainText(attributeDescriptionHtml, "; ");
        var title = string.IsNullOrEmpty(attributes) ? productName : $"{productName} | {attributes}";
        return backordered ? "BACKORDERED: " + title : title;
    }
}
