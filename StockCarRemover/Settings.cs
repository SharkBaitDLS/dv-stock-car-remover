using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using DV;
using DV.ThingTypes;
using UnityModManagerNet;

namespace StockCarRemover;

public class Settings : UnityModManager.ModSettings
{
    public bool HideDisabledLocoLicenses { get; set; } = true;
    public bool EnableStationFallback { get; set; } = true;

    // XmlSerializer goes through the accessor methods below that conform to its interface 
    [XmlIgnore] public HashSet<string> DisabledLiveryIds { get; set; } = [];
    [XmlIgnore] public Dictionary<string, string> LiveryReplacements { get; set; } = [];

    public string[] DisabledLiveries
    {
        get => [.. DisabledLiveryIds];
        set => DisabledLiveryIds = [.. value ?? []];
    }

    public Replacement[] Replacements
    {
        get => [.. LiveryReplacements.Select(kv => new Replacement { LiveryId = kv.Key, ReplacementId = kv.Value })];
        set => LiveryReplacements = (value ?? []).ToDictionary(r => r.LiveryId, r => r.ReplacementId);
    }

    public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);

    internal TrainCarLivery? GetReplacement(TrainCarLivery disabled)
    {
        if (!LiveryReplacements.TryGetValue(disabled.id, out var replacementId)) return null;
        return Globals.G?.Types?.Liveries.FirstOrDefault(l => l.id == replacementId);
    }

    public class Replacement
    {
        [XmlAttribute] public string LiveryId { get; set; } = "";
        [XmlAttribute] public string ReplacementId { get; set; } = "";
    }
}
