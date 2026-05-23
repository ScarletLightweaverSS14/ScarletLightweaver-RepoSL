using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.Zombies;

/// <summary>
/// Fired when a zombie activates the "Undergo Grotesque Mutation" instant action.
/// </summary>
public sealed partial class ZombieEvolveActionEvent : InstantActionEvent { }

/// <summary>
/// Fired when the mutation DoAfter completes successfully.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class ZombieEvolveDoAfterEvent : SimpleDoAfterEvent { }
