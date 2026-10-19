namespace Content.Server._Starlight.RTS.Components;

/// <summary>
/// Tracks a unit's active move order. Idle units have neither this component nor NPC steering.
/// </summary>
[RegisterComponent]
public sealed partial class RtsUnitOrderComponent : Component
{
    [ViewVariables]
    public EntityUid Battlefield;
}
