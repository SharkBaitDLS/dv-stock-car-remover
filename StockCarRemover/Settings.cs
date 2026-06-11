using System.Collections.Generic;
using UnityModManagerNet;

namespace StockCarRemover;

public class Settings : UnityModManager.ModSettings
{
    public HashSet<string> DisabledLiveryIds { get; set; } = new();

    public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);
}
