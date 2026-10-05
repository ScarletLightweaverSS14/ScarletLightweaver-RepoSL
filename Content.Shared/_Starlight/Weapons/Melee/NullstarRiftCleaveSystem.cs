using System.Numerics;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Shared._Starlight.Weapons.Melee;

public sealed partial class NullstarRiftCleaveSystem : VirtualController
{
    [Dependency] private ActionContainerSystem _container = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
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
        InitializeDash();
        base.Initialize();
        SubscribeLocalEvent<NullstarRiftCleaveComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NullstarRiftCleaveComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<NullstarRiftCleaveComponent, NullstarRiftCleaveEvent>(OnCast);
        SubscribeLocalEvent<NullstarRiftCleaveComponent, NullstarRiftCleaveDoAfterEvent>(OnCompleted);
        SubscribeLocalEvent<NullstarRiftCleaveComponent, DoAfterAttemptEvent<NullstarRiftCleaveDoAfterEvent>>(OnWindupCheck);
        SubscribeLocalEvent<NullstarRiftCleaveComponent, AttemptMeleeEvent>(OnMeleeAttempt);
        SubscribeLocalEvent<NullstarRiftCleaveComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<NullstarRiftCleaveActionComponent, ActionAttemptEvent>(OnActionAttempt);
    }

    private void OnMapInit(Entity<NullstarRiftCleaveComponent> ent, ref MapInitEvent args)
    {
        _container.EnsureAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
        Dirty(ent);
    }

    private void OnGetActions(Entity<NullstarRiftCleaveComponent> ent, ref GetItemActionsEvent args)
    {
        if (args.InHands)
            args.AddAction(ent.Comp.ActionEntity);
    }

    private bool CanWieldAndAttack(EntityUid blade, EntityUid user)
    {
        return _hands.IsHolding(user, blade, out _) &&
               TryComp<WieldableComponent>(blade, out var wieldable) && wieldable.Wielded &&
               _blocker.CanInteract(user, blade) && _blocker.CanAttack(user);
    }

    private void OnActionAttempt(Entity<NullstarRiftCleaveActionComponent> ent, ref ActionAttemptEvent args)
    {
        if (args.Cancelled)
            return;
        if (!TryComp<ActionComponent>(ent, out var action) || action.Container is not { } blade ||
            !TryComp<NullstarRiftCleaveComponent>(blade, out var cleave) ||
            cleave.LockedUntil > _timing.CurTime || !CanWieldAndAttack(blade, args.User) || !CanStartDash(args.User))
        {
            args.Cancelled = true;
            _popup.PopupClient(Loc.GetString("nullstar-cleave-cannot-cast"), args.User, args.User);
        }
    }

    private void OnMeleeAttempt(Entity<NullstarRiftCleaveComponent> ent, ref AttemptMeleeEvent args)
    {
        if (ent.Comp.LockedUntil > _timing.CurTime)
            args.Cancelled = true;
    }

    private void OnCast(Entity<NullstarRiftCleaveComponent> ent, ref NullstarRiftCleaveEvent args)
    {
        if (args.Handled || ent.Comp.LockedUntil > _timing.CurTime || !CanWieldAndAttack(ent, args.Performer) || !CanStartDash(args.Performer))
            return;
        var origin = _transform.GetMapCoordinates(args.Performer);
        var target = _transform.ToMapCoordinates(args.Target);
        var direction = target.Position - origin.Position;
        if (origin.MapId != target.MapId || !float.IsFinite(direction.LengthSquared()) || direction.LengthSquared() < 0.01f)
            return;
        if (!_net.IsServer)
        {
            args.Handled = true;
            return;
        }

        var doAfter = new DoAfterArgs(EntityManager, args.Performer, ent.Comp.Windup,
            new NullstarRiftCleaveDoAfterEvent(), ent, used: ent)
        {
            NeedHand = true,
            BreakOnMove = true,
            MovementThreshold = 0.1f,
            BreakOnDamage = true,
            BreakOnHandChange = true,
            AttemptFrequency = AttemptFrequency.EveryTick,
            CancelDuplicate = false,
        };
        if (!_doAfter.TryStartDoAfter(doAfter, out var id))
            return;

        direction = Vector2.Normalize(direction);
        var rift = Spawn(ent.Comp.Rift, origin);
        var comp = Comp<NullstarRiftComponent>(rift);
        comp.Caster = args.Performer;
        comp.Weapon = ent;
        comp.WindupSeconds = (float) ent.Comp.Windup.TotalSeconds;
        comp.PhaseStarted = _timing.CurTime;
        comp.DashOrigin = Transform(args.Performer).Coordinates;
        // Stop short of the cursor so the blade, not the wielder, reaches the aimed point.
        comp.DashDistance = Math.Clamp((target.Position - origin.Position).Length() - comp.ForwardOffset, 0, ent.Comp.DashRange);
        comp.DashDistance = ClearDistance(args.Performer, direction, comp.DashDistance);
        Comp<TimedDespawnComponent>(rift).Lifetime = comp.WindupSeconds + 2f;
        var center = new MapCoordinates(origin.Position + direction * (comp.DashDistance + comp.ForwardOffset), origin.MapId);
        _transform.SetCoordinates(rift, _transform.ToCoordinates(Transform(args.Performer).ParentUid, center));
        // The authored strip lies on sprite X, so use the ordinary Cartesian angle, not the south-facing convention.
        _transform.SetWorldRotation(rift, new Angle(new Vector2(-direction.Y, direction.X)));
        var motion = Spawn(ent.Comp.MotionEffect, origin);
        comp.MotionEffect = motion;
        _transform.SetCoordinates(motion, comp.DashOrigin);
        _transform.SetWorldRotation(motion, new Angle(direction));
        var visual = Comp<NullstarCleaveMotionComponent>(motion);
        visual.Distance = comp.DashDistance;
        visual.WindupSeconds = comp.WindupSeconds;
        visual.PhaseStarted = _timing.CurTime;
        Comp<TimedDespawnComponent>(motion).Lifetime = comp.WindupSeconds + 2f;
        Dirty(motion, visual);
        Dirty(rift, comp);
        ent.Comp.PendingDoAfter = id;
        ent.Comp.PendingRift = rift;
        ent.Comp.LockedUntil = _timing.CurTime + ent.Comp.Windup + TimeSpan.FromSeconds(1) + ent.Comp.Recovery;
        Dirty(ent);
        args.Handled = true; // Start the blade-owned eight-second cooldown, including interrupted attempts.
    }

    private void OnWindupCheck(Entity<NullstarRiftCleaveComponent> ent, ref DoAfterAttemptEvent<NullstarRiftCleaveDoAfterEvent> args)
    {
        if (!CanWieldAndAttack(ent, args.DoAfter.Args.User) || !CanStartDash(args.DoAfter.Args.User))
            args.Cancel();
    }

    private void OnCompleted(Entity<NullstarRiftCleaveComponent> ent, ref NullstarRiftCleaveDoAfterEvent args)
    {
        if (!_net.IsServer || args.Handled)
            return;
        var rift = ent.Comp.PendingRift;
        ent.Comp.PendingDoAfter = null;
        args.Handled = true;
        if (args.Cancelled || !CanWieldAndAttack(ent, args.User) || !CanStartDash(args.User) ||
            rift is not { } uid || !TryComp<NullstarRiftComponent>(uid, out var comp))
        {
            CancelRift(rift);
            ent.Comp.PendingRift = null;
            ent.Comp.LockedUntil = _timing.CurTime;
            Dirty(ent);
            return;
        }

        BeginDash(ent, (uid, comp));
    }

    private void OnShutdown(Entity<NullstarRiftCleaveComponent> ent, ref ComponentShutdown args)
    {
        if (!_net.IsServer)
            return;
        if (TryComp<NullstarRiftComponent>(ent.Comp.PendingRift, out var rift) &&
            TryComp<NullstarCleaveDashComponent>(rift.Caster, out var dash) && dash.Weapon == ent.Owner)
            EndDash((rift.Caster, dash), strike: false);
        CancelRift(ent.Comp.PendingRift);
        _doAfter.Cancel(ent.Comp.PendingDoAfter);
    }

    private void CancelRift(EntityUid? uid)
    {
        if (TryComp<NullstarRiftComponent>(uid, out var rift))
            QueueDel(rift.MotionEffect);
        QueueDel(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;
        var query = EntityQueryEnumerator<NullstarRiftComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var elapsed = (float) (_timing.CurTime - comp.PhaseStarted).TotalSeconds;
            if (!comp.Active || IsPaused(uid) || elapsed >= comp.Duration || elapsed < comp.LastUpdate + 0.1f)
                continue;
            comp.LastUpdate = elapsed;
            if (!Exists(comp.Caster))
            {
                QueueDel(uid);
                continue;
            }
            ApplyHits((uid, comp), direct: false);
        }
    }

    private void ApplyHits(Entity<NullstarRiftComponent> ent, bool direct)
    {
        var comp = ent.Comp;
        var xform = Transform(ent);
        var center = _transform.GetMapCoordinates(ent, xform);
        var tangent = _transform.GetWorldRotation(xform).ToVec();
        var normal = new Vector2(tangent.Y, -tangent.X);
        var origin = new MapCoordinates(center.Position - normal * comp.ForwardOffset, center.MapId);
        var width = direct ? comp.Width : comp.ResidualWidth;
        var range = MathF.Sqrt(comp.Length * comp.Length + width * width) / 2f;
        var elapsed = (float) (_timing.CurTime - comp.PhaseStarted).TotalSeconds;
        foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(xform.Coordinates, range, LookupFlags.Uncontained))
        {
            if (mob.Owner == comp.Caster || !TryComp<PhysicsComponent>(mob, out var physics) ||
                (physics.CollisionLayer & (int) CollisionGroup.GhostImpassable) != 0 ||
                comp.LastHit.TryGetValue(mob, out var last) && elapsed < last + comp.HitInterval.TotalSeconds)
                continue;
            var targetXform = Transform(mob);
            var position = _transform.GetMapCoordinates(mob, targetXform);
            var delta = position.Position - center.Position;
            if (position.MapId != center.MapId || Math.Abs(Vector2.Dot(delta, tangent)) > comp.Length / 2f ||
                Math.Abs(Vector2.Dot(delta, normal)) > width / 2f || !_blocker.CanAttack(comp.Caster, mob) ||
                !_interaction.InRangeUnobstructed(origin, position, range: 0,
                    collisionMask: CollisionGroup.Impassable | CollisionGroup.HighImpassable,
                    predicate: ignored => ignored == mob.Owner || ignored == comp.Caster || ignored == ent.Owner))
                continue;
            comp.LastHit[mob] = elapsed;
            _damage.TryChangeDamage(mob.Owner, direct ? comp.DirectDamage : comp.ResidualDamage, origin: comp.Caster);
            if (direct || comp.Pushed.Contains(mob) || targetXform.Anchored ||
                TryComp<BuckleComponent>(mob, out var buckle) && buckle.Buckled)
                continue;
            comp.Pushed.Add(mob);
            var push = normal * (Vector2.Dot(delta, normal) >= 0 ? 1f : -1f);
            _throwing.TryThrow(mob, push * comp.PushDistance, baseThrowSpeed: 4f, user: comp.Caster,
                pushbackRatio: 0f, compensateFriction: true, recoil: false, animated: false, playSound: false, doSpin: false);
        }
    }
}
