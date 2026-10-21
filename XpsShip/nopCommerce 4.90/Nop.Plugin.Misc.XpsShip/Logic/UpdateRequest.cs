using System.Globalization;
using System.Text.RegularExpressions;

namespace Nop.Plugin.Misc.XpsShip.Logic;

public record UpdateItem(string Id, int Quantity);

/// <summary>An XPS Update Order post, parsed from its form fields</summary>
public class UpdateRequest
{
    public string? OrderId { get; init; }
    public string? CarrierRaw { get; init; }
    public string? Carrier { get; init; }
    public IReadOnlyList<string> TrackingNumbers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<UpdateItem> Items { get; init; } = Array.Empty<UpdateItem>();

    /// <summary>quote[charges][total]: what the label cost</summary>
    public decimal? LabelCost { get; init; }

    /// <summary>quote[details][quotedWeight] in <see cref="QuotedWeightUnit"/>; can be dimensional weight</summary>
    public decimal? QuotedWeight { get; init; }

    public string? QuotedWeightUnit { get; init; }

    /// <summary>The receiver XPS printed on the label, formatted by <see cref="ShipTo.Format"/>; null when not posted</summary>
    public string? ShipToAddress { get; init; }

    /// <summary>Problems that make the post unusable (422)</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>The billed weight in pounds, when XPS gave it in pounds (or gave no unit)</summary>
    public decimal? BilledWeightLb =>
        QuotedWeight.HasValue && (string.IsNullOrEmpty(QuotedWeightUnit) || QuotedWeightUnit.Trim().ToLowerInvariant() is "lb" or "lbs" or "pound" or "pounds")
            ? QuotedWeight
            : null;

    // items[0][id], items[0][quantity]; XPS's docs also show dotted names, so both are accepted
    private static readonly Regex ItemField = new(@"^items(?:\[(\d+)\]|\.(\d+))(?:\[(id|quantity)\]|\.(id|quantity))$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ChargesTotal = new(@"^quote(?:\[charges\]|\.charges)(?:\[\d+\]|\.\d+)?(?:\[total\]|\.total)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static UpdateRequest Parse(IEnumerable<KeyValuePair<string, string>> form)
    {
        var fields = form.ToList();
        string? Get(params string[] names) =>
            fields.FirstOrDefault(f => names.Any(n => string.Equals(f.Key, n, StringComparison.OrdinalIgnoreCase))).Value;

        var errors = new List<string>();

        var orderId = Get("orderId")?.Trim();
        if (string.IsNullOrEmpty(orderId))
            errors.Add("orderId is missing");

        var carrierRaw = Get("carrier")?.Trim();
        var carrier = Logic.TrackingNumbers.NormalizeCarrier(carrierRaw);
        if (carrier == null)
            errors.Add($"carrier '{carrierRaw}' is not ups, usps, fedex or dhl");

        var numbers = Logic.TrackingNumbers.Clean(Get("trackingNumbers"));
        if (numbers.Count == 0)
            errors.Add("trackingNumbers is empty");
        foreach (var number in numbers.Where(n => n.Length > XpsShipDefaults.TrackingNumberMaxLength))
            errors.Add($"tracking number {number.Substring(0, 20)}... is longer than {XpsShipDefaults.TrackingNumberMaxLength} characters");

        // Group the item fields by index; the index only pairs an id with its quantity
        var items = new SortedDictionary<int, (string? Id, string? Quantity)>();
        foreach (var field in fields)
        {
            var match = ItemField.Match(field.Key);
            if (!match.Success)
                continue;

            var index = int.Parse(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value, CultureInfo.InvariantCulture);
            var name = (match.Groups[3].Success ? match.Groups[3].Value : match.Groups[4].Value).ToLowerInvariant();
            items.TryGetValue(index, out var item);
            items[index] = name == "id" ? (field.Value, item.Quantity) : (item.Id, field.Value);
        }

        var parsedItems = new List<UpdateItem>();
        foreach (var (index, item) in items)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                errors.Add($"items[{index}] has no id");
                continue;
            }

            if (!int.TryParse(item.Quantity?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) || quantity < 0)
            {
                errors.Add($"items[{index}] quantity '{item.Quantity}' is not a whole number");
                continue;
            }

            parsedItems.Add(new UpdateItem(item.Id.Trim(), quantity));
        }

        string? Receiver(string field) => Get($"receiver[{field}]", $"receiver.{field}");

        return new UpdateRequest
        {
            OrderId = orderId,
            CarrierRaw = carrierRaw,
            Carrier = carrier,
            TrackingNumbers = numbers,
            Items = parsedItems,
            LabelCost = XpsFormat.ParseDecimal(fields.FirstOrDefault(f => ChargesTotal.IsMatch(f.Key)).Value),
            QuotedWeight = XpsFormat.ParseDecimal(Get("quote[details][quotedWeight]", "quote.details.quotedWeight")),
            QuotedWeightUnit = Get("quote[details][quotedWeightUnit]", "quote.details.quotedWeightUnit"),
            ShipToAddress = ShipTo.Format(Receiver("name"), Receiver("company"), Receiver("address1"), Receiver("address2"),
                Receiver("city"), Receiver("state"), Receiver("zip"), Receiver("country")),
            Errors = errors
        };
    }
}
