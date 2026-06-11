using System.Collections.Generic;
using DV;
using DV.ThingTypes;

namespace StockCarRemover;

// Garage rolling stock are out of scope for this mod, so we need to identify them correctly to exclude them
internal static class GarageVehicles
{
    // Demonstrator spawns are "garages" in the game's logic, but we don't want to touch those
    // as they overlap with their non-demonstrator variants.
    private static readonly HashSet<Garage> DemonstratorGarages =
    [
        Garage.DE2_Relic, Garage.DE6_Relic, Garage.DH4_Relic,
        Garage.DM3_Relic, Garage.S282_Relic, Garage.S060_Relic,
    ];

    private static HashSet<string>? _ids;

    private static HashSet<string> Ids
    {
        // Rebuild while empty so we don't latch an empty set if asked before the type registry loads.
        get
        {
            if (_ids == null || _ids.Count == 0) _ids = Build();
            return _ids;
        }
    }

    internal static bool Contains(TrainCarLivery livery) => Ids.Contains(livery.id);

    private static HashSet<string> Build()
    {
        var set = new HashSet<string>();
        var garages = Globals.G?.Types?.garages;
        if (garages == null) return set;

        foreach (var garage in garages)
        {
            if (garage?.garageCarLiveries == null || DemonstratorGarages.Contains(garage.v1)) continue;
            foreach (var livery in garage.garageCarLiveries)
                if (livery != null) set.Add(livery.id);
        }
        return set;
    }
}
