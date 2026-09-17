using Content.Shared._Starlight.Actions.EntitySystems;
using System.Numerics;
using Content.Shared._Starlight.Actions.Events;
using Content.Shared.ActionBlocker;
using Content.Shared.Movement.Events;
using Content.Shared.Throwing;
using Robust.Shared.GameStates;
using Robust.Shared.Timing;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._Starlight.Dragon;

public sealed partial class WingDashSystem : EntitySystem
{
    [Dependency] private SharedJumpSystem _jump = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WingDashComponent, WingDashEvent>(OnWingDash);
        SubscribeLocalEvent<WingDashComponent, UpdateCanMoveEvent>(OnCanMove);
        SubscribeLocalEvent<WingDashComponent, LandEvent>(OnLand);
        SubscribeLocalEvent<WingDashComponent, StopThrowEvent>(OnStopThrow);
        SubscribeLocalEvent<WingDashComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<WingDashComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnWingDash(Entity<WingDashComponent> ent, ref WingDashEvent args)
    {
        if (args.Handled || ent.Comp.EndTime != null || args.DashSpeed <= 0 || args.DashDistance <= 0)
            return;

        var origin = _transform.GetMapCoordinates(ent);
        var target = _transform.ToMapCoordinates(args.Target);
        var distance = (target.Position - origin.Position).Length();
        if (origin.MapId != target.MapId || distance < 0.1f)
            return;

        var jump = new JumpActionEvent { Performer = ent, Action = args.Action, Target = args.Target };
        if (!_jump.TryJump(ent, args.Target, jump, speed: args.DashSpeed,
                toPointer: true, distance: args.DashDistance))
            return;

        // Flying mobs normally brake and steer while airborne. Let the jump carry the dragon
        // until it lands, with a timeout as well because throws in space never land.
        ent.Comp.EndTime = _timing.CurTime + TimeSpan.FromSeconds(Math.Min(distance, args.DashDistance) / args.DashSpeed);
        Dirty(ent);
        _blocker.UpdateCanMove(ent);
        args.Handled = true;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<WingDashComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.EndTime is { } end && _timing.CurTime >= end)
                EndDash((uid, comp));
        }
    }

    private void EndDash(Entity<WingDashComponent> ent)
    {
        if (ent.Comp.EndTime == null)
            return;
        ent.Comp.EndTime = null;
        Dirty(ent);
        // Space has no landing friction. Stop the dash impulse when its range ends.
        if (!TerminatingOrDeleted(ent) && TryComp<PhysicsComponent>(ent, out var body))
            _physics.SetLinearVelocity(ent, Vector2.Zero, body: body);
        _blocker.UpdateCanMove(ent);
    }

    private void OnCanMove(EntityUid uid, WingDashComponent comp, UpdateCanMoveEvent args)
    {
        if (comp.EndTime > _timing.CurTime)
            args.Cancel();
    }

    private void OnLand(Entity<WingDashComponent> ent, ref LandEvent args) => EndDash(ent);
    private void OnStopThrow(Entity<WingDashComponent> ent, ref StopThrowEvent args) => EndDash(ent);
    private void OnState(Entity<WingDashComponent> ent, ref AfterAutoHandleStateEvent args) => _blocker.UpdateCanMove(ent);
    private void OnShutdown(Entity<WingDashComponent> ent, ref ComponentShutdown args) => EndDash(ent);
}
