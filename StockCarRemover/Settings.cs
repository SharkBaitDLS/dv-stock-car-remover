using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using DV.ThingTypes;
using UnityModManagerNet;

namespace StockCarRemover;

public class Settings : UnityModManager.ModSettings
{
    public const string NoTender = "__none__";

    public bool HideDisabledLocoLicenses { get; set; } = true;
    public bool EnableStationFallback { get; set; } = true;

    // XmlSerializer goes through the accessor methods below that conform to its interface
    [XmlIgnore] public HashSet<string> DisabledLiveryIds { get; set; } = [];
    [XmlIgnore] public Dictionary<string, string> LiveryReplacements { get; set; } = [];

    [XmlIgnore] public Dictionary<string, string> TenderOverrides { get; set; } = [];

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

    public TenderChoice[] Tenders
    {
        get => [.. TenderOverrides.Select(kv => new TenderChoice { LocoId = kv.Key, TenderId = kv.Value })];
        set => TenderOverrides = (value ?? []).ToDictionary(t => t.LocoId, t => t.TenderId);
    }

    public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);

    internal TrainCarLivery? GetReplacement(TrainCarLivery disabled) =>
        LiveryReplacements.TryGetValue(disabled.id, out var replacementId)
            ? Liveries.ById(replacementId)
            : null;

    public class Replacement
    {
        [XmlAttribute] public string LiveryId { get; set; } = "";
        [XmlAttribute] public string ReplacementId { get; set; } = "";
    }

    public class TenderChoice
    {
        [XmlAttribute] public string LocoId { get; set; } = "";
        [XmlAttribute] public string TenderId { get; set; } = "";
    }
}
