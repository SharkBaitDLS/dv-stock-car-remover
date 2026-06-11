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

    private static IDictionary? ResolveRegistry()
    {
        var injector = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("CCL.Importer.CarTypeInjector"))
            .FirstOrDefault(t => t != null);

        return injector?.GetField("IdToLiveryMap", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null) as IDictionary;
    }

    internal static bool IsCustomCar(TrainCarLivery livery) =>
        IdToLiveryMap != null && IdToLiveryMap.Contains(livery.id);
}
