using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DV.ThingTypes;
using HarmonyLib;
using UnityEngine;

namespace StockCarRemover;

// Hooks into StationLocoSpawner at initialization to pre-filter the spawn pool,
// ensuring correct probability distribution (disabled locos don't dilute the pool
// with empty spawn slots). Stores originals so settings changes can be re-applied
// live without needing a save reload.
//
// Honors the original spawn pools by default, only overriding them where locomotive
// replacements are explicitly specified. This means that a configuration that disables
// too many locomotives will see reduced overall locomotive spawns unless replacements
// are selected for enough of them, as certain tracks only have a subset of locomotives
// in their pools.
[HarmonyPatch(typeof(StationLocoSpawner), "Start")]
public static class StationLocoSpawner_Start_Patch
{
    // Weakly keyed so entries don't prevent spawner GC when scenes unload.
    private static readonly ConditionalWeakTable<StationLocoSpawner, List<List<TrainCarLivery>>> _originals = new();

    public static void Postfix(StationLocoSpawner __instance)
    {
        var originals = __instance.locoTypeGroupsToSpawn
            .Select(g => new List<TrainCarLivery>(g.liveries))
            .ToList();
        _originals.Add(__instance, originals);

        ApplySettings(__instance);
    }

    internal static void ReapplyAll()
    {
        foreach (var spawner in Object.FindObjectsOfType<StationLocoSpawner>())
            Reapply(spawner);
    }

    private static void Reapply(StationLocoSpawner spawner)
    {
        if (!_originals.TryGetValue(spawner, out var originals)) return;

        spawner.locoTypeGroupsToSpawn.Clear();
        foreach (var liveries in originals)
            spawner.locoTypeGroupsToSpawn.Add(new ListTrainCarTypeWrapper([.. liveries]));

        ApplySettings(spawner);
    }

    private static void ApplySettings(StationLocoSpawner instance)
    {
        if (Main.Settings.DisabledLiveryIds.Count == 0)
            return;

        foreach (var group in instance.locoTypeGroupsToSpawn)
            ApplyToGroup(group.liveries);
        instance.locoTypeGroupsToSpawn.RemoveAll(group => group.liveries.Count == 0);

        var count = instance.locoTypeGroupsToSpawn.Count;
        if (count > 0)
            Traverse.Create(instance)
                .Field("nextLocoGroupSpawnIndex")
                .SetValue(Random.Range(0, count));
    }

    private static void ApplyToGroup(List<TrainCarLivery> liveries)
    {
        for (int i = liveries.Count - 1; i >= 0; i--)
        {
            var slot = liveries[i];
            if (!Main.Settings.DisabledLiveryIds.Contains(slot.id)) continue;

            var rep = Main.Settings.GetReplacement(slot);
            if (rep == null)
            {
                liveries.RemoveAt(i);
                continue;
            }
            liveries[i] = rep;

            // Couple the configured tender to the replaced loco (keyed by the original slot id).
            if (CarTypes.IsLocomotive(slot))
                ApplyTenderAt(liveries, i, ResolveTenderId(slot, rep));
        }
        RemoveOrphanTenders(liveries);
    }

    private static string ResolveTenderId(TrainCarLivery slot, TrainCarLivery replacement) =>
        Main.Settings.TenderOverrides.TryGetValue(slot.id, out var choice)
            ? choice
            : Liveries.ConventionalTender(replacement)?.id ?? Settings.NoTender;

    private static void ApplyTenderAt(List<TrainCarLivery> liveries, int locoIndex, string tenderId)
    {
        int t = locoIndex + 1;
        bool hasTrailingTender = t < liveries.Count && CarTypes.IsTender(liveries[t]);

        var tender = tenderId == Settings.NoTender ? null : Liveries.ById(tenderId);

        if (tender == null)
        {
            if (hasTrailingTender) liveries.RemoveAt(t);
            return;
        }
        if (hasTrailingTender) liveries[t] = tender;
        else liveries.Insert(t, tender);
    }

    private static void RemoveOrphanTenders(List<TrainCarLivery> liveries)
    {
        for (int i = liveries.Count - 1; i >= 0; i--)
            if (CarTypes.IsTender(liveries[i]) && (i == 0 || !CarTypes.IsLocomotive(liveries[i - 1])))
                liveries.RemoveAt(i);
    }
}

// Skip the spawn logic entirely when there is nothing left to spawn in a location
[HarmonyPatch(typeof(StationLocoSpawner), "Update")]
public static class StationLocoSpawner_Update_Patch
{
    public static bool Prefix(StationLocoSpawner __instance) =>
        __instance.locoTypeGroupsToSpawn.Count > 0;
}
