namespace Nop.Plugin.Misc.XpsShip.Logic;

/// <summary>An order line that can still go into a shipment</summary>
public record ShippableLine(Guid OrderItemGuid, int OrderItemId, int Remaining, decimal? UnitWeight);

public record PlannedItem(ShippableLine Line, int Quantity);

public class ShipmentPlan
{
    public IReadOnlyList<PlannedItem> Items { get; init; } = Array.Empty<PlannedItem>();

    /// <summary>Any error means nothing is created (422)</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>Quantities that were capped; logged, but the shipment still goes ahead</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public decimal ItemWeight => Items.Sum(i => (i.Line.UnitWeight ?? 0) * i.Quantity);
}

/// <summary>Turns XPS's posted items into shipment items against what the order still has to ship</summary>
public static class ShipmentPlanner
{
    /// <param name="requested">The posted items. Empty means XPS sent no item list: ship everything remaining.</param>
    /// <param name="allLines">Every ship-enabled line on the order, including fully shipped ones</param>
    public static ShipmentPlan Plan(IReadOnlyList<UpdateItem> requested, IReadOnlyList<ShippableLine> allLines)
    {
        if (requested.Count == 0)
        {
            var everything = allLines.Where(l => l.Remaining > 0).Select(l => new PlannedItem(l, l.Remaining)).ToList();
            return everything.Count == 0
                ? new ShipmentPlan { Errors = new[] { "nothing left to ship on this order" } }
                : new ShipmentPlan { Items = everything };
        }

        var errors = new List<string>();
        var warnings = new List<string>();

        // Match every id first: one unknown id rejects the whole post
        var quantities = new Dictionary<Guid, int>();
        foreach (var item in requested)
        {
            var line = Guid.TryParse(item.Id, out var guid) ? allLines.FirstOrDefault(l => l.OrderItemGuid == guid) : null;
            if (line == null)
            {
                errors.Add($"item id {item.Id} is not a line on this order");
                continue;
            }

            quantities[line.OrderItemGuid] = quantities.TryGetValue(line.OrderItemGuid, out var q) ? q + item.Quantity : item.Quantity;
        }

        if (errors.Count > 0)
            return new ShipmentPlan { Errors = errors };

        var items = new List<PlannedItem>();
        foreach (var line in allLines)
        {
            if (!quantities.TryGetValue(line.OrderItemGuid, out var quantity) || quantity == 0)
                continue;

            if (quantity > line.Remaining)
            {
                warnings.Add($"item {line.OrderItemGuid}: {quantity} posted, only {line.Remaining} left to ship; shipped {line.Remaining}");
                quantity = line.Remaining;
            }

            if (quantity > 0)
                items.Add(new PlannedItem(line, quantity));
        }

        if (items.Count == 0)
            errors.Add("the posted items have nothing left to ship");

        return new ShipmentPlan { Items = errors.Count > 0 ? Array.Empty<PlannedItem>() : items, Errors = errors, Warnings = warnings };
    }
}
