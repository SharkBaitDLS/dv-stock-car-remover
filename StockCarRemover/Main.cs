using System;
using System.Linq;
using System.Reflection;
using DV;
using DV.ThingTypes;
using HarmonyLib;
using UnityModManagerNet;

namespace StockCarRemover;

public static class Main
{
    internal static Settings Settings { get; private set; } = null!;

    private static bool Load(UnityModManager.ModEntry modEntry)
    {
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

        modEntry.OnGUI = SettingsGUI.OnGUI;
        modEntry.OnSaveGUI = entry => Settings.Save(entry);
        return true;
    }

    // Returns the configured replacement for a disabled loco/tender livery, or
    // null if none is set, the replacement is also disabled, or the livery is
    // not a loco or tender.
    internal static TrainCarLivery? GetReplacement(TrainCarLivery disabled)
    {
        if (!Settings.LiveryReplacements.TryGetValue(disabled.id, out var replacementId)) return null;
        return Globals.G?.Types?.Liveries.FirstOrDefault(l => l.id == replacementId);
    }
}
