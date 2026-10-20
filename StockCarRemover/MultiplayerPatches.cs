using System.Reflection;
using HarmonyLib;

namespace StockCarRemover;

[HarmonyPatch]
public static class Multiplayer_StationLocoSpawner_SpawnLocomotives
{
    const string target = "Multiplayer.Patches.World.StationLocoSpawner_Start_Patch";

    public static bool Prepare()
    {
        if (AccessTools.TypeByName(target) == null) return false;
        if (TargetMethod() != null)
        {
            Main.Logger.Log("Multiplayer mod detected, patching host loco spawning.");
            return true;
        }

        Main.Logger.Warning(
            "Multiplayer mod detected but its loco spawn routine could not be found. " +
            "Stations with no valid locomotive spawns may cause errors.");
        return false;
    }

    public static MethodBase? TargetMethod() =>
        AccessTools.Method(AccessTools.TypeByName(target), "SpawnLocomotives");

    public static bool Prefix(StationLocoSpawner __0) => __0.locoTypeGroupsToSpawn.Count > 0;
}
