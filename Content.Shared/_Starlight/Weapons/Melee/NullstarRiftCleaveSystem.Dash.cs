using System.Numerics;
using Content.Shared.Buckle.Components;
using Content.Shared.Friction;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Spawners;

namespace Content.Shared._Starlight.Weapons.Melee;

public sealed partial class NullstarRiftCleaveSystem
{
    [Dependency] private RayCastSystem _raycast = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private PullingSystem _pulling = default!;

    private void InitializeDash()
    {
        // Apply the bounded velocity after walking and friction, immediately before collision solving.
        UpdatesAfter.Add(typeof(SharedMoverController));
        UpdatesAfter.Add(typeof(TileFrictionController));
        SubscribeLocalEvent<NullstarCleaveDashComponent, UpdateCanMoveEvent>(OnDashCanMove);
        SubscribeLocalEvent<NullstarCleaveDashComponent, ComponentStartup>(OnDashStartup);
        SubscribeLocalEvent<NullstarCleaveDashComponent, ComponentShutdown>(OnDashShutdown);
    }

    private bool CanStartDash(EntityUid user)
    {
        return !HasComp<NullstarCleaveDashComponent>(user) && _blocker.CanMove(user) &&
               !Transform(user).Anchored && !_containers.IsEntityInContainer(user) &&
               TryComp<PhysicsComponent>(user, out var body) && body.CanCollide &&
               (body.BodyType & (BodyType.Dynamic | BodyType.KinematicController)) != 0 &&
               (!TryComp<BuckleComponent>(user, out var buckle) || !buckle.Buckled);
    }

    private void OnDashCanMove(Entity<NullstarCleaveDashComponent> ent, ref UpdateCanMoveEvent args)
    {
        if (!ent.Comp.Ending)
            args.Cancel();
    }

    private void OnDashStartup(Entity<NullstarCleaveDashComponent> ent, ref ComponentStartup args)
    {
        _blocker.UpdateCanMove(ent);
    }

    private void OnDashShutdown(Entity<NullstarCleaveDashComponent> ent, ref ComponentShutdown args)
    {
        var unexpected = !ent.Comp.Ending;
        ent.Comp.Ending = true;
        _blocker.UpdateCanMove(ent);
        if (_net.IsServer && TryComp<PhysicsComponent>(ent, out var physics))
            PhysicsSystem.SetLinearVelocity(ent, Vector2.Zero, body: physics);
        if (!_net.IsServer || !unexpected)
            return;
        CancelRift(ent.Comp.Rift);
        if (TryComp<NullstarRiftCleaveComponent>(ent.Comp.Weapon, out var blade))
        {
            blade.PendingRift = null;
            blade.LockedUntil = _timing.CurTime;
            Dirty(ent.Comp.Weapon, blade);
        }
    }

    private Vector2 Direction(EntityUid rift)
    {
        var tangent = _transform.GetWorldRotation(rift).ToVec();
        return new Vector2(tangent.Y, -tangent.X);
    }

    /// <summary>Sweep the wielder's actual hard fixtures, including their width at doorways/corners.</summary>
    private float ClearDistance(EntityUid user, Vector2 direction, float requested)
    {
        if (requested <= 0 || !TryComp<FixturesComponent>(user, out var fixtures))
            return 0;
        var distance = requested;
        var xform = Transform(user);
        var filter = new QueryFilter
        {
            IsIgnored = uid => uid == user,
        };
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;
            filter.MaskBits = fixture.CollisionMask;
            filter.LayerBits = fixture.CollisionLayer;
            _raycast.CastShape(xform.MapID, fixture.Shape, PhysicsSystem.GetPhysicsTransform(user),
                direction * requested, filter,
                (FixtureProxy proxy, Vector2 point, Vector2 normal, float fraction, ref RayResult result) =>
                {
                    distance = Math.Min(distance, Math.Max(0, requested * fraction - 0.03f));
                    return fraction;
                });
        }
        return distance;
    }

    private void BeginDash(Entity<NullstarRiftCleaveComponent> blade, Entity<NullstarRiftComponent> rift)
    {
        var user = rift.Comp.Caster;
        // Release grabs through the usual pulling API; never drag a second entity through this move.
        if (TryComp<PullableComponent>(user, out var pulled))
            _pulling.TryStopPull(user, pulled);
        if (TryComp<PullerComponent>(user, out var puller) && puller.Pulling is { } other &&
            TryComp<PullableComponent>(other, out var otherPullable))
            _pulling.TryStopPull(other, otherPullable);

        var dash = AddComp<NullstarCleaveDashComponent>(user);
        dash.Rift = rift;
        dash.Weapon = blade;
        dash.LastPosition = _transform.GetMapCoordinates(user).Position;
        _transform.SetWorldRotation(user, Direction(rift).ToWorldAngle());
        if (TryComp<NullstarCleaveMotionComponent>(rift.Comp.MotionEffect, out var visual))
        {
            visual.Dashing = true;
            visual.PhaseStarted = _timing.CurTime;
            Dirty(rift.Comp.MotionEffect!.Value, visual);
        }
        _audio.PlayPvs(blade.Comp.DashSound, Transform(user).Coordinates);
        if (rift.Comp.DashDistance < 0.03f)
            EndDash((user, dash), strike: true);
    }

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        base.UpdateBeforeSolve(prediction, frameTime);
        if (!_net.IsServer || frameTime <= 0)
            return;
        var query = EntityQueryEnumerator<NullstarCleaveDashComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var dash, out var body, out var xform))
        {
            if (IsPaused(uid))
                continue;
            if (!TryComp<NullstarRiftComponent>(dash.Rift, out var rift) ||
                !TryComp<NullstarRiftCleaveComponent>(dash.Weapon, out var blade) ||
                !CanWieldAndAttack(dash.Weapon, uid) || xform.Anchored || _containers.IsEntityInContainer(uid) ||
                TryComp<BuckleComponent>(uid, out var buckle) && buckle.Buckled)
            {
                EndDash((uid, dash), strike: false);
                continue;
            }
            var origin = _transform.ToMapCoordinates(rift.DashOrigin);
            var position = _transform.GetMapCoordinates(uid, xform);
            var direction = Direction(dash.Rift);
            var displacement = position.Position - origin.Position;
            if (origin.MapId != position.MapId || displacement.Length() > blade.DashRange + 1 ||
                Math.Abs(Vector2.Dot(displacement, new Vector2(-direction.Y, direction.X))) > 1)
            {
                EndDash((uid, dash), strike: false);
                continue;
            }

            var remaining = Math.Max(0, rift.DashDistance - Vector2.Dot(displacement, direction));
            var step = Math.Min(remaining, blade.DashSpeed * frameTime);
            var clear = ClearDistance(uid, direction, step);
            dash.Elapsed += frameTime;
            dash.LastPosition = position.Position;
            dash.FinishAfterStep = clear < step || remaining <= step || dash.Elapsed >= 0.7f;
            PhysicsSystem.SetLinearVelocity(uid, direction * (clear / frameTime), body: body);
            _transform.SetWorldRotation(uid, direction.ToWorldAngle());
        }
    }

    public override void UpdateAfterSolve(bool prediction, float frameTime)
    {
        base.UpdateAfterSolve(prediction, frameTime);
        if (!_net.IsServer)
            return;
        var query = EntityQueryEnumerator<NullstarCleaveDashComponent>();
        while (query.MoveNext(out var uid, out var dash))
        {
            if (IsPaused(uid))
                continue;
            UpdateWake(dash.Rift, uid, finished: false);
            // Contact resolution can also stop a rush (e.g. a door closing during movement).
            if (dash.FinishAfterStep || dash.Elapsed > 0.05f &&
                Vector2.DistanceSquared(_transform.GetMapCoordinates(uid).Position, dash.LastPosition) < 0.0001f)
                EndDash((uid, dash), strike: true);
        }
    }

    private void UpdateWake(EntityUid riftUid, EntityUid user, bool finished)
    {
        if (!TryComp<NullstarRiftComponent>(riftUid, out var rift) ||
            !TryComp<NullstarCleaveMotionComponent>(rift.MotionEffect, out var visual))
            return;
        var origin = _transform.ToMapCoordinates(rift.DashOrigin);
        visual.Travelled = Math.Clamp(Vector2.Dot(_transform.GetMapCoordinates(user).Position - origin.Position,
            Direction(riftUid)), 0, rift.DashDistance);
        visual.Finished = finished;
        if (finished)
        {
            visual.PhaseStarted = _timing.CurTime;
            Comp<TimedDespawnComponent>(rift.MotionEffect!.Value).Lifetime = 0.35f;
        }
        Dirty(rift.MotionEffect!.Value, visual);
    }

    private void EndDash(Entity<NullstarCleaveDashComponent> ent, bool strike)
    {
        var dash = ent.Comp;
        dash.Ending = true;
        RemComp<NullstarCleaveDashComponent>(ent); // Restores movement and clears dash velocity on every exit.
        if (TryComp<NullstarRiftCleaveComponent>(dash.Weapon, out var blade))
        {
            blade.PendingRift = null;
            blade.LockedUntil = _timing.CurTime + (strike ? blade.Recovery : TimeSpan.Zero);
            Dirty(dash.Weapon, blade);
        }
        if (!strike || !TryComp<NullstarRiftComponent>(dash.Rift, out var rift))
        {
            CancelRift(dash.Rift);
            return;
        }

        UpdateWake(dash.Rift, ent, finished: true);
        var position = _transform.GetMapCoordinates(ent);
        var direction = Direction(dash.Rift);
        var center = new MapCoordinates(position.Position + direction * rift.ForwardOffset, position.MapId);
        _transform.SetCoordinates(dash.Rift, _transform.ToCoordinates(Transform(ent).ParentUid, center));
        _transform.SetWorldRotation(dash.Rift, new Angle(new Vector2(-direction.Y, direction.X)));
        rift.Active = true;
        rift.PhaseStarted = _timing.CurTime;
        rift.LastUpdate = 0;
        Comp<TimedDespawnComponent>(dash.Rift).Lifetime = rift.Duration + rift.CloseSeconds;
        Dirty(dash.Rift, rift);
        // Damage, the crescent flash and its loud cut share the same release tick.
        _audio.PlayPvs(rift.ReleaseSound, Transform(dash.Rift).Coordinates);
        ApplyHits((dash.Rift, rift), direct: true);
    }
}
