using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Starlight.Weapons.MinigunHolster;

/// <summary>
/// Tracks the backpack an M-2 Ironclad minigun is linked to.
/// When the minigun is dropped it snaps back into the backpack's holster slot.
/// Use a screwdriver (5 s) to unlink. Interact the backpack onto the minigun to relink.
/// </summary>
[RegisterComponent]
public sealed partial class MinigunHolsterComponent : Component
{
    /// <summary>The backpack this minigun is currently linked to.</summary>
    [DataField]
    public EntityUid? LinkedBackpack;
}
