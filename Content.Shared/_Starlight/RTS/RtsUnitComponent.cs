using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.RTS;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RtsUnitComponent : Component
{
    [DataField, AutoNetworkedField]
    public RtsUnitState State;
}

[Serializable, NetSerializable]
public enum RtsUnitState : byte
{
    Idle,
    Moving,
    Blocked,
}
