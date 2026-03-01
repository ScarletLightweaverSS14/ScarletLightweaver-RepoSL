using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Starlight.Weapons.AmmoCycler;

/// <summary>
/// Adds a right-click "Cycle ammo" verb to weapons carrying
/// <see cref="BorgAmmoCyclerComponent"/>. On activation the current virtual
/// ammo is flushed, the proto is swapped to the next type, and the launcher
/// is refilled to its full capacity instantly.
/// </summary>
public sealed partial class BorgAmmoCyclerSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BorgAmmoCyclerComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
    }

    private void OnGetVerbs(Entity<BorgAmmoCyclerComponent> gun, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        if (gun.Comp.Protos.Count < 2)
            return;

        var nextIndex = (gun.Comp.CurrentIndex + 1) % gun.Comp.Protos.Count;
        var nextProto = gun.Comp.Protos[nextIndex];

        // Resolve a human-readable name for the next ammo type.
        var nextName = _proto.TryIndex<EntityPrototype>(nextProto, out var entProto)
            ? entProto.Name
            : nextProto.ToString();

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("borg-ammo-cycle-verb", ("next", nextName)),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/refresh.svg.192dpi.png")),
            Priority = 5,
            Act = () => CycleAmmo(gun, user)
        });
    }

    private void CycleAmmo(Entity<BorgAmmoCyclerComponent> gun, EntityUid user)
    {
        if (gun.Comp.Protos.Count < 2)
            return;

        gun.Comp.CurrentIndex = (gun.Comp.CurrentIndex + 1) % gun.Comp.Protos.Count;
        var newProto = gun.Comp.Protos[gun.Comp.CurrentIndex];

        // Delegate field mutation to SharedGunSystem via event (bypasses [Access] restriction).
        RaiseLocalEvent(gun.Owner, new BorgAmmoSwapEvent(newProto));

        Dirty(gun);

        var newName = _proto.TryIndex<EntityPrototype>(newProto, out var ep)
            ? ep.Name
            : newProto.ToString();

        _popup.PopupEntity(
            Loc.GetString("borg-ammo-cycle-loaded", ("type", newName)),
            gun.Owner,
            user,
            PopupType.Medium);
    }
}
