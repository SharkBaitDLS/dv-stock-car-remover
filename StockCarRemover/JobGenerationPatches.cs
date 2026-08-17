using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using HarmonyLib;

namespace StockCarRemover;

// Constrains the procedural job generator's random selections so it only ever picks
// cargo and cars that resolve to enabled liveries. By filtering at every level of the
// cargo -> car-type -> car funnel (starting with the cargo group itself), the
// generator never commits to an impossible job and then no-ops, ensuring stations
// populate with as many valid jobs as possible.
public static class JobGenerationPatches
{
    private static readonly FieldInfo RngField =
        AccessTools.Field(typeof(StationProceduralJobGenerator), "currentRng_");

    // These are the private generic helpers GetRandomFromList<T> / GetMultipleRandomsFromList<T>.
    // PatchAll can't resolve open generics, so each instantiation we care about is registered
    // directly.
    internal static void Register(Harmony harmony)
    {
        var pick = AccessTools.Method(typeof(StationProceduralJobGenerator), "GetRandomFromList");
        Patch(harmony, pick.MakeGenericMethod(typeof(CargoGroup)), nameof(PickPrefix));

        var pickMany = AccessTools.Method(typeof(StationProceduralJobGenerator), "GetMultipleRandomsFromList");
        Patch(harmony, pickMany.MakeGenericMethod(typeof(CargoType)), nameof(CargoTypesPrefix));
    }

    private static void Patch(Harmony harmony, MethodBase target, string prefix) =>
        harmony.Patch(target, prefix: new HarmonyMethod(typeof(JobGenerationPatches), prefix));

    private static System.Random Rng(StationProceduralJobGenerator instance) =>
        (System.Random)RngField.GetValue(instance);

    // Picks from the subset matching `usable`, falling back to the full list if nothing
    // qualifies.
    private static T PickFiltered<T>(List<T> list, System.Func<T, bool> usable, System.Random rng)
    {
        var pool = list.Where(usable).ToList();
        var source = pool.Count > 0 ? pool : list;
        return source[rng.Next(0, source.Count)];
    }

    // Shared across every reference-type GetRandomFromList<T> call due to Harmony patching limitations.
    // Dispatches on the runtime list type and only constrains the cargo/car funnel while leaving all other
    // branches vanilla.
    public static bool PickPrefix(StationProceduralJobGenerator __instance, object __0, ref object __result)
    {
        if (Main.Settings.DisabledLiveryIds.Count == 0) return true;
        switch (__0)
        {
            case List<CargoGroup> groups:
                __result = PickFiltered(groups, CarFilter.IsCargoGroupUsable, Rng(__instance));
                return false;
            case List<TrainCarType_v2> carTypes:
                __result = PickFiltered(carTypes, CarFilter.IsCarTypeUsable, Rng(__instance));
                return false;
            case List<TrainCarLivery> liveries:
                __result = PickFiltered(liveries, CarFilter.IsLiveryUsable, Rng(__instance));
                return false;
            default:
                return true;
        }
    }

    // Filters the cargo pool to carriable types before drawing the requested count of
    // distinct cargo
    public static bool CargoTypesPrefix(
        StationProceduralJobGenerator __instance, List<CargoType> __0, int __1, ref List<CargoType> __result)
    {
        if (Main.Settings.DisabledLiveryIds.Count == 0) return true;

        var pool = __0.Where(CarFilter.IsCargoCarriable).ToList();
        if (pool.Count == 0) return true;

        var rng = Rng(__instance);
        int count = System.Math.Min(__1, pool.Count);
        var picked = new List<CargoType>(count);
        for (int i = 0; i < count; i++)
        {
            int index = rng.Next(0, pool.Count);
            picked.Add(pool[index]);
            pool.RemoveAt(index);
        }
        __result = picked;
        return false;
    }
}

// Decides up front whether a station can produce a valid job at all.
[HarmonyPatch(typeof(StationProceduralJobGenerator), nameof(StationProceduralJobGenerator.GenerateJobChain))]
public static class StationProceduralJobGenerator_GenerateJobChain
{
    private static readonly FieldInfo RulesetField =
        AccessTools.Field(typeof(StationProceduralJobGenerator), "generationRuleset");

    // Snapshot of everything we temporarily mutate on the station's ruleset.
    public sealed class Snapshot
    {
        public bool Load, Haul, Unload, EmptyHaul;
        public List<CargoGroup> Output = null!, Input = null!;
    }

    public static void Prefix(StationProceduralJobGenerator __instance, out Snapshot? __state)
    {
        __state = null;
        if (Main.Settings.DisabledLiveryIds.Count == 0) return;

        var ruleset = (StationProceduralJobsRuleset)RulesetField.GetValue(__instance);
        if (ruleset == null) return;

        // Shunting load and freight haul draw from outputCargoGroups, shunting unload and
        // logistical haul from inputCargoGroups
        var outputUsable = ruleset.outputCargoGroups.Any(CarFilter.IsCargoGroupUsable);
        var inputUsable = ruleset.inputCargoGroups.Any(CarFilter.IsCargoGroupUsable);

        var supportsOutput = ruleset.loadStartingJobSupported || ruleset.haulStartingJobSupported;
        var supportsInput = ruleset.unloadStartingJobSupported || ruleset.emptyHaulStartingJobSupported;
        var canGenerateValid = (supportsOutput && outputUsable) || (supportsInput && inputUsable);

        if (!canGenerateValid && Main.Settings.EnableStationFallback) return;

        __state = new Snapshot
        {
            Load = ruleset.loadStartingJobSupported,
            Haul = ruleset.haulStartingJobSupported,
            Unload = ruleset.unloadStartingJobSupported,
            EmptyHaul = ruleset.emptyHaulStartingJobSupported,
            Output = ruleset.outputCargoGroups,
            Input = ruleset.inputCargoGroups,
        };

        if (outputUsable)
        {
            ruleset.outputCargoGroups = ruleset.outputCargoGroups.Where(CarFilter.IsCargoGroupUsable).ToList();
        }
        else
        {
            ruleset.loadStartingJobSupported = false;
            ruleset.haulStartingJobSupported = false;
        }

        if (inputUsable)
        {
            ruleset.inputCargoGroups = ruleset.inputCargoGroups.Where(CarFilter.IsCargoGroupUsable).ToList();
        }
        else
        {
            ruleset.unloadStartingJobSupported = false;
            ruleset.emptyHaulStartingJobSupported = false;
        }
    }

    public static void Finalizer(StationProceduralJobGenerator __instance, Snapshot? __state)
    {
        if (__state == null) return;

        var ruleset = (StationProceduralJobsRuleset)RulesetField.GetValue(__instance);
        if (ruleset == null) return;

        ruleset.loadStartingJobSupported = __state.Load;
        ruleset.haulStartingJobSupported = __state.Haul;
        ruleset.unloadStartingJobSupported = __state.Unload;
        ruleset.emptyHaulStartingJobSupported = __state.EmptyHaul;
        ruleset.outputCargoGroups = __state.Output;
        ruleset.inputCargoGroups = __state.Input;
    }
}

public static class CarFilter
{
    public static bool IsLiveryUsable(TrainCarLivery livery) =>
        !Main.Settings.DisabledLiveryIds.Contains(livery.id);

    public static bool IsCarTypeUsable(TrainCarType_v2 carType) =>
        carType.liveries.Any(IsLiveryUsable);

    public static bool IsCargoCarriable(CargoType cargo)
    {
        var map = Globals.G?.Types?.CargoToLoadableCarTypes;
        if (map == null) return true;
        return map.TryGetValue(cargo.ToV2(), out var carTypes) && carTypes.Any(IsCarTypeUsable);
    }

    public static bool IsCargoGroupUsable(CargoGroup group) =>
        group.cargoTypes.Any(IsCargoCarriable);
}
