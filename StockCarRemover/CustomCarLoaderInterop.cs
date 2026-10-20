using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV.ThingTypes;
using HarmonyLib;

namespace StockCarRemover;

// Detects liveries injected by CCL so we don't redundantly populate the UI, and surfaces
// CCL's per-station locomotive spawn chances.
//
// Uses reflection rather than a hard dependency so CCL isn't a requirement for
// this mod to be installed. If CCL reworks how they inject cars this may break.
internal static class CustomCarLoaderInterop
{
    private static readonly IDictionary? IdToLiveryMap = ResolveRegistry();
    private static readonly MethodInfo? TrainsetLookup = ResolveTrainsetLookup();

    private static readonly Type? SpawnChanceDataType = FindType("CCL.Importer.StationSpawnChanceData");
    private static readonly IDictionary? SpawnChanceData = ResolveSpawnChanceData();
    internal static readonly MethodInfo? AddSpawnChanceData = ResolveAddSpawnChanceData();
    private static readonly MethodInfo? LiveryChance = ResolveLiveryChance();

    private static readonly Dictionary<StationLocoSpawner, string> SpawnerStations = [];

    private static Type? FindType(string name) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name))
            .FirstOrDefault(t => t != null);

    private static IDictionary? ResolveRegistry() =>
        FindType("CCL.Importer.CarTypeInjector")
            ?.GetField("IdToLiveryMap", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null) as IDictionary;

    private static MethodInfo? ResolveTrainsetLookup() =>
        FindType("CCL.Importer.CarManager")
            ?.GetMethod("GetTrainsetForLivery", BindingFlags.Public | BindingFlags.Static);

    private static IDictionary? ResolveSpawnChanceData() =>
        SpawnChanceDataType
            ?.GetField("Data", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null) as IDictionary;

    private static MethodInfo? ResolveAddSpawnChanceData() =>
        SpawnChanceDataType
            ?.GetMethod("AddData", BindingFlags.Public | BindingFlags.Static, null,
                [typeof(string), typeof(StationLocoSpawner)], null);

    private static MethodInfo? ResolveLiveryChance() =>
        SpawnChanceDataType
            ?.GetMethod("GetChance", BindingFlags.Public | BindingFlags.Instance, null,
                [typeof(TrainCarLivery)], null);

    internal static bool IsCustomCar(TrainCarLivery livery) =>
        IdToLiveryMap != null && IdToLiveryMap.Contains(livery.id);

    internal static TrainCarLivery[] TrainsetFor(TrainCarLivery livery) =>
        TrainsetLookup?.Invoke(null, [livery]) as TrainCarLivery[] ?? [];

    internal static bool HasSpawnChances =>
        SpawnChanceData != null && AddSpawnChanceData != null && LiveryChance != null;

    internal static void RecordSpawnerStation(StationLocoSpawner spawner, string station) =>
        SpawnerStations[spawner] = station;

    internal static void RefreshSpawnChances()
    {
        if (!HasSpawnChances) return;

        foreach (var destroyed in SpawnerStations.Keys.Where(s => s == null).ToList())
            SpawnerStations.Remove(destroyed);

        foreach (var station in SpawnerStations.GroupBy(kv => kv.Value, kv => kv.Key).ToList())
        {
            SpawnChanceData!.Remove(station.Key);
            foreach (var spawner in station)
                AddSpawnChanceData!.Invoke(null, [station.Key, spawner]);
        }
    }

    internal static List<(string Station, object Data)> StationSpawnChances() =>
        SpawnChanceData == null || !SpawnerStations.Keys.Any(s => s != null)
            ? []
            : [.. SpawnChanceData.Keys.Cast<string>().Select(key => (key, SpawnChanceData[key]))];

    internal static float SpawnChance(object stationData, TrainCarLivery livery) =>
        (float)LiveryChance!.Invoke(stationData, [livery]);
}

[HarmonyPatch]
public static class StationSpawnChanceData_AddData_Patch
{
    public static bool Prepare() => CustomCarLoaderInterop.HasSpawnChances;

    public static MethodBase? TargetMethod() => CustomCarLoaderInterop.AddSpawnChanceData;

    public static void Postfix(string __0, StationLocoSpawner __1) =>
        CustomCarLoaderInterop.RecordSpawnerStation(__1, __0);
}
