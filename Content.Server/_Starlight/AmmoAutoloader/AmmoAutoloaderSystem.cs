using Content.Server.Power.EntitySystems;
using Content.Server.UserInterface;
using Content.Shared._Starlight.AmmoAutoloader;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Whitelist;
using Content.Shared.Wires;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.AmmoAutoloader;

public sealed class AmmoAutoloaderSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming              _timing         = default!;
    [Dependency] private readonly ItemSlotsSystem          _itemSlots      = default!;
    [Dependency] private readonly SharedContainerSystem    _containers     = default!;
    [Dependency] private readonly SharedInteractionSystem  _interaction    = default!;
    [Dependency] private readonly SharedPopupSystem        _popup          = default!;
    [Dependency] private readonly SharedToolSystem         _tools          = default!;
    [Dependency] private readonly AudioSystem              _audio          = default!;
    [Dependency] private readonly AppearanceSystem         _appearance     = default!;
    [Dependency] private readonly UserInterfaceSystem      _ui             = default!;
    [Dependency] private readonly EntityWhitelistSystem    _whitelist      = default!;
    [Dependency] private readonly IPrototypeManager        _protoManager   = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AmmoAutoloaderComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<AmmoAutoloaderComponent, BoundUIOpenedEvent>(OnUIOpened);
        SubscribeLocalEvent<AmmoAutoloaderComponent, AmmoAutoloaderToggleSide1Message>(OnToggleSide1);
        SubscribeLocalEvent<AmmoAutoloaderComponent, AmmoAutoloaderToggleSide2Message>(OnToggleSide2);
        SubscribeLocalEvent<AmmoAutoloaderComponent, AmmoAutoloaderEjectMag1Message>(OnEjectMag1);
        SubscribeLocalEvent<AmmoAutoloaderComponent, AmmoAutoloaderEjectMag2Message>(OnEjectMag2);
        SubscribeLocalEvent<AmmoAutoloaderComponent, AmmoAutoloaderEjectAmmo1Message>(OnEjectAmmo1);
        SubscribeLocalEvent<AmmoAutoloaderComponent, AmmoAutoloaderEjectAmmo2Message>(OnEjectAmmo2);
        SubscribeLocalEvent<AmmoAutoloaderComponent, EntInsertedIntoContainerMessage>(OnItemInserted);
        SubscribeLocalEvent<AmmoAutoloaderComponent, EntRemovedFromContainerMessage>(OnItemRemoved);
        SubscribeLocalEvent<AmmoAutoloaderComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<AmmoAutoloaderComponent, AmmoAutoloaderUnjamDoAfterEvent>(OnUnjamDoAfter);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────────────────────────────────────

    private void OnInit(Entity<AmmoAutoloaderComponent> ent, ref ComponentInit args)
    {
        RefreshDisplayInfo(ent);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Update loop – runs loading ticks
    // ─────────────────────────────────────────────────────────────────────────────

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<AmmoAutoloaderComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.IsJammed)
                continue;

            if (!this.IsPowered(uid, EntityManager))
                continue;

            var dirty = false;

            if (comp.Side1AutoLoad && now >= comp.NextSide1Load)
            {
                TryLoadSide(uid, comp, side: 1);
                comp.NextSide1Load = now + comp.LoadInterval;
                dirty = true;
            }

            if (comp.Side2AutoLoad && now >= comp.NextSide2Load)
            {
                TryLoadSide(uid, comp, side: 2);
                comp.NextSide2Load = now + comp.LoadInterval;
                dirty = true;
            }

            if (dirty)
            {
                UpdateAppearance(uid, comp);
                UpdateUI(uid, comp);
                Dirty(uid, comp);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Loading logic
    // ─────────────────────────────────────────────────────────────────────────────

    private void TryLoadSide(EntityUid uid, AmmoAutoloaderComponent comp, int side)
    {
        var magSlotId  = side == 1 ? AmmoAutoloaderComponent.MagSlot1Id  : AmmoAutoloaderComponent.MagSlot2Id;
        var ammoSlotId = side == 1 ? AmmoAutoloaderComponent.AmmoSlot1Id : AmmoAutoloaderComponent.AmmoSlot2Id;

        if (!_itemSlots.TryGetSlot(uid, magSlotId,  out var magSlot)  || magSlot.Item  is not { } magUid)
        {
            SetAutoLoad(uid, comp, side, false);
            return;
        }

        if (!_itemSlots.TryGetSlot(uid, ammoSlotId, out var ammoSlot) || ammoSlot.Item is not { } ammoUid)
        {
            SetAutoLoad(uid, comp, side, false);
            return;
        }

        if (!TryComp<BallisticAmmoProviderComponent>(magUid,  out var magComp)  ||
            !TryComp<BallisticAmmoProviderComponent>(ammoUid, out var ammoComp))
        {
            SetAutoLoad(uid, comp, side, false);
            return;
        }

        // Reject ammo boxes in the magazine slot (boxes have a Proto set; mags/speedloaders don't).
        if (magComp.Proto != null)
        {
            _popup.PopupEntity(
                Loc.GetString("ammo-autoloader-mag-slot-wrong-item", ("side", side)), uid);
            SetAutoLoad(uid, comp, side, false);
            return;
        }

        // Mag full?
        if (magComp.Entities.Count + magComp.UnspawnedCount >= magComp.Capacity)
        {
            _popup.PopupEntity(
                Loc.GetString("ammo-autoloader-side-full", ("side", side)), uid);
            SetAutoLoad(uid, comp, side, false);
            return;
        }

        // Ammo box empty?
        if (ammoComp.Entities.Count + ammoComp.UnspawnedCount == 0)
        {
            _popup.PopupEntity(
                Loc.GetString("ammo-autoloader-side-empty", ("side", side)), uid);
            SetAutoLoad(uid, comp, side, false);
            return;
        }

        // ── Compatibility check ────────────────────────────────────────────────────
        if (!IsAmmoCompatible(ammoUid, ammoComp, magUid, magComp))
        {
            Jam(uid, comp, side);
            return;
        }

        // ── Transfer one round ─────────────────────────────────────────────────────
        var ammoList = new List<(EntityUid? Entity, IShootable Shootable)>();
        var takeEv   = new TakeAmmoEvent(1, ammoList, Transform(ammoUid).Coordinates, uid);
        RaiseLocalEvent(ammoUid, takeEv);

        foreach (var (bulletEnt, _) in ammoList)
        {
            if (bulletEnt == null)
                continue;

            // The interaction does the whitelist check + container insert.
            _interaction.InteractUsing(
                uid,
                bulletEnt.Value,
                magUid,
                Transform(magUid).Coordinates,
                checkCanInteract: false,
                checkCanUse:      false);

            if (IsClientSide(bulletEnt.Value))
                Del(bulletEnt.Value);
        }

        _audio.PlayPvs(comp.LoadSound, uid);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Compatibility
    // ─────────────────────────────────────────────────────────────────────────────

    private bool IsAmmoCompatible(
        EntityUid ammoUid, BallisticAmmoProviderComponent ammoComp,
        EntityUid magUid,  BallisticAmmoProviderComponent magComp)
    {
        if (magComp.Whitelist == null)
            return true;

        // Test against an already-spawned bullet if available.
        if (ammoComp.Entities.Count > 0)
            return _whitelist.IsWhitelistPass(magComp.Whitelist, ammoComp.Entities[^1]);

        // Otherwise spawn a temporary one just to check tags.
        if (ammoComp.UnspawnedCount > 0 && ammoComp.Proto != null)
        {
            var test   = SpawnAtPosition(ammoComp.Proto, Transform(ammoUid).Coordinates);
            var passes = _whitelist.IsWhitelistPass(magComp.Whitelist, test);
            Del(test);
            return passes;
        }

        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Jam / Unjam
    // ─────────────────────────────────────────────────────────────────────────────

    private void Jam(EntityUid uid, AmmoAutoloaderComponent comp, int side)
    {
        comp.IsJammed    = true;
        comp.JammedSide  = side;
        comp.Side1AutoLoad = false;
        comp.Side2AutoLoad = false;

        _audio.PlayPvs(comp.JamSound, uid);
        _popup.PopupEntity(
            Loc.GetString("ammo-autoloader-jammed", ("side", side)), uid, PopupType.MediumCaution);

        UpdateAppearance(uid, comp);
        UpdateUI(uid, comp);
        Dirty(uid, comp);
    }

    private void OnInteractUsing(Entity<AmmoAutoloaderComponent> ent, ref InteractUsingEvent args)
    {
        if (!ent.Comp.IsJammed || args.Handled)
            return;

        var panel = CompOrNull<WiresPanelComponent>(ent);

        // Panel closed – they need a screwdriver first.
        if (panel == null || !panel.Open)
        {
            if (_tools.HasQuality(args.Used, "Prying"))
            {
                _popup.PopupEntity(
                    Loc.GetString("ammo-autoloader-need-panel-open"), ent, args.User, PopupType.Small);
                args.Handled = true;
            }
            return;
        }

        // Panel open – accept a crowbar.
        if (_tools.HasQuality(args.Used, "Prying"))
        {
            _tools.UseTool(args.Used, args.User, ent, 2f, "Prying",
                new AmmoAutoloaderUnjamDoAfterEvent());
            args.Handled = true;
        }
    }

    private void OnUnjamDoAfter(Entity<AmmoAutoloaderComponent> ent, ref AmmoAutoloaderUnjamDoAfterEvent args)
    {
        if (args.Cancelled || !ent.Comp.IsJammed)
            return;

        // Eject the offending ammo box so the player must fix it.
        var badAmmoSlotId = ent.Comp.JammedSide == 1
            ? AmmoAutoloaderComponent.AmmoSlot1Id
            : AmmoAutoloaderComponent.AmmoSlot2Id;

        if (_itemSlots.TryGetSlot(ent, badAmmoSlotId, out var badSlot))
            _itemSlots.TryEject(ent, badSlot, args.User, out _);

        ent.Comp.IsJammed   = false;
        ent.Comp.JammedSide = 0;

        _audio.PlayPvs(ent.Comp.UnjamSound, ent);
        _popup.PopupEntity(
            Loc.GetString("ammo-autoloader-unjammed"), ent, args.User, PopupType.Small);

        UpdateAppearance(ent, ent.Comp);
        UpdateUI(ent, ent.Comp);
        Dirty(ent, ent.Comp);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Slot change handlers – refresh display info
    // ─────────────────────────────────────────────────────────────────────────────

    private void OnItemInserted(Entity<AmmoAutoloaderComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        RefreshDisplayInfo(ent);
        UpdateAppearance(ent, ent.Comp);
        UpdateUI(ent, ent.Comp);
        Dirty(ent, ent.Comp);
    }

    private void OnItemRemoved(Entity<AmmoAutoloaderComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        RefreshDisplayInfo(ent);
        UpdateAppearance(ent, ent.Comp);
        UpdateUI(ent, ent.Comp);
        Dirty(ent, ent.Comp);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  UI
    // ─────────────────────────────────────────────────────────────────────────────

    private void OnUIOpened(Entity<AmmoAutoloaderComponent> ent, ref BoundUIOpenedEvent args)
    {
        RefreshDisplayInfo(ent);
        UpdateUI(ent, ent.Comp);
    }

    private void OnToggleSide1(Entity<AmmoAutoloaderComponent> ent, ref AmmoAutoloaderToggleSide1Message args)
    {
        if (ent.Comp.IsJammed)
            return;
        SetAutoLoad(ent, ent.Comp, 1, !ent.Comp.Side1AutoLoad);
        UpdateAppearance(ent, ent.Comp);
        UpdateUI(ent, ent.Comp);
        Dirty(ent, ent.Comp);
    }

    private void OnToggleSide2(Entity<AmmoAutoloaderComponent> ent, ref AmmoAutoloaderToggleSide2Message args)
    {
        if (ent.Comp.IsJammed)
            return;
        SetAutoLoad(ent, ent.Comp, 2, !ent.Comp.Side2AutoLoad);
        UpdateAppearance(ent, ent.Comp);
        UpdateUI(ent, ent.Comp);
        Dirty(ent, ent.Comp);
    }

    private void OnEjectMag1(Entity<AmmoAutoloaderComponent> ent, ref AmmoAutoloaderEjectMag1Message args)
        => EjectSlot(ent, AmmoAutoloaderComponent.MagSlot1Id, args.Actor);

    private void OnEjectMag2(Entity<AmmoAutoloaderComponent> ent, ref AmmoAutoloaderEjectMag2Message args)
        => EjectSlot(ent, AmmoAutoloaderComponent.MagSlot2Id, args.Actor);

    private void OnEjectAmmo1(Entity<AmmoAutoloaderComponent> ent, ref AmmoAutoloaderEjectAmmo1Message args)
        => EjectSlot(ent, AmmoAutoloaderComponent.AmmoSlot1Id, args.Actor);

    private void OnEjectAmmo2(Entity<AmmoAutoloaderComponent> ent, ref AmmoAutoloaderEjectAmmo2Message args)
        => EjectSlot(ent, AmmoAutoloaderComponent.AmmoSlot2Id, args.Actor);

    private void EjectSlot(Entity<AmmoAutoloaderComponent> ent, string slotId, EntityUid? user)
    {
        if (!_itemSlots.TryGetSlot(ent, slotId, out var slot) || slot.Item == null)
            return;

        // Stop auto-load on the affected side so we don't race with ejection.
        var side = slotId.Contains("1") ? 1 : 2;
        SetAutoLoad(ent, ent.Comp, side, false);

        _itemSlots.TryEject(ent, slot, user, out _);

        UpdateAppearance(ent, ent.Comp);
        UpdateUI(ent, ent.Comp);
        Dirty(ent, ent.Comp);
    }

    private void UpdateUI(EntityUid uid, AmmoAutoloaderComponent comp)
    {
        var side1HasMag  = GetSlotItem(uid, AmmoAutoloaderComponent.MagSlot1Id)  != null;
        var side2HasMag  = GetSlotItem(uid, AmmoAutoloaderComponent.MagSlot2Id)  != null;
        var side1HasAmmo = GetSlotItem(uid, AmmoAutoloaderComponent.AmmoSlot1Id) != null;
        var side2HasAmmo = GetSlotItem(uid, AmmoAutoloaderComponent.AmmoSlot2Id) != null;

        _ui.SetUiState(uid, AmmoAutoloaderUiKey.Key, new AmmoAutoloaderBuiState(
            comp.IsJammed, comp.JammedSide,
            comp.Side1AutoLoad, comp.Side2AutoLoad,
            comp.Side1MagName,  comp.Side2MagName,
            comp.Side1AmmoName, comp.Side2AmmoName,
            comp.Side1Compatible, comp.Side2Compatible,
            side1HasMag,  side2HasMag,
            side1HasAmmo, side2HasAmmo,
            comp.Side1MagEntity, comp.Side2MagEntity));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private void SetAutoLoad(EntityUid uid, AmmoAutoloaderComponent comp, int side, bool value)
    {
        if (side == 1) comp.Side1AutoLoad = value;
        else           comp.Side2AutoLoad = value;
    }

    private EntityUid? GetSlotItem(EntityUid uid, string slotId)
    {
        if (_itemSlots.TryGetSlot(uid, slotId, out var slot))
            return slot.Item;
        return null;
    }

    private void RefreshDisplayInfo(Entity<AmmoAutoloaderComponent> ent)
    {
        RefreshSideInfo(ent, 1);
        RefreshSideInfo(ent, 2);
    }

    private void RefreshSideInfo(Entity<AmmoAutoloaderComponent> ent, int side)
    {
        var magSlotId  = side == 1 ? AmmoAutoloaderComponent.MagSlot1Id  : AmmoAutoloaderComponent.MagSlot2Id;
        var ammoSlotId = side == 1 ? AmmoAutoloaderComponent.AmmoSlot1Id : AmmoAutoloaderComponent.AmmoSlot2Id;

        EntityUid? magUid  = GetSlotItem(ent, magSlotId);
        EntityUid? ammoUid = GetSlotItem(ent, ammoSlotId);

        string? magName  = magUid  != null ? MetaData(magUid.Value).EntityName  : null;
        string? ammoName = ammoUid != null ? MetaData(ammoUid.Value).EntityName : null;

        bool compatible = false;
        if (magUid != null && ammoUid != null &&
            TryComp<BallisticAmmoProviderComponent>(magUid.Value,  out var magComp) &&
            TryComp<BallisticAmmoProviderComponent>(ammoUid.Value, out var ammoComp))
        {
            compatible = IsAmmoCompatible(ammoUid.Value, ammoComp, magUid.Value, magComp);
        }

        NetEntity? magNetEnt = magUid != null ? GetNetEntity(magUid.Value) : null;

        if (side == 1)
        {
            ent.Comp.Side1MagName    = magName;
            ent.Comp.Side1AmmoName   = ammoName;
            ent.Comp.Side1Compatible = compatible;
            ent.Comp.Side1MagEntity  = magNetEnt;
        }
        else
        {
            ent.Comp.Side2MagName    = magName;
            ent.Comp.Side2AmmoName   = ammoName;
            ent.Comp.Side2Compatible = compatible;
            ent.Comp.Side2MagEntity  = magNetEnt;
        }
    }

    private void UpdateAppearance(EntityUid uid, AmmoAutoloaderComponent comp)
    {
        var isRunning = !comp.IsJammed && (comp.Side1AutoLoad || comp.Side2AutoLoad);
        _appearance.SetData(uid, AmmoAutoloaderVisuals.IsRunning, isRunning);
        _appearance.SetData(uid, AmmoAutoloaderVisuals.IsJammed,  comp.IsJammed);
    }
}

// ─── DoAfter event ────────────────────────────────────────────────────────────────
[Serializable]
public sealed partial class AmmoAutoloaderUnjamDoAfterEvent : SimpleDoAfterEvent;
