// 🌟Starlight🌟
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.Silicons.Borgs.BorgFortify;

/// <summary>Fired when a welder repair DoAfter completes on a borg with a broken Fortify shield.</summary>
[Serializable, NetSerializable]
public sealed partial class BorgFortifyRepairDoAfterEvent : SimpleDoAfterEvent;
