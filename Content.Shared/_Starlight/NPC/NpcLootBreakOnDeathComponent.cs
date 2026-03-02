using Robust.Shared.GameStates;

namespace Content.Shared.Starlight.NPC;

/// <summary>
/// When added to an NPC, causes all items held in their hands at the moment of death to have a
/// <see cref="Chance"/> probability of becoming broken (renamed + stripped of functionality).
/// </summary>
[RegisterComponent]
[NetworkedComponent]
public sealed partial class NpcLootBreakOnDeathComponent : Component
{
    /// <summary>
    /// Probability (0–1) that each held item is broken when the NPC dies.
    /// </summary>
    [DataField]
    public float Chance = 0.85f;
}
