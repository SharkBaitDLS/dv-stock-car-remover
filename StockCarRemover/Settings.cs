using System.Collections.Generic;
using System.Linq;
using DV;
using DV.ThingTypes;
using UnityModManagerNet;

namespace StockCarRemover;

public class Settings : UnityModManager.ModSettings
{
    public HashSet<string> DisabledLiveryIds { get; set; } = new();
    public Dictionary<string, string> LiveryReplacements { get; set; } = new();

    public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);

    internal TrainCarLivery? GetReplacement(TrainCarLivery disabled)
    {
        if (!LiveryReplacements.TryGetValue(disabled.id, out var replacementId)) return null;
        return Globals.G?.Types?.Liveries.FirstOrDefault(l => l.id == replacementId);
    }
}
