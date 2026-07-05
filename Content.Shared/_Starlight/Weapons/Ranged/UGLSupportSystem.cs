using Content.Shared._Starlight.ItemSwitch;
using Content.Shared._Starlight.ItemSwitch.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Shared._Starlight.Weapons.Ranged;

/// <summary>
/// M90 GL underbarrel grenade launcher support.
///
/// Responsibilities:
///
/// 1. InteractUsing — intercepts item-use on the gun BEFORE ItemSlotsSystem so that
///    a valid 20mm GL round is loaded quietly through TryBallisticInsert (which plays
///    the insert sound) instead of triggering the rifle-magazine whitelist popup.
///
/// 2. Mode-switch feedback — shows a brief popup so players always know which mode
///    they just entered.
///
/// 3. Gun property patch — sets GunComponent fields (sound, fire-rate, modes, pump)
///    in-place each time the ItemSwitch changes state, then calls RefreshModifiers.
///    Pump=true in grenade mode keeps spent casings in the launcher until Z is pressed.
///
/// 4. Bolt-closed fix — when switching back to rifle mode, sets BoltClosed=true if
///    there is already a round in the gun_chamber slot (avoids forcing the player to
///    re-rack after every mode toggle).
///
/// 5. Container sync — when BallisticAmmoProvider is re-added (grenade mode entered)
///    its Entities list is empty; we sync it from the container so pre-loaded grenades
///    are visible to the gun system.
/// </summary>
public sealed class UGLSupportSystem : EntitySystem
{
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;

    private static readonly ProtoId<TagPrototype> GlTag = "GrenadeM90GL";

    private static readonly SoundSpecifier InsertSnd =
        new SoundPathSpecifier("/Audio/Weapons/Guns/MagIn/batrifle_magin.ogg");
    private static readonly SoundSpecifier RifleSnd =
        new SoundPathSpecifier("/Audio/Weapons/Guns/Gunshots/batrifle.ogg");
    private static readonly SoundSpecifier GrenadeSnd =
        new SoundPathSpecifier("/Audio/Weapons/Guns/Gunshots/grenade_launcher.ogg");

    public override void Initialize()
    {
        base.Initialize();

        // Fire before ItemSlotsSystem so valid GL rounds go through TryBallisticInsert
        // and don't hit the rifle-magazine whitelist popup.
        SubscribeLocalEvent<ItemSwitchComponent, InteractUsingEvent>(OnInteractUsing,
            before: [typeof(ItemSlotsSystem)]);

        SubscribeLocalEvent<ItemSwitchComponent, ItemSwitchedEvent>(OnSwitched);
    }

    // --- Interaction: load a 20mm grenade into the underbarrel ---

    private void OnInteractUsing(Entity<ItemSwitchComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled) return;
        if (!TryComp<BallisticAmmoProviderComponent>(ent, out var ballistic)) return;
        if (!_tags.HasTag(args.Used, GlTag)) return;

        args.Handled = true;

        // suppressInsertionSound=true so we play the sound ourselves and avoid
        // any serialization uncertainty around the BallisticAmmoProvider DataField.
        if (_gun.TryBallisticInsert((ent.Owner, ballistic), args.Used, args.User, suppressInsertionSound: true))
            _audio.PlayPredicted(InsertSnd, ent.Owner, args.User);
        else
            _popup.PopupClient(Loc.GetString("ugl-already-loaded"), ent.Owner, args.User);
    }

    // --- Mode switch: patch Gun + bolt + container sync + popup ---

    private void OnSwitched(Entity<ItemSwitchComponent> ent, ref ItemSwitchedEvent args)
    {
        // 1. Patch GunComponent in-place for the new mode
        if (TryComp<GunComponent>(ent, out var gun))
        {
            switch (args.State)
            {
                case "rifle":
                    gun.FireRate = 5f;
                    gun.ProjectileSpeed = SharedGunSystem.ProjectileSpeed;
                    gun.AvailableModes = SelectiveFire.FullAuto | SelectiveFire.SemiAuto;
                    gun.SelectedMode   = SelectiveFire.FullAuto; // always restore full-auto
                    gun.SoundGunshot = RifleSnd;
                    gun.Pump = false;
                    break;

                case "grenade":
                    gun.FireRate = 1f;
                    gun.ProjectileSpeed = 20f;
                    gun.AvailableModes = SelectiveFire.SemiAuto;
                    gun.SelectedMode = SelectiveFire.SemiAuto;
                    gun.SoundGunshot = GrenadeSnd;
                    gun.Pump = true; // keep spent casing until player presses Z to eject
                    break;
            }

            _gun.RefreshModifiers(ent.Owner);
        }

        // 2. Bolt-closed fix: when returning to rifle mode, close the bolt if there is
        //    already a round chambered so the player doesn't have to re-rack.
        if (args.State == "rifle" &&
            TryComp<ChamberMagazineAmmoProviderComponent>(ent, out var chamber) &&
            _itemSlots.TryGetSlot(ent.Owner, "gun_chamber", out var chamberSlot))
        {
            var hasChamberRound = chamberSlot.Item != null;
            chamber.BoltClosed = hasChamberRound;
            _appearance.SetData(ent.Owner, AmmoVisuals.BoltClosed, hasChamberRound);
            Dirty(ent.Owner, chamber);
        }

        // 3. Sync BallisticAmmoProvider Entities from the container
        if (TryComp<BallisticAmmoProviderComponent>(ent, out var ballistic) &&
            ballistic.Container is { } container)
        {
            foreach (var contained in container.ContainedEntities)
            {
                if (!ballistic.Entities.Contains(contained))
                    ballistic.Entities.Add(contained);
            }
        }

        // 4. Show a brief mode popup to the user
        if (args.User is not { } user) return;
        var key = args.State == "grenade" ? "ugl-mode-grenade" : "ugl-mode-rifle";
        _popup.PopupClient(Loc.GetString(key), ent.Owner, user);
    }
}