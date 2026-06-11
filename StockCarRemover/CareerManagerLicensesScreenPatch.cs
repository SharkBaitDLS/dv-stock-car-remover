using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DV;
using DV.ServicePenalty.UI;
using DV.ThingTypes;
using HarmonyLib;
using UnityEngine;

namespace StockCarRemover;

// Hides a locomotive's general license from the career manager's licenses screen when it is
// disabled from spawning. This is purely a UI convenience and does not actually remove the license
// from the save, so any extant locomotives on the map that the user may still want to drive will
// continue to be operable.
//
// The original display order is snapshotted at Awake so ReapplyAll() can restore and re-filter
// it live on settings save. The manager screen will repopulate its rows on its next Activate/scroll.
[HarmonyPatch(typeof(CareerManagerLicensesScreen))]
public static class CareerManagerLicensesScreen_Patch
{
    private static readonly FieldInfo DisplayOrderField =
        AccessTools.Field(typeof(CareerManagerLicensesScreen), "licensesDisplayOrder");

    // Weakly keyed so entries don't prevent screen GC when scenes unload.
    private static readonly ConditionalWeakTable<CareerManagerLicensesScreen, object[]> _originals = new();

    [HarmonyPatch("Awake")]
    [HarmonyPostfix]
    public static void SnapshotAndTrim(CareerManagerLicensesScreen __instance)
    {
        var displayOrder = (IList)DisplayOrderField.GetValue(__instance);

        var snapshot = new object[displayOrder.Count];
        displayOrder.CopyTo(snapshot, 0);
        _originals.Add(__instance, snapshot);

        ApplySettings(displayOrder);
    }

    internal static void ReapplyAll()
    {
        foreach (var screen in Object.FindObjectsOfType<CareerManagerLicensesScreen>())
        {
            if (!_originals.TryGetValue(screen, out var snapshot)) continue;

            var displayOrder = (IList)DisplayOrderField.GetValue(screen);
            displayOrder.Clear();
            foreach (var entry in snapshot)
                displayOrder.Add(entry);

            ApplySettings(displayOrder);
        }
    }

    private static void ApplySettings(IList displayOrder)
    {
        if (!Main.Settings.HideDisabledLocoLicenses || Main.Settings.DisabledLiveryIds.Count == 0) return;

        for (int i = displayOrder.Count - 1; i >= 0; i--)
        {
            var license = Traverse.Create(displayOrder[i]).Field("generalLicense").GetValue<GeneralLicenseType_v2>();
            if (license != null && IsLicenseFullyDisabled(license))
                displayOrder.RemoveAt(i);
        }
    }

    // Keeps the scroll bound in sync with the trimmed display list.
    [HarmonyPatch("TotalSlotCount", MethodType.Getter)]
    [HarmonyPostfix]
    public static void AdjustSlotCount(CareerManagerLicensesScreen __instance, ref int __result)
    {
        if (!Main.Settings.HideDisabledLocoLicenses || Main.Settings.DisabledLiveryIds.Count == 0) return;
        __result = ((IList)DisplayOrderField.GetValue(__instance)).Count;
    }

    private static bool IsLicenseFullyDisabled(GeneralLicenseType_v2 license)
    {
        var requiring = Globals.G.Types.Liveries.Where(l => l.requiredLicense == license).ToList();
        return requiring.Count > 0 && requiring.All(l => Main.Settings.DisabledLiveryIds.Contains(l.id));
    }
}
