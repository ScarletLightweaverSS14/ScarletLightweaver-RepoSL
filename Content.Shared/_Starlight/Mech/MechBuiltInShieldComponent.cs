using Robust.Shared.Prototypes;

namespace Content.Shared.Mech;

/// <summary>
/// Added to a mech to indicate it has a built-in energy shield (e.g. Durand turtle shield).
/// Provides the toggle action to the pilot and drains mech reactor energy on use and damage.
/// </summary>
[RegisterComponent]
public sealed partial class MechBuiltInShieldComponent : Component
{
    [DataField]
    public EntProtoId ShieldAction = "ActionToggleDome";

    [DataField]
    public EntityUid? ShieldActionEntity;

    /// <summary>
    /// Mech energy drained per second while the shield is active (passive drain).
    /// </summary>
    [DataField]
    public float PassiveDrainRate = 1f;

    /// <summary>
    /// Mech energy drained per point of damage the shield absorbs.
    /// </summary>
    [DataField]
    public float DamageEnergyDrain = 15f;

    /// <summary>
    /// Speed multiplier applied to the mech while the shield is active.
    /// 0.15 ≈ nearly halted.
    /// </summary>
    [DataField]
    public float ShieldSpeedMultiplier = 0.15f;

    /// Runtime-only: tracks whether the shield was active last tick so we can trigger a speed refresh on transition.
    public bool ShieldWasActive;
}
