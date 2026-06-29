using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Shared.Starlight.NPC;

/// <summary>
/// When a player uses this item in-hand, all entries in <see cref="Loot"/> are spawned at the
/// item's location and thrown outward. The bag then deletes itself.
/// Mobs equipping this bag as gear will not be slowed (override SpeedModifier in YAML).
/// </summary>
[RegisterComponent]
public sealed partial class SovietLootBagComponent : Component
{
    /// <summary>
    /// Entity prototype IDs to spawn when the bag is opened.
    /// </summary>
    [DataField]
    public List<EntProtoId> Loot = new();
}
