using Content.Server._Starlight.RTS.Components;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared._Starlight.RTS;
using Content.Shared.Movement.Components;
using Content.Shared.NPC;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.Server._Starlight.RTS;

/// <summary>
/// Runs explicit RTS movement orders through the ordinary NPC pathfinder and movement controller.
/// Units have no autonomous HTN behavior and stop processing steering when their order finishes.
/// </summary>
public sealed partial class RtsUnitSystem : EntitySystem
{
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public const float ArrivalRange = 0.25f;
    public const float BattlefieldMargin = 0.5f;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(NPCSteeringSystem));
        SubscribeLocalEvent<RtsUnitOrderComponent, ComponentShutdown>(OnOrderShutdown);
    }

    /// <summary>
    /// Starts or replaces one move order. Session/team authorization belongs to the commander system;
    /// this also rejects invalid units, destinations outside their battlefield, and station movement.
    /// </summary>
    public bool IssueMove(EntityUid unit, EntityCoordinates target)
    {
        if (TerminatingOrDeleted(unit) ||
            !TryComp<RtsUnitComponent>(unit, out var rtsUnit) ||
            !HasComp<PhysicsComponent>(unit) ||
            !TryComp(unit, out TransformComponent? xform) ||
            xform.MapUid is not { } map ||
            !TryComp<RtsBattlefieldComponent>(map, out var battlefield) ||
            !_transform.IsValid(target))
            return false;

        var mapTarget = _transform.ToMapCoordinates(target);
        if (mapTarget.MapId != xform.MapID)
            return false;

        // Store the order relative to the battlefield, never relative to an arbitrary clicked entity.
        var destination = _transform.ToCoordinates(map, mapTarget);
        var position = destination.Position;
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) ||
            position.X < BattlefieldMargin || position.Y < BattlefieldMargin ||
            position.X > battlefield.Size - BattlefieldMargin ||
            position.Y > battlefield.Size - BattlefieldMargin)
            return false;

        // Starlight removes InputMover from NPCs while they are idle. Restore it only after
        // validating the command; EnsureComp also replaces a mover pending deferred removal.
        EnsureComp<ActiveNPCComponent>(unit);
        EnsureComp<InputMoverComponent>(unit);

        // A previous failed or completed steering component retains its status. Recreate it so
        // replacement commands start with fresh pathfinding, cancellation, and stuck detection.
        _steering.Unregister(unit);
        var steering = _steering.Register(unit, destination);
        steering.Range = ArrivalRange;
        steering.Radius = 0.3f;
        steering.InRangeMaxSpeed = 0.1f;
        EnsureComp<RtsUnitOrderComponent>(unit).Battlefield = map;
        SetState(unit, rtsUnit, RtsUnitState.Moving);
        return true;
    }

    /// <summary>
    /// Cancels movement and returns a unit to idle. Repeated stop commands are harmless.
    /// </summary>
    public void Stop(EntityUid unit)
    {
        if (!TryComp<RtsUnitComponent>(unit, out var rtsUnit))
            return;

        Finish(unit, rtsUnit, RtsUnitState.Idle);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Only commanded units are queried. All path requests and collision avoidance remain in
        // NPCSteeringSystem, which cancels outstanding jobs when steering is removed.
        var query = EntityQueryEnumerator<RtsUnitOrderComponent, RtsUnitComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var order, out var unit, out var xform))
        {
            if (xform.MapUid != order.Battlefield ||
                !HasComp<RtsBattlefieldComponent>(order.Battlefield) ||
                !TryComp<NPCSteeringComponent>(uid, out var steering))
            {
                Finish(uid, unit, RtsUnitState.Blocked);
                continue;
            }

            switch (steering.Status)
            {
                case SteeringStatus.InRange:
                    Finish(uid, unit, RtsUnitState.Idle);
                    break;
                case SteeringStatus.NoPath:
                    Finish(uid, unit, RtsUnitState.Blocked);
                    break;
            }
        }
    }

    private void Finish(EntityUid uid, RtsUnitComponent unit, RtsUnitState state)
    {
        _steering.Unregister(uid);
        RemComp<ActiveNPCComponent>(uid);
        RemComp<RtsUnitOrderComponent>(uid);
        SetState(uid, unit, state);
    }

    private void OnOrderShutdown(Entity<RtsUnitOrderComponent> ent, ref ComponentShutdown args)
    {
        // Deleting an entity shuts down steering and cancels its jobs through the normal lifecycle.
        if (TerminatingOrDeleted(ent.Owner))
            return;

        // Also cancel work if an order is removed by cleanup or administrative tools.
        _steering.Unregister(ent.Owner);
        RemComp<ActiveNPCComponent>(ent.Owner);
        if (TryComp<RtsUnitComponent>(ent.Owner, out var unit))
            SetState(ent.Owner, unit, RtsUnitState.Idle);
    }

    private void SetState(EntityUid uid, RtsUnitComponent unit, RtsUnitState state)
    {
        if (unit.State == state)
            return;

        unit.State = state;
        Dirty(uid, unit);
    }
}
