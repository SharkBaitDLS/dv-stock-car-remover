using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using DV.ThingTypes;

namespace StockCarRemover;

// Detects liveries injected by CCL so we don't redundantly populate the UI.
// 
// Uses reflection rather than a hard dependency so CCL isn't a requirement for
// this mod to be installed. If CCL reworks how they inject cars this may break.
internal static class CustomCarLoaderInterop
{
    private static readonly IDictionary? IdToLiveryMap = ResolveRegistry();
    private static readonly MethodInfo? TrainsetLookup = ResolveTrainsetLookup();

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

    internal static bool IsCustomCar(TrainCarLivery livery) =>
        IdToLiveryMap != null && IdToLiveryMap.Contains(livery.id);

    internal static TrainCarLivery[] TrainsetFor(TrainCarLivery livery) =>
        TrainsetLookup?.Invoke(null, [livery]) as TrainCarLivery[] ?? [];
}
