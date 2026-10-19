using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.RTS;

/// <summary>
/// Identifies the team that owns an RTS entity, including on clients displaying selection information.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RtsOwnerComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public RtsTeam Team;
}
