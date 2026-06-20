using System.Collections.Generic;
using System.Linq;
using DV;
using DV.Localization;
using DV.ThingTypes;
using UnityEngine;
using UnityModManagerNet;

namespace StockCarRemover;

internal static class SettingsGUI
{
    private const string NoReplacementLabel = "none (removed)";
    private const string NoTenderLabel = "none (no tender)";

    private static List<TrainCarLivery>? _replaceableLiveries;
    private static List<TrainCarLivery>? _candidateTenders;
    private static readonly Dictionary<string, bool> _kindFoldouts = [];
    private static string? _openPickerFor;
    private static Vector2 _pickerScroll;
    private static string _pickerSearch = "";

    private static string? _openTenderPickerFor;
    private static Vector2 _tenderScroll;
    private static string _tenderSearch = "";

    private const string IntroText =
        """
        Uncheck rolling stock to remove it from the spawn pool.

        Disabled locomotives can optionally be given a replacement to spawn in their place, which increases overall locomotive spawn rates.
        """;

    internal static void OnGUI(UnityModManager.ModEntry entry)
    {
        if (Globals.G == null || Globals.G.Types == null)
        {
            GUILayout.Label("Waiting for game data to load…");
            return;
        }

        _replaceableLiveries ??= [.. Globals.G.Types.Liveries
            .Where(CarTypes.IsAnyLocoSlugTender)
            .OrderBy(l => l.id)];

        _candidateTenders ??= [.. Globals.G.Types.Liveries
            .Where(CarTypes.IsTender)
            .OrderBy(l => l.id)];

        if (_openPickerFor != null && !Main.Settings.DisabledLiveryIds.Contains(_openPickerFor))
            _openPickerFor = null;

        if (_openTenderPickerFor != null && !Main.Settings.DisabledLiveryIds.Contains(_openTenderPickerFor))
            _openTenderPickerFor = null;

        GUILayout.Label(IntroText, GUILayout.ExpandWidth(true));
        GUILayout.Space(4);

        Main.Settings.HideDisabledLocoLicenses = GUILayout.Toggle(
            Main.Settings.HideDisabledLocoLicenses,
            " Hide disabled locomotives' licenses from the career manager");

        Main.Settings.EnableStationFallback = GUILayout.Toggle(
            Main.Settings.EnableStationFallback,
            " Fall back to vanilla spawn rules if no jobs can be generated at a station");

        GUILayout.Space(4);

        foreach (var (key, label, liveries) in BuildGroups())
            DrawKindSection(key, label, liveries);
    }

    private static string Loc(string? key, string fallback) =>
        string.IsNullOrEmpty(key) ? fallback : LocalizationAPI.L(key);

    private static void DrawKindSection(string key, string label, List<TrainCarLivery> liveries)
    {
        _kindFoldouts.TryGetValue(key, out bool expanded);
        bool isLocoKind = liveries.Any(CarTypes.IsLocomotive);

        GUILayout.BeginVertical(GUI.skin.box);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button($"{(expanded ? "▼" : "▶")}  {label}", GUILayout.ExpandWidth(true)))
            _kindFoldouts[key] = !expanded;

        if (GUILayout.Button("Enable All", GUILayout.Width(90)))
        {
            foreach (var l in liveries)
                Main.Settings.DisabledLiveryIds.Remove(l.id);
        }
        if (GUILayout.Button("Disable All", GUILayout.Width(90)))
        {
            foreach (var l in liveries)
                Main.Settings.DisabledLiveryIds.Add(l.id);
        }
        GUILayout.EndHorizontal();

        if (expanded)
        {
            GUILayout.Space(2);
            if (isLocoKind)
                foreach (var livery in liveries)
                    DrawLocoRow(livery);
            else
                DrawCheckboxGrid(liveries);
            GUILayout.Space(2);
        }

        GUILayout.EndVertical();
        GUILayout.Space(2);
    }

    private static void DrawCheckboxGrid(List<TrainCarLivery> liveries)
    {
        const int columns = 3;
        int col = 0;
        GUILayout.BeginHorizontal();
        foreach (var livery in liveries)
        {
            DrawEnableToggle(livery, 210);
            col++;
            if (col == columns)
            {
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                col = 0;
            }
        }
        GUILayout.EndHorizontal();
    }

    private static void DrawLocoRow(TrainCarLivery livery)
    {
        GUILayout.BeginHorizontal();
        DrawEnableToggle(livery, 220);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        if (Main.Settings.DisabledLiveryIds.Contains(livery.id) && CarTypes.IsAnyLocoSlugTender(livery))
            DrawReplacementLine(livery);
    }

    private static void DrawEnableToggle(TrainCarLivery livery, float width)
    {
        bool enabled = !Main.Settings.DisabledLiveryIds.Contains(livery.id);
        bool next = GUILayout.Toggle(enabled, Loc(livery.localizationKey, livery.id), GUILayout.Width(width));
        if (next == enabled) return;
        if (next)
        {
            Main.Settings.DisabledLiveryIds.Remove(livery.id);
            Main.Settings.TenderOverrides.Remove(livery.id);
        }
        else Main.Settings.DisabledLiveryIds.Add(livery.id);
    }

    private static void DrawReplacementLine(TrainCarLivery livery)
    {
        bool pickerOpen = _openPickerFor == livery.id;

        Main.Settings.LiveryReplacements.TryGetValue(livery.id, out var replacementId);
        string replacementLabel = string.IsNullOrEmpty(replacementId)
            ? NoReplacementLabel
            : Liveries.ById(replacementId) is TrainCarLivery rep
                ? Loc(rep.localizationKey, rep.id)
                : $"? {replacementId}";

        GUILayout.BeginHorizontal();
        GUILayout.Space(20);
        GUILayout.Label("Replace with:", GUILayout.Width(85));
        if (GUILayout.Button($"{replacementLabel} ▼", GUILayout.Width(220)))
        {
            _openPickerFor = pickerOpen ? null : livery.id;
            _pickerScroll = Vector2.zero;
            _pickerSearch = "";
        }
        if (!string.IsNullOrEmpty(replacementId) && GUILayout.Button("Clear", GUILayout.Width(50)))
        {
            Main.Settings.LiveryReplacements.Remove(livery.id);
            Main.Settings.TenderOverrides.Remove(livery.id);
            if (_openPickerFor == livery.id) _openPickerFor = null;
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        if (pickerOpen)
            DrawReplacementPicker(livery);

        if (CarTypes.IsLocomotive(livery))
            DrawTenderLine(livery, Liveries.ById(replacementId));
    }

    private static void DrawReplacementPicker(TrainCarLivery loco)
    {
        string forLiveryId = loco.id;
        GUILayout.BeginVertical(GUI.skin.box);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Search:", GUILayout.Width(50));
        var newSearch = GUILayout.TextField(_pickerSearch, GUILayout.ExpandWidth(true));
        if (newSearch != _pickerSearch)
        {
            _pickerSearch = newSearch;
            _pickerScroll = Vector2.zero;
        }
        GUILayout.EndHorizontal();

        _pickerScroll = GUILayout.BeginScrollView(_pickerScroll, GUILayout.Height(160));

        if (GUILayout.Button(NoReplacementLabel, GUILayout.ExpandWidth(true)))
        {
            Main.Settings.LiveryReplacements.Remove(forLiveryId);
            Main.Settings.TenderOverrides.Remove(forLiveryId);
            _openPickerFor = null;
        }

        foreach (var candidate in _replaceableLiveries!)
        {
            if (candidate.id == forLiveryId) continue;
            string displayName = Loc(candidate.localizationKey, candidate.id);
            if (_pickerSearch.Length > 0
                && !displayName.ToLower().Contains(_pickerSearch.ToLower())
                && !candidate.id.ToLower().Contains(_pickerSearch.ToLower()))
                continue;
            if (GUILayout.Button($"{displayName}  [{candidate.id}]", GUILayout.ExpandWidth(true)))
            {
                Main.Settings.LiveryReplacements[forLiveryId] = candidate.id;
                Main.Settings.TenderOverrides[forLiveryId] = Liveries.ConventionalTender(candidate)?.id ?? Settings.NoTender;
                _openPickerFor = null;
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private static void DrawTenderLine(TrainCarLivery loco, TrainCarLivery? effectiveLoco)
    {
        bool pickerOpen = _openTenderPickerFor == loco.id;

        GUILayout.BeginHorizontal();
        GUILayout.Space(20);
        GUILayout.Label("Tender:", GUILayout.Width(85));
        if (GUILayout.Button($"{TenderLabel(loco, effectiveLoco)} ▼", GUILayout.Width(220)))
        {
            _openTenderPickerFor = pickerOpen ? null : loco.id;
            _tenderScroll = Vector2.zero;
            _tenderSearch = "";
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        if (pickerOpen)
            DrawTenderPicker(loco, effectiveLoco);
    }

    private static string TenderLabel(TrainCarLivery loco, TrainCarLivery? effectiveLoco)
    {
        if (Main.Settings.TenderOverrides.TryGetValue(loco.id, out var choice))
        {
            if (choice == Settings.NoTender)
                return NoTenderLabel;
            return Liveries.ById(choice) is TrainCarLivery t
                ? Loc(t.localizationKey, t.id)
                : $"? {choice}";
        }
        return DefaultTenderLabel(effectiveLoco);
    }

    private static string DefaultTenderLabel(TrainCarLivery? effectiveLoco)
    {
        var natural = effectiveLoco != null ? Liveries.ConventionalTender(effectiveLoco) : null;
        return natural != null
            ? $"Default ({Loc(natural.localizationKey, natural.id)})"
            : "Default (none)";
    }

    private static void DrawTenderPicker(TrainCarLivery loco, TrainCarLivery? effectiveLoco)
    {
        GUILayout.BeginVertical(GUI.skin.box);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Search:", GUILayout.Width(50));
        var newSearch = GUILayout.TextField(_tenderSearch, GUILayout.ExpandWidth(true));
        if (newSearch != _tenderSearch)
        {
            _tenderSearch = newSearch;
            _tenderScroll = Vector2.zero;
        }
        GUILayout.EndHorizontal();

        _tenderScroll = GUILayout.BeginScrollView(_tenderScroll, GUILayout.Height(160));

        if (GUILayout.Button(DefaultTenderLabel(effectiveLoco), GUILayout.ExpandWidth(true)))
        {
            Main.Settings.TenderOverrides.Remove(loco.id);
            _openTenderPickerFor = null;
        }
        if (GUILayout.Button(NoTenderLabel, GUILayout.ExpandWidth(true)))
        {
            Main.Settings.TenderOverrides[loco.id] = Settings.NoTender;
            _openTenderPickerFor = null;
        }

        foreach (var candidate in _candidateTenders!)
        {
            string displayName = Loc(candidate.localizationKey, candidate.id);
            if (_tenderSearch.Length > 0
                && !displayName.ToLower().Contains(_tenderSearch.ToLower())
                && !candidate.id.ToLower().Contains(_tenderSearch.ToLower()))
                continue;
            if (GUILayout.Button($"{displayName}  [{candidate.id}]", GUILayout.ExpandWidth(true)))
            {
                Main.Settings.TenderOverrides[loco.id] = candidate.id;
                _openTenderPickerFor = null;
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private static List<(string key, string label, List<TrainCarLivery> liveries)> BuildGroups()
    {
        var result = new List<(string, string, List<TrainCarLivery>)>();

        foreach (var kind in Globals.G.Types.CarKinds)
        {
            var liveries = Globals.G.Types.Liveries
                .Where(l => l.parentType?.kind == kind
                            && !CustomCarLoaderInterop.IsCustomCar(l)
                            && !GarageVehicles.Contains(l)
                            && !CarTypes.IsTender(l))
                .OrderBy(l => l.id)
                .ToList();
            if (liveries.Count > 0)
                result.Add((kind.id, Loc(kind.localizationKey, kind.id), liveries));
        }
        return result;
    }
}
