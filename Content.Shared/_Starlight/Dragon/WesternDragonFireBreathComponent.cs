using Content.Shared.Actions;
using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Dragon;

[RegisterComponent, NetworkedComponent]
public sealed partial class WesternDragonFireBreathComponent : Component;

public sealed partial class WesternDragonFireBreathEvent : WorldTargetActionEvent;
