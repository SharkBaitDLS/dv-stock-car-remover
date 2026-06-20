using System.Linq;
using DV;
using DV.ThingTypes;

namespace StockCarRemover;

internal static class Liveries
{
    internal static TrainCarLivery? ById(string? id) =>
        string.IsNullOrEmpty(id) ? null : Globals.G?.Types?.Liveries.FirstOrDefault(l => l.id == id);

    internal static TrainCarLivery? ConventionalTender(TrainCarLivery loco)
    {
        if (!CarTypes.IsLocomotive(loco) || !loco.id.EndsWith("A")) return null;
        var tender = ById(loco.id.Substring(0, loco.id.Length - 1) + "B");
        return tender != null && CarTypes.IsTender(tender) ? tender : null;
    }
}
