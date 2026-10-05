using System.Numerics;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.Buckle.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Shared._Starlight.Weapons.Melee;

public sealed partial class NullstarUnboundSystem : EntitySystem
{
    [Dependency] private ActionContainerSystem _container = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NullstarUnboundComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NullstarUnboundComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<NullstarUnboundComponent, NullstarUnboundEvent>(OnRelease);
        SubscribeLocalEvent<InstantActionComponent, ActionAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<NullstarUnboundWaveComponent, MapInitEvent>(OnWaveInit);
    }

    private void OnMapInit(Entity<NullstarUnboundComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.PreviewAction is not { } action)
            return;
        _container.EnsureAction(ent, ref ent.Comp.ActionEntity, action);
        _actions.SetEnabled(ent.Comp.ActionEntity, !ent.Comp.Released);
        Dirty(ent);
    }

    private void OnGetActions(Entity<NullstarUnboundComponent> ent, ref GetItemActionsEvent args)
    {
        if (args.InHands && !ent.Comp.Released)
            args.AddAction(ent.Comp.ActionEntity);
    }

    private bool CanRelease(Entity<NullstarUnboundComponent> blade, EntityUid user)
    {
        if (blade.Comp.Released)
            return false;
        if (_hands.IsHolding(user, blade, out _) &&
            TryComp<WieldableComponent>(blade, out var wieldable) && wieldable.Wielded)
            return true;
        _popup.PopupClient(Loc.GetString("nullstar-must-wield"), user, user);
        return false;
    }

    private void OnAttempt(Entity<InstantActionComponent> ent, ref ActionAttemptEvent args)
    {
        if (args.Cancelled || ent.Comp.Event is not NullstarUnboundEvent)
            return;
        if (!TryComp<ActionComponent>(ent, out var action) || action.Container is not { } blade ||
            !TryComp<NullstarUnboundComponent>(blade, out var component) || !CanRelease((blade, component), args.User))
            args.Cancelled = true;
    }

    private void OnRelease(Entity<NullstarUnboundComponent> ent, ref NullstarUnboundEvent args)
    {
        if (args.Handled || !CanRelease(ent, args.Performer))
            return;
        args.Handled = true;
        if (_net.IsServer)
            TryRelease(ent, args.Performer);
    }

    /// <summary>Server entry point for the future extraction completion; each blade releases only once.</summary>
    public bool TryRelease(Entity<NullstarUnboundComponent> blade, EntityUid user)
    {
        if (!_net.IsServer || !CanRelease(blade, user))
            return false;
        var effect = Spawn(blade.Comp.Effect, _transform.GetMapCoordinates(user));
        var wave = Comp<NullstarUnboundWaveComponent>(effect);
        wave.Caster = user;
        Dirty(effect, wave);
        blade.Comp.Released = true;
        _actions.SetEnabled(blade.Comp.ActionEntity, false);
        Dirty(blade);
        return true;
    }

    private void OnWaveInit(Entity<NullstarUnboundWaveComponent> ent, ref MapInitEvent args)
    {
        if (!_net.IsServer)
            return;
        ent.Comp.StartTime = _timing.CurTime;
        Dirty(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;
        var query = EntityQueryEnumerator<NullstarUnboundWaveComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var wave, out var xform))
        {
            if (wave.LastRadius >= wave.Radius || wave.Radius <= 0 || IsPaused(uid))
                continue;
            var progress = (float) ((_timing.CurTime - wave.StartTime).TotalSeconds / Math.Max(0.01f, wave.ExpansionSeconds));
            var radius = wave.Radius * Math.Clamp(progress, 0f, 1f);
            var origin = _transform.GetMapCoordinates(uid, xform);
            foreach (var target in _lookup.GetEntitiesInRange<MobStateComponent>(xform.Coordinates, radius, LookupFlags.Uncontained))
            {
                if (target.Owner == wave.Caster || wave.HitEntities.Contains(target.Owner) ||
                    !TryComp<PhysicsComponent>(target, out var physics) ||
                    (physics.CollisionLayer & (int) CollisionGroup.GhostImpassable) != 0 ||
                    TryComp<BuckleComponent>(target, out var buckle) && buckle.Buckled)
                    continue;
                var targetXform = Transform(target);
                var delta = _transform.GetMapCoordinates(target, targetXform).Position - origin.Position;
                var distance = delta.Length();
                if (targetXform.Anchored || distance > radius || distance < wave.LastRadius - 0.25f ||
                    !_interaction.InRangeUnobstructed((uid, xform), (target.Owner, targetXform),
                        range: wave.Radius, collisionMask: CollisionGroup.Impassable))
                    continue;
                wave.HitEntities.Add(target.Owner);
                var direction = distance > 0.001f ? delta / distance : Vector2.UnitY;
                // Use normal collision handling; debris is visual only and no direct damage is applied.
                _throwing.TryThrow(target, direction * 4f, baseThrowSpeed: 9f, user: wave.Caster,
                    pushbackRatio: 0f, compensateFriction: true, recoil: false, animated: false, playSound: false, doSpin: false);
            }
            wave.LastRadius = radius;
        }
    }
}
