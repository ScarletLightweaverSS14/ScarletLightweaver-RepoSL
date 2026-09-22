using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Starlight.Weapons.Ranged;

/// <summary>
/// Adds a loadable secondary weapon to a gun. Select an ammunition prototype, or use the ammo
/// slot's whitelist to accept a family of ammunition. Loaded cartridges supply their own projectile.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AltWeaponryComponent : Component
{
    /// <summary>
    /// Exact ammunition prototype accepted by the slot. Leave blank to use only its whitelist.
    /// A non-null string keeps the in-game View Variables text editor available even when blank.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string Prototype = string.Empty;

    /// <summary>
    /// Firing settings supplied by a hidden gun, using the ordinary Gun and ContainerAmmoProvider components.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public EntProtoId GunPrototype = "WeaponAltWeaponry";

    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public string AmmoContainer = "altweapon";

    /// <summary>
    /// Registered automatically without replacing the host weapon's existing item slots.
    /// The insert verb can select this slot explicitly when the primary weapon shares its caliber.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public ItemSlot AmmoSlot = new()
    {
        Name = "alt-weaponry-slot",
        Swap = false,
    };

    /// <summary>
    /// Internal container for the secondary gun, separate from its ammunition container.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public string Container = "alt_weapon";

    // Persist ownership so saved weapons can reclaim their own containers on load.
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public bool OwnsContainers;

    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? SecondaryGun;

    // Runtime references prevent cleanup from following edited names or replacement containers.
    [ViewVariables]
    public ContainerSlot? OwnedGunContainer;

    [ViewVariables]
    public ItemSlot? OwnedAmmoSlot;

    [ViewVariables]
    public string? InitializationError;
}
