using System;
using System.Linq;
using DV;
using DV.ThingTypes;

namespace StockCarRemover;

internal static class Liveries
{
    internal static TrainCarLivery? ById(string? id) =>
        string.IsNullOrEmpty(id) ? null : Globals.G?.Types?.Liveries.FirstOrDefault(l => l.id == id);

    // CCL as the primary source of truth, anything without a configured trainset falls back to the game's
    // <livery>A + <livery>B convention.
    internal static TrainCarLivery? AutoTender(TrainCarLivery loco)
    {
        if (!CarTypes.IsLocomotive(loco)) return null;

        foreach (var member in CustomCarLoaderInterop.TrainsetFor(loco))
            if (member != null && CarTypes.IsTender(member)) return member;

        return ConventionalTender(loco);
    }

    private static TrainCarLivery? ConventionalTender(TrainCarLivery loco)
    {
        if (!loco.id.EndsWith("A", StringComparison.Ordinal)) return null;
        var tender = ById(loco.id.Substring(0, loco.id.Length - 1) + "B");
        return tender != null && CarTypes.IsTender(tender) ? tender : null;
    }
}
