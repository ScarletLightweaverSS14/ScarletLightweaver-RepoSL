using Content.Shared._Starlight.Weapons.MinigunHolster;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Tools.Systems;
using Robust.Shared.GameObjects;

namespace Content.Server._Starlight.Weapons.MinigunHolster;

/// <summary>
/// Handles the M-2 Ironclad minigun holster backpack mechanics:
/// • Drop → auto-holster back into linked backpack
/// • Screwdriver on minigun → 5 s delay → unlink
/// • Backpack in hand → click minigun → relink
/// </summary>
public sealed class MinigunHolsterSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedToolSystem _tool = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    private const string HolsterSlotId = "minigun_holster";
    private const string ScrewingQuality = "Screwing";
    private const string BackpackTag = "MinigunAmmoBackpack";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MinigunHolsterComponent, DroppedEvent>(OnDropped);
        SubscribeLocalEvent<MinigunHolsterComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<MinigunHolsterComponent, MinigunUnlinkDoAfterEvent>(OnUnlinkDoAfter);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Drop → snap back to holster
    // ──────────────────────────────────────────────────────────────────────────

    private void OnDropped(Entity<MinigunHolsterComponent> ent, ref DroppedEvent args)
    {
        if (ent.Comp.LinkedBackpack is not { } backpack || !EntityManager.EntityExists(backpack))
            return;

        // Make sure the user still has the backpack in their back slot.
        if (!_inventory.TryGetSlotEntity(args.User, "back", out var worn) || worn != backpack)
            return;

        // Snap back into the holster slot.
        if (_itemSlots.TryInsert(backpack, HolsterSlotId, ent.Owner, user: null, excludeUserAudio: true))
        {
            SetBackpackHolstered(backpack, true);
            _popup.PopupEntity(
                Loc.GetString("minigun-holster-snapped-back"),
                args.User,
                args.User,
                PopupType.Small);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // InteractUsing on minigun
    // ──────────────────────────────────────────────────────────────────────────

    private void OnInteractUsing(Entity<MinigunHolsterComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var used = args.Used;
        var user = args.User;

        // ── Screwdriver → begin unlink ──
        if (ent.Comp.LinkedBackpack != null && _tool.HasQuality(used, ScrewingQuality))
        {
            args.Handled = _tool.UseTool(used, user, ent.Owner, 5f, ScrewingQuality, new MinigunUnlinkDoAfterEvent());
            if (args.Handled)
            {
                _popup.PopupEntity(
                    Loc.GetString("minigun-holster-unlinking"),
                    ent.Owner,
                    user,
                    PopupType.SmallCaution);
            }
            return;
        }

        // ── Backpack → relink ──
        if (ent.Comp.LinkedBackpack == null && _tag.HasTag(used, BackpackTag))
        {
            ent.Comp.LinkedBackpack = used;
            SetBackpackHolstered(used, false);
            args.Handled = true;
            _popup.PopupEntity(
                Loc.GetString("minigun-holster-linked", ("backpack", used)),
                ent.Owner,
                user,
                PopupType.Medium);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Unlink DoAfter result
    // ──────────────────────────────────────────────────────────────────────────

    private void OnUnlinkDoAfter(Entity<MinigunHolsterComponent> ent, ref MinigunUnlinkDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        if (ent.Comp.LinkedBackpack is { } backpack)
            SetBackpackHolstered(backpack, false);

        ent.Comp.LinkedBackpack = null;
        _popup.PopupEntity(
            Loc.GetString("minigun-holster-unlinked"),
            ent.Owner,
            args.User,
            PopupType.Medium);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private void SetBackpackHolstered(EntityUid backpack, bool holstered)
    {
        _appearance.SetData(backpack, MinigunBackpackVisuals.HasMinigun, holstered);
    }
}
