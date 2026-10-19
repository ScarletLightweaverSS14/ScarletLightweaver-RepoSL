using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.RTS;

/// <summary>Enables RTS controls only while a player possesses this dedicated camera.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RtsCommanderComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Battlefield;

    [DataField, AutoNetworkedField]
    public RtsTeam Team;

    [DataField, AutoNetworkedField]
    public int BattlefieldSize;
}
