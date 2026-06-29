using Robust.Shared.GameObjects;

namespace Content.Shared._Starlight.NPC;

/// <summary>
/// When present, an NPC with an <see cref="HTNComponent"/> will instantly set its combat
/// target to whoever deals damage to it — preventing players from safely kiting or
/// attacking from outside the NPC's normal perception range.
/// </summary>
[RegisterComponent]
public sealed partial class NpcOnHitAggroComponent : Component
{
    /// <summary>
    /// If true, only update the target when the NPC has no target assigned yet.
    /// If false (default), always redirect onto the attacker regardless of the current target.
    /// </summary>
    [DataField]
    public bool OnlyIfNoTarget = false;
}
