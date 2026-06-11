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

    private static List<(TrainCarKind kind, List<TrainCarLivery> liveries)>? _groups;
    private static List<TrainCarLivery>? _replaceableLiveries;
    private static readonly Dictionary<string, bool> _kindFoldouts = [];
    private static string? _openPickerFor;
    private static Vector2 _pickerScroll;
    private static string _pickerSearch = "";

    private const string IntroText =
        """
        Uncheck rolling stock to remove it from the spawn pool.

        Non-cargo stock can optionally be given a replacement.
            • Replacing non-shed locomotives will increase the overall quantity of spawned locomotives compared to just removing them.
            • Replacing shed stock only has an effect if set before starting a new save.
        """;

    internal static void OnGUI(UnityModManager.ModEntry entry)
    {
        if (Globals.G == null || Globals.G.Types == null)
        {
            GUILayout.Label("Waiting for game data to load…");
            return;
        }

        _groups ??= BuildGroups();
        _replaceableLiveries ??= [.. Globals.G.Types.Liveries
            .Where(l => CarTypes.IsAnyLocoSlugTender(l) || CarTypes.IsCaboose(l))
            .OrderBy(l => l.id)];

        if (_openPickerFor != null && !Main.Settings.DisabledLiveryIds.Contains(_openPickerFor))
            _openPickerFor = null;

        GUILayout.Label(IntroText, GUILayout.ExpandWidth(true));
        GUILayout.Space(4);

        Main.Settings.HideDisabledLocoLicenses = GUILayout.Toggle(
            Main.Settings.HideDisabledLocoLicenses,
            " Hide disabled locomotives' licenses from the career manager");
        GUILayout.Space(4);

        foreach (var (kind, liveries) in _groups)
            DrawKindSection(kind, liveries);
    }

    private static string Loc(string? key, string fallback) =>
        string.IsNullOrEmpty(key) ? fallback : LocalizationAPI.L(key);

    private static TrainCarLivery? GetLiveryById(string id) =>
        Globals.G?.Types?.Liveries.FirstOrDefault(l => l.id == id);

    private static void DrawKindSection(TrainCarKind kind, List<TrainCarLivery> liveries)
    {
        _kindFoldouts.TryGetValue(kind.id, out bool expanded);

        GUILayout.BeginVertical(GUI.skin.box);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button($"{(expanded ? "▼" : "▶")}  {Loc(kind.localizationKey, kind.id)}", GUILayout.ExpandWidth(true)))
            _kindFoldouts[kind.id] = !expanded;

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
            const int columns = 3;
            int col = 0;
            GUILayout.BeginHorizontal();
            foreach (var livery in liveries)
            {
                bool enabled = !Main.Settings.DisabledLiveryIds.Contains(livery.id);
                bool next = GUILayout.Toggle(enabled, Loc(livery.localizationKey, livery.id), GUILayout.Width(210));
                if (next != enabled)
                {
                    if (next) Main.Settings.DisabledLiveryIds.Remove(livery.id);
                    else Main.Settings.DisabledLiveryIds.Add(livery.id);
                }
                col++;
                if (col == columns)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    col = 0;
                }
            }
            GUILayout.EndHorizontal();

            var replaceableDisabled = liveries
                .Where(l => Main.Settings.DisabledLiveryIds.Contains(l.id) && (CarTypes.IsAnyLocoSlugTender(l) || CarTypes.IsCaboose(l)))
                .ToList();
            if (replaceableDisabled.Count > 0)
            {
                GUILayout.Space(4);
                GUILayout.Label("Replacements:");
                foreach (var livery in replaceableDisabled)
                    DrawReplacementRow(livery);
            }

            GUILayout.Space(2);
        }

        GUILayout.EndVertical();
        GUILayout.Space(2);
    }

    private static void DrawReplacementRow(TrainCarLivery livery)
    {
        bool pickerOpen = _openPickerFor == livery.id;
        string displayName = Loc(livery.localizationKey, livery.id);

        Main.Settings.LiveryReplacements.TryGetValue(livery.id, out var replacementId);
        string replacementLabel = string.IsNullOrEmpty(replacementId)
            ? NoReplacementLabel
            : GetLiveryById(replacementId) is TrainCarLivery rep
                ? Loc(rep.localizationKey, rep.id)
                : $"? {replacementId}";

        GUILayout.BeginHorizontal();
        GUILayout.Label(displayName, GUILayout.Width(200));
        GUILayout.Label("→", GUILayout.Width(20));
        if (GUILayout.Button($"{replacementLabel} ▼", GUILayout.Width(220)))
        {
            _openPickerFor = pickerOpen ? null : livery.id;
            _pickerScroll = Vector2.zero;
            _pickerSearch = "";
        }
        if (!string.IsNullOrEmpty(replacementId) && GUILayout.Button("Clear", GUILayout.Width(50)))
        {
            Main.Settings.LiveryReplacements.Remove(livery.id);
            if (_openPickerFor == livery.id) _openPickerFor = null;
        }
        GUILayout.EndHorizontal();

        if (pickerOpen)
            DrawReplacementPicker(livery.id);
    }

    private static void DrawReplacementPicker(string forLiveryId)
    {
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
            _openPickerFor = null;
        }

        foreach (var candidate in _replaceableLiveries!)
        {
            if (candidate.id == forLiveryId) continue;
            string displayName = Loc(candidate.localizationKey, candidate.id);
            if (_pickerSearch.Length > 0
                && !displayName.Contains(_pickerSearch)
                && !candidate.id.Contains(_pickerSearch))
                continue;
            if (GUILayout.Button($"{displayName}  [{candidate.id}]", GUILayout.ExpandWidth(true)))
            {
                Main.Settings.LiveryReplacements[forLiveryId] = candidate.id;
                _openPickerFor = null;
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private static List<(TrainCarKind kind, List<TrainCarLivery> liveries)> BuildGroups()
    {
        var result = new List<(TrainCarKind, List<TrainCarLivery>)>();
        foreach (var kind in Globals.G.Types.CarKinds)
        {
            var liveries = Globals.G.Types.Liveries
                .Where(l => l.parentType?.kind == kind && !CustomCarLoaderInterop.IsCustomCar(l))
                .OrderBy(l => l.id)
                .ToList();
            if (liveries.Count > 0)
                result.Add((kind, liveries));
        }
        return result;
    }
}
