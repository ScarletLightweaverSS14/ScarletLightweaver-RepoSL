using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.SCP;

/// <summary>
/// Tracks which player entity this SCP should continuously follow.
/// Set <see cref="Target"/> server-side; the follow system keeps
/// the HTN blackboard updated every tick.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SCPFollowerComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Target;
}
