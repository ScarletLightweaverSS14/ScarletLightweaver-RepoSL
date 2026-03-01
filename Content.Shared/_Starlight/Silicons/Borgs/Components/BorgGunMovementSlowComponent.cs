using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Silicons.Borgs.Components;

/// <summary>
/// When this component is placed on a borg module entity, the borg carrying
/// that module (regardless of whether it is selected) will have its movement
/// speed reduced. Intended for heavy-weapon platforms such as the assault LMG module.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BorgGunMovementSlowComponent : Component
{
    /// <summary>
    /// Multiplier applied to walk speed (1.0 = no change, 0.7 = 30% slower).
    /// </summary>
    [DataField]
    public float WalkModifier = 0.70f;

    /// <summary>
    /// Multiplier applied to sprint speed (1.0 = no change, 0.7 = 30% slower).
    /// </summary>
    [DataField]
    public float SprintModifier = 0.70f;
}
