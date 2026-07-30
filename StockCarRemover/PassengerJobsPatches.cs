using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV.ThingTypes;
using HarmonyLib;

// Optional integration with the Passenger Jobs mod, since that ends up spawning vanilla passenger cars
namespace StockCarRemover
{
    internal static class PjPassengerFilter
    {
        internal const string ConsistManagerType = "PassengerJobs.Generation.ConsistManager";
        internal const string GeneratorType = "PassengerJobs.Generation.PassengerJobGenerator";

        private static MethodInfo? _allPassengerCars;

        internal static bool Active => Main.Settings.DisabledLiveryIds.Count > 0;

        internal static bool Installed => AccessTools.TypeByName(ConsistManagerType) != null;

        internal static MethodBase? Method(string typeName, string method)
        {
            var type = AccessTools.TypeByName(typeName);
            return type == null ? null : AccessTools.Method(type, method);
        }

        internal static bool Ready(MethodBase? target, string description)
        {
            if (!Installed) return false;
            if (target != null) return true;

            Main.Logger.Warning(
                $"Passenger jobs mod detected but {description} could not be found. " +
                "Passenger cars may continue to spawn regardless of settings.");
            return false;
        }

        internal static bool NothingLeftToSpawn()
        {
            if (!Active) return false;

            _allPassengerCars ??= Method(ConsistManagerType, "GetAllPassengerCars") as MethodInfo;
            if (_allPassengerCars == null) return false;

            var liveries = (IEnumerable<TrainCarLivery>)_allPassengerCars.Invoke(null, null);
            return !liveries.Any(CarFilter.IsLiveryUsable);
        }
    }

    [HarmonyPatch]
    public static class ConsistManager_GetFilteredPassengerCars
    {
        public static bool Prepare()
        {
            var ready = PjPassengerFilter.Ready(TargetMethod(), "the passenger consist pool");
            if (ready) Main.Logger.Log("Passenger jobs mod detected, patching passenger consist generation.");
            return ready;
        }

        public static MethodBase? TargetMethod() =>
            PjPassengerFilter.Method(PjPassengerFilter.ConsistManagerType, "GetFilteredPassengerCars");

        public static void Postfix(ref IEnumerable<TrainCarLivery> __result)
        {
            if (!PjPassengerFilter.Active) return;

            var filtered = __result.Where(CarFilter.IsLiveryUsable).ToList();

            if (filtered.Count == 0) return;

            __result = filtered;
        }
    }

    [HarmonyPatch]
    public static class PassengerJobGenerator_GeneratePassengerJobsRoutine
    {
        public static bool Prepare() => PjPassengerFilter.Ready(TargetMethod(), "the passenger job generation routine");

        public static MethodBase? TargetMethod()
        {
            var routine = PjPassengerFilter.Method(PjPassengerFilter.GeneratorType, "GeneratePassengerJobsRoutine");
            return routine == null ? null : AccessTools.EnumeratorMoveNext(routine);
        }

        public static bool Prefix(ref bool __result)
        {
            if (!PjPassengerFilter.NothingLeftToSpawn()) return true;

            __result = false;
            return false;
        }
    }
}
