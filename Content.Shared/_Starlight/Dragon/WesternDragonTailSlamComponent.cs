using Content.Shared.Actions;
using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Dragon;

[RegisterComponent, NetworkedComponent]
public sealed partial class WesternDragonTailSlamComponent : Component;

public sealed partial class WesternDragonTailSlamEvent : InstantActionEvent;
