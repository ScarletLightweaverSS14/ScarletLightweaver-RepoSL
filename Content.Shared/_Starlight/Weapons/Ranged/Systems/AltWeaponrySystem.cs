using Content.Shared.CombatMode;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Input;
using Content.Shared.VentCrawl;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Shared._Starlight.Weapons.Ranged.Systems;

/// <summary>
/// Fires a YAML-configured secondary gun using the normal gun and ammunition systems.
/// </summary>
public sealed partial class AltWeaponrySystem : EntitySystem
{
    [Dependency] private SharedGunSystem _guns = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedCombatModeSystem _combatMode = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private EntityWhitelistSystem _whitelists = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AltWeaponryComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<AltWeaponryComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<AltWeaponryComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<AltWeaponryComponent, ContainerIsInsertingAttemptEvent>(OnInsert);
        CommandBinds.Builder
            .Bind(ContentKeyFunctions.SecondaryWeaponAction, new PointerInputCmdHandler(OnFire))
            .Register<AltWeaponrySystem>();
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<AltWeaponrySystem>();
        base.Shutdown();
    }

    private void OnInit(Entity<AltWeaponryComponent> ent, ref ComponentInit args)
    {
        // Clients receive slots and ownership through component state.
        if (!_netManager.IsServer)
            return;

        var comp = ent.Comp;
        if (string.IsNullOrWhiteSpace(comp.Container) || string.IsNullOrWhiteSpace(comp.AmmoContainer) ||
            comp.Container == comp.AmmoContainer)
        {
            comp.InitializationError = "Alternate gun and ammo containers must have distinct, nonblank names.";
            comp.OwnsContainers = false;
            return;
        }

        _containers.TryGetContainer(ent, comp.Container, out var gunContainer);
        _containers.TryGetContainer(ent, comp.AmmoContainer, out var ammoContainer);
        var hasSlot = TryComp<ItemSlotsComponent>(ent, out var slots) &&
            (_slots.TryGetSlot(ent, comp.AmmoContainer, out _, slots) || _slots.TryGetSlot(ent, comp.Container, out _, slots));
        if ((!comp.OwnsContainers && (gunContainer != null || ammoContainer != null || hasSlot)) ||
            (gunContainer != null && gunContainer is not ContainerSlot) ||
            (ammoContainer != null && ammoContainer is not ContainerSlot))
        {
            comp.InitializationError = "Alternate weapon container names conflict with an existing container or item slot.";
            comp.OwnsContainers = false;
            return;
        }

        comp.OwnedGunContainer = _containers.EnsureContainer<ContainerSlot>(ent, comp.Container);
        _slots.AddItemSlot(ent, comp.AmmoContainer, comp.AmmoSlot);
        comp.OwnedAmmoSlot = comp.AmmoSlot;
        comp.OwnsContainers = true;
        Dirty(ent);
    }

    private void OnMapInit(Entity<AltWeaponryComponent> ent, ref MapInitEvent args)
        => EnsureGun(ent);

    private void EnsureGun(Entity<AltWeaponryComponent> ent)
    {
        if (!_netManager.IsServer || !ent.Comp.OwnsContainers || ent.Comp.OwnedGunContainer is not { } container)
            return;

        // A saved weapon may already contain its secondary gun.
        if (container.ContainedEntities.Count == 0 &&
            TrySpawnInContainer(ent.Comp.GunPrototype, ent, container.ID, out var gun))
        {
            ent.Comp.SecondaryGun = gun;
            Dirty(ent);
        }
    }

    private void OnRemove(Entity<AltWeaponryComponent> ent, ref ComponentRemove args)
    {
        // Slot/container removal is replicated. Removing them on the client too can double-delete ItemSlots.
        if (!_netManager.IsServer || Terminating(ent))
            return;

        if (ent.Comp.OwnedAmmoSlot is { ContainerSlot: { } ammoContainer } slot &&
            TryComp<ItemSlotsComponent>(ent, out var slots) &&
            _slots.TryGetSlot(ent, ammoContainer.ID, out var currentSlot, slots) && ReferenceEquals(slot, currentSlot) &&
            _containers.TryGetContainer(ent, ammoContainer.ID, out var currentAmmo) && ReferenceEquals(ammoContainer, currentAmmo))
        {
            // Container shutdown deletes its contents. Return loaded ammunition first.
            if (ammoContainer.ContainedEntity is { } ammo)
                _containers.Remove(ammo, ammoContainer, force: true);
            _slots.RemoveItemSlot(ent, slot, slots);
        }

        if (ent.Comp.OwnedGunContainer is not { } container ||
            !_containers.TryGetContainer(ent, container.ID, out var currentGun) || !ReferenceEquals(container, currentGun))
            return;

        // Only the gun we spawned may be deleted; an unrelated replacement must survive.
        if (container.ContainedEntity is { } contained && contained != ent.Comp.SecondaryGun)
            _containers.Remove(contained, container, force: true);
        _containers.ShutdownContainer(container);
    }

    private void OnInsert(Entity<AltWeaponryComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (ent.Comp.OwnsContainers && args.Container.ID == ent.Comp.AmmoContainer && !CanLoad(ent, args.EntityUid))
            args.Cancel();
    }

    private bool CanLoad(Entity<AltWeaponryComponent> ent, EntityUid ammo)
    {
        if (!_slots.TryGetSlot(ent, ent.Comp.AmmoContainer, out var slot))
            return false;

        // Unconfigured components accept nothing. A prototype restricts to that round; tags allow families.
        if (!string.IsNullOrWhiteSpace(ent.Comp.Prototype))
        {
            if (MetaData(ammo).EntityPrototype?.ID != ent.Comp.Prototype.Trim())
                return false;
        }
        else if (slot.Whitelist == null)
            return false;

        return !_whitelists.IsWhitelistFail(slot.Whitelist, ammo) &&
               !_whitelists.IsWhitelistPass(slot.Blacklist, ammo);
    }

    private bool OnFire(ICommonSession? session, EntityCoordinates coordinates, EntityUid target)
    {
        if (session?.AttachedEntity is { } user)
            TryFire(user, coordinates);

        // Predicted handlers must still forward the input to the server.
        return false;
    }

    public bool TryFire(EntityUid user, EntityCoordinates coordinates)
    {
        if (!coordinates.IsValid(EntityManager) ||
            !_combatMode.IsInCombatMode(user) ||
            _hands.GetActiveItem(user) is not { } weapon ||
            !TryComp<AltWeaponryComponent>(weapon, out var alternate) ||
            !alternate.OwnsContainers ||
            (TryComp<VentCrawlerComponent>(user, out var crawler) && crawler.InTube) ||
            _transform.ToMapCoordinates(coordinates).MapId != Transform(user).MapID ||
            !_containers.TryGetContainer(weapon, alternate.Container, out var container) ||
            container.ContainedEntities.Count != 1 ||
            container.ContainedEntities[0] != alternate.SecondaryGun ||
            !_slots.TryGetSlot(weapon, alternate.AmmoContainer, out var slot) ||
            (slot.Item is { } ammo && !CanLoad((weapon, alternate), ammo)))
        {
            return false;
        }

        return _guns.TryShootAlternate(user, weapon, container.ContainedEntities[0], alternate.AmmoContainer, coordinates);
    }
}
