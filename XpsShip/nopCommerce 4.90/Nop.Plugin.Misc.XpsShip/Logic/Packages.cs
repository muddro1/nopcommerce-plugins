using Nop.Plugin.Misc.XpsShip.Models;

namespace Nop.Plugin.Misc.XpsShip.Logic;

/// <param name="Dimensions">Length, width, height in inches, or null when the product has none</param>
public record PackableLine(int Remaining, decimal? UnitWeightLb, bool ShipSeparately, (decimal Length, decimal Width, decimal Height)? Dimensions);

public static class Packages
{
    /// <summary>
    /// One package per unit of a "Ship separately" product, with its own weight and size, plus one package for
    /// everything else with the summed weight and no size so staff still pick that box. Null when no line ships
    /// separately: XPS then gets no packages and works as before.
    /// </summary>
    public static IList<XpsPackage>? For(IEnumerable<PackableLine> lines)
    {
        var open = lines.Where(l => l.Remaining > 0).ToList();
        if (!open.Any(l => l.ShipSeparately))
            return null;

        var packages = new List<XpsPackage>();
        foreach (var line in open.Where(l => l.ShipSeparately))
        {
            for (var i = 0; i < line.Remaining; i++)
                packages.Add(new XpsPackage
                {
                    Weight = line.UnitWeightLb.HasValue ? XpsFormat.Weight(line.UnitWeightLb.Value) : null,
                    Length = line.Dimensions.HasValue ? XpsFormat.Weight(line.Dimensions.Value.Length) : null,
                    Width = line.Dimensions.HasValue ? XpsFormat.Weight(line.Dimensions.Value.Width) : null,
                    Height = line.Dimensions.HasValue ? XpsFormat.Weight(line.Dimensions.Value.Height) : null
                });
        }

        var rest = open.Where(l => !l.ShipSeparately).ToList();
        if (rest.Count > 0)
        {
            var weight = rest.Sum(l => (l.UnitWeightLb ?? 0) * l.Remaining);
            packages.Add(new XpsPackage { Weight = weight > 0 ? XpsFormat.Weight(weight) : null });
        }

        return packages;
    }
}
