using System.Collections.Generic;
using System.Linq;
using DV.ThingTypes;
using HarmonyLib;
using UnityEngine;

namespace StockCarRemover;

// Filters liveries/car types out of the job generator's random-selection pool.
// GetRandomFromList<T> is the single method the procedural job generator uses to
// pick both a TrainCarLivery and a TrainCarType_v2 for every job it creates, so
// patching it is the minimal, robust hook point.
// The generic instantiations are registered manually in Main.Load because
// harmony.PatchAll cannot resolve open generic methods on its own.
public static class GetRandomFromListPatch
{
    public static void Prefix(object __0)
    {
        switch (__0)
        {
            case List<TrainCarLivery> liveries:
                for (int i = liveries.Count - 1; i >= 0; i--)
                {
                    if (!Main.Settings.DisabledLiveryIds.Contains(liveries[i].id)) continue;
                    var rep = Main.GetReplacement(liveries[i]);
                    if (rep != null) liveries[i] = rep;
                    else liveries.RemoveAt(i);
                }
                break;

            case List<TrainCarType_v2> carTypes:
                carTypes.RemoveAll(ct => ct.liveries.All(l =>
                    Main.Settings.DisabledLiveryIds.Contains(l.id) && Main.GetReplacement(l) == null));
                break;
        }
    }
}

// Rewrites locoTypeGroupsToSpawn before the station's spawn loop can run.
// Disabled liveries are removed from each group (or replaced when configured);
// groups that become entirely empty are dropped so Update()'s index-based
// selection never hits them.
//
// nextLocoGroupSpawnIndex is set by Awake() against the unfiltered count before
// Start() runs. After shortening the list we re-randomize it so Update() never
// hits an out-of-bounds access on the first player visit.
[HarmonyPatch(typeof(StationLocoSpawner), "Start")]
public static class StationLocoSpawner_Start_Patch
{
    public static void Postfix(StationLocoSpawner __instance)
    {
        if (Main.Settings.DisabledLiveryIds.Count == 0) return;

        __instance.locoTypeGroupsToSpawn.RemoveAll(group =>
        {
            for (int i = group.liveries.Count - 1; i >= 0; i--)
            {
                if (!Main.Settings.DisabledLiveryIds.Contains(group.liveries[i].id)) continue;
                var rep = Main.GetReplacement(group.liveries[i]);
                if (rep != null) group.liveries[i] = rep;
                else group.liveries.RemoveAt(i);
            }
            return group.liveries.Count == 0;
        });

        var count = __instance.locoTypeGroupsToSpawn.Count;
        if (count > 0)
            Traverse.Create(__instance)
                .Field("nextLocoGroupSpawnIndex")
                .SetValue(Random.Range(0, count));
    }
}

// Catch-all safety net: filters any remaining disabled liveries from non-player
// SpawnCarTypesOnTrack calls. Handles settings changes made after Start() has run.
// Uses ref to replace with new filtered lists rather than mutating the stored groups.
[HarmonyPatch(typeof(CarSpawner), nameof(CarSpawner.SpawnCarTypesOnTrack))]
public static class CarSpawner_SpawnCarTypesOnTrack_Patch
{
    public static void Prefix(
        ref List<TrainCarLivery> trainCarTypes,
        ref List<bool> carsOrientationReversed,
        bool playerSpawnedCars)
    {
        if (playerSpawnedCars || Main.Settings.DisabledLiveryIds.Count == 0) return;

        var filteredLiveries = new List<TrainCarLivery>(trainCarTypes.Count);
        var filteredOrientations = carsOrientationReversed != null ? new List<bool>() : null;

        for (int i = 0; i < trainCarTypes.Count; i++)
        {
            var livery = trainCarTypes[i];
            if (Main.Settings.DisabledLiveryIds.Contains(livery.id))
            {
                var rep = Main.GetReplacement(livery);
                if (rep == null) continue;
                livery = rep;
            }
            filteredLiveries.Add(livery);
            filteredOrientations?.Add(carsOrientationReversed![i]);
        }

        trainCarTypes = filteredLiveries;
        if (filteredOrientations != null) carsOrientationReversed = filteredOrientations;
    }
}
