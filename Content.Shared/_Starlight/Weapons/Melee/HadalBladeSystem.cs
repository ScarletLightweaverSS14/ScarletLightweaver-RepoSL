using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._Starlight.Weapons.Melee;

public sealed partial class HadalBladeSystem : EntitySystem
{
    [Dependency] private ActionContainerSystem _actionContainer = default!;
    [Dependency] private SharedGunSystem _guns = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HadalBladeComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<HadalBladeComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<HadalBladeComponent, HadalCrushEvent>(OnCrush);
        SubscribeLocalEvent<WorldTargetActionComponent, ActionAttemptEvent>(OnActionAttempt);
    }

    private void OnMapInit(Entity<HadalBladeComponent> ent, ref MapInitEvent args)
    {
        _actionContainer.EnsureAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
        Dirty(ent);
    }

    private void OnGetActions(Entity<HadalBladeComponent> ent, ref GetItemActionsEvent args)
    {
        if (args.InHands)
            args.AddAction(ent.Comp.ActionEntity);
    }

    private bool CanCrush(EntityUid blade, EntityUid user)
    {
        if (_hands.IsHolding(user, blade, out _) &&
            TryComp<WieldableComponent>(blade, out var wieldable) && wieldable.Wielded)
            return true;

        _popup.PopupClient(Loc.GetString("hadal-blade-must-wield"), user, user);
        return false;
    }

    private void OnActionAttempt(Entity<WorldTargetActionComponent> ent, ref ActionAttemptEvent args)
    {
        if (args.Cancelled || ent.Comp.Event is not HadalCrushEvent)
            return;

        // Check before the windup as well as after it. The action belongs to the blade,
        // so its cooldown survives dropping, holstering and passing it to another user.
        if (!TryComp<ActionComponent>(ent, out var action) ||
            action.Container is not { } blade ||
            !HasComp<HadalBladeComponent>(blade) || !CanCrush(blade, args.User))
            args.Cancelled = true;
    }

    private void OnCrush(Entity<HadalBladeComponent> ent, ref HadalCrushEvent args)
    {
        if (args.Handled || !CanCrush(ent, args.Performer))
            return;

        var origin = _transform.GetMapCoordinates(args.Performer);
        var target = _transform.ToMapCoordinates(args.Target);
        var direction = target.Position - origin.Position;
        if (origin.MapId != target.MapId || direction.LengthSquared() < 0.0001f ||
            ent.Comp.ProjectileCount <= 0)
            return;

        // Predict sound and cooldown, but create the physical projectiles only on the server.
        args.Handled = true;
        if (!_net.IsServer)
            return;

        var velocity = _physics.GetMapLinearVelocity(args.Performer);
        var spread = ent.Comp.ProjectileCount > 1 ? MathHelper.DegreesToRadians(ent.Comp.Spread) : 0f;
        var step = ent.Comp.ProjectileCount > 1 ? spread / (ent.Comp.ProjectileCount - 1) : 0f;
        var start = direction.ToWorldAngle() - spread / 2;

        for (var i = 0; i < ent.Comp.ProjectileCount; i++)
        {
            var projectile = Spawn(ent.Comp.Projectile, origin);
            var shotDirection = (start + step * i).ToWorldVec();
            _guns.ShootProjectile(projectile, shotDirection, velocity, ent, args.Performer, ent.Comp.ProjectileSpeed);
        }
    }
}
