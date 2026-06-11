using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV;
using DV.Localization;
using DV.ThingTypes;
using HarmonyLib;
using UnityModManagerNet;
using UnityEngine;

namespace StockCarRemover;

public static class Main
{
    internal static Settings Settings { get; private set; } = null!;
    private static UnityModManager.ModEntry ModEntry { get; set; } = null!;

    private static bool Load(UnityModManager.ModEntry modEntry)
    {
        ModEntry = modEntry;
        Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);

        Harmony? harmony = null;
        try
        {
            harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            // GetRandomFromList<T> is a generic instance method — PatchAll cannot
            // resolve generic instantiations, so we register them manually.
            var genericDef = AccessTools.Method(typeof(StationProceduralJobGenerator), "GetRandomFromList");
            var prefix = new HarmonyMethod(typeof(GetRandomFromListPatch), nameof(GetRandomFromListPatch.Prefix));
            harmony.Patch(genericDef.MakeGenericMethod(typeof(TrainCarLivery)), prefix: prefix);
            harmony.Patch(genericDef.MakeGenericMethod(typeof(TrainCarType_v2)), prefix: prefix);
        }
        catch (Exception ex)
        {
            modEntry.Logger.LogException($"Failed to load {modEntry.Info.DisplayName}:", ex);
            harmony?.UnpatchAll(modEntry.Info.Id);
            return false;
        }

        modEntry.OnGUI = OnGUI;
        modEntry.OnSaveGUI = OnSaveGUI;
        return true;
    }

    private static void OnSaveGUI(UnityModManager.ModEntry entry) => Settings.Save(entry);

    // ── GUI state ────────────────────────────────────────────────────────────

    // Cache built once after game data is available.
    private static List<(TrainCarKind kind, List<TrainCarLivery> liveries)>? _groups;
    private static readonly Dictionary<string, bool> _kindFoldouts = new();

    private static void OnGUI(UnityModManager.ModEntry entry)
    {
        if (Globals.G == null || Globals.G.Types == null)
        {
            GUILayout.Label("Waiting for game data to load…");
            return;
        }

        _groups ??= BuildGroups();

        GUILayout.Label(
            "Uncheck a car or locomotive to remove it from procedural job generation and station spawning.",
            GUILayout.ExpandWidth(true));
        GUILayout.Space(4);

        foreach (var (kind, liveries) in _groups)
            DrawKindSection(kind, liveries);
    }

    private static string Loc(string? key, string fallback) =>
        string.IsNullOrEmpty(key) ? fallback : LocalizationAPI.L(key);

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
                Settings.DisabledLiveryIds.Remove(l.id);
        }
        if (GUILayout.Button("Disable All", GUILayout.Width(90)))
        {
            foreach (var l in liveries)
                Settings.DisabledLiveryIds.Add(l.id);
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
                bool enabled = !Settings.DisabledLiveryIds.Contains(livery.id);
                bool next = GUILayout.Toggle(enabled, Loc(livery.localizationKey, livery.id), GUILayout.Width(210));
                if (next != enabled)
                {
                    if (next) Settings.DisabledLiveryIds.Remove(livery.id);
                    else Settings.DisabledLiveryIds.Add(livery.id);
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
            GUILayout.Space(2);
        }

        GUILayout.EndVertical();
        GUILayout.Space(2);
    }

    private static List<(TrainCarKind kind, List<TrainCarLivery> liveries)> BuildGroups()
    {
        var result = new List<(TrainCarKind, List<TrainCarLivery>)>();
        foreach (var kind in Globals.G.Types.CarKinds)
        {
            var liveries = Globals.G.Types.Liveries
                .Where(l => l.parentType?.kind == kind)
                .OrderBy(l => l.id)
                .ToList();
            if (liveries.Count > 0)
                result.Add((kind, liveries));
        }
        return result;
    }
}
