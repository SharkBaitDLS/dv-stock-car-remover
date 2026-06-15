using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using HarmonyLib;

// Optional integration with the Persistent Jobs mod which has entirely custom job generation logic
namespace StockCarRemover
{
    internal static class PjFilter
    {
        internal static bool Active => Main.Settings.DisabledLiveryIds.Count > 0;

        internal static MethodBase? Method(string typeName, string method) =>
            AccessTools.Method(AccessTools.TypeByName(typeName), method);
    }

    [HarmonyPatch]
    public static class CarSpawningJobGenerator_GenerateJobChain
    {
        const string target = "PersistentJobsMod.CarSpawningJobGenerators.CarSpawningJobGenerator";

        public static bool Prepare()
        {
            var found = AccessTools.TypeByName(target) != null;
            if (found) Main.Logger.Log("Persistent jobs mod detected, patching car-spawning job generation.");
            return found;
        }

        public static MethodBase? TargetMethod() => PjFilter.Method(target, "GenerateJobChain");

        // Snapshot of everything we temporarily mutate on the (shared) ruleset asset.
        public sealed class Snapshot
        {
            public bool Load, Haul, EmptyHaul;
            public List<CargoGroup> Output = null!, Input = null!;
        }

        public static void Prefix(StationProceduralJobsRuleset generationRuleset, out Snapshot? __state)
        {
            __state = null;
            if (!PjFilter.Active) return;

            // Shunting and freight haul draw from outputCargoGroups, logistical haul from inputCargoGroups
            var outputUsable = generationRuleset.outputCargoGroups.Any(CarFilter.IsCargoGroupUsable);
            var inputUsable = generationRuleset.inputCargoGroups.Any(CarFilter.IsCargoGroupUsable);

            var supportsOutput = generationRuleset.loadStartingJobSupported || generationRuleset.haulStartingJobSupported;
            var supportsInput = generationRuleset.emptyHaulStartingJobSupported;
            var canGenerateValid = (supportsOutput && outputUsable) || (supportsInput && inputUsable);

            if (!canGenerateValid && Main.Settings.EnableStationFallback) return;

            __state = new Snapshot
            {
                Load = generationRuleset.loadStartingJobSupported,
                Haul = generationRuleset.haulStartingJobSupported,
                EmptyHaul = generationRuleset.emptyHaulStartingJobSupported,
                Output = generationRuleset.outputCargoGroups,
                Input = generationRuleset.inputCargoGroups,
            };

            if (outputUsable)
            {
                generationRuleset.outputCargoGroups = generationRuleset.outputCargoGroups.Where(CarFilter.IsCargoGroupUsable).ToList();
            }
            else
            {
                generationRuleset.loadStartingJobSupported = false;
                generationRuleset.haulStartingJobSupported = false;
            }

            if (inputUsable)
            {
                generationRuleset.inputCargoGroups = generationRuleset.inputCargoGroups.Where(CarFilter.IsCargoGroupUsable).ToList();
            }
            else
            {
                generationRuleset.emptyHaulStartingJobSupported = false;
            }
        }

        public static void Finalizer(StationProceduralJobsRuleset generationRuleset, Snapshot? __state)
        {
            if (__state == null) return;
            generationRuleset.loadStartingJobSupported = __state.Load;
            generationRuleset.haulStartingJobSupported = __state.Haul;
            generationRuleset.emptyHaulStartingJobSupported = __state.EmptyHaul;
            generationRuleset.outputCargoGroups = __state.Output;
            generationRuleset.inputCargoGroups = __state.Input;
        }
    }

    [HarmonyPatch]
    public static class CarSpawnGroupsRandomizer_GetCarSpawnGroups
    {
        const string target = "PersistentJobsMod.CarSpawningJobGenerators.CarSpawnGroupsRandomizer";

        public static bool Prepare() => AccessTools.TypeByName(target) != null;

        public static MethodBase? TargetMethod() => PjFilter.Method(target, "GetCarSpawnGroups");

        public static void Prefix(ref List<CargoType> cargoTypes)
        {
            if (!PjFilter.Active) return;
            var filtered = cargoTypes.Where(CarFilter.IsCargoCarriable).ToList();
            // Overly defensive given that we should have been handed valid cargo types if our
            // primary filters worked correctly, but better safe than crashy.
            if (filtered.Count > 0 && filtered.Count != cargoTypes.Count) cargoTypes = filtered;
        }
    }

    [HarmonyPatch]
    public static class CarSpawnGroupsRandomizer_GetRandomLivery
    {
        const string target = "PersistentJobsMod.CarSpawningJobGenerators.CarSpawnGroupsRandomizer";

        public static bool Prepare() => AccessTools.TypeByName(target) != null;

        public static MethodBase? TargetMethod() => PjFilter.Method(target, "GetRandomLivery");

        public static bool Prefix(CargoType cargoType, Random random, ref TrainCarLivery __result)
        {
            if (!PjFilter.Active) return true;

            var usableCarTypes = cargoType.ToV2().loadableCarTypes
                .Where(t => CarFilter.IsCarTypeUsable(t.carType)).ToList();
            if (usableCarTypes.Count == 0) return true; // defensively fall back, but this should never happen

            var chosenCarType = usableCarTypes[random.Next(usableCarTypes.Count)];
            var usableLiveries = chosenCarType.carType.liveries.Where(CarFilter.IsLiveryUsable).ToList();
            if (usableLiveries.Count == 0) return true; // defensively fall back, but this should never happen

            __result = usableLiveries[random.Next(usableLiveries.Count)];
            return false;
        }
    }
}
