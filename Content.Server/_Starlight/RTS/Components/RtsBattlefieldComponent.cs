namespace Content.Server._Starlight.RTS.Components;

/// <summary>
/// Owns the map and starting entities for one isolated RTS battlefield.
/// Deleting the map also deletes its headquarters and vegetation through transform parenting.
/// </summary>
[RegisterComponent]
public sealed partial class RtsBattlefieldComponent : Component
{
    [DataField]
    public int Seed;

    [DataField]
    public int Size;

    [DataField]
    public EntityUid BlueHeadquarters;

    [DataField]
    public EntityUid RedHeadquarters;
}
