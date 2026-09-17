using System.Linq;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared.Devour;
using Content.Shared.Devour.Components;
using Content.Shared.DoAfter;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC;
using Content.Shared.Whitelist;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Containers;

namespace Content.Server._Starlight.Dragon;

public sealed partial class WesternDragonBossSystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    private void InitializeDevour()
    {
        SubscribeLocalEvent<WesternDragonBossComponent, MapInitEvent>(OnBossMapInit);
        SubscribeLocalEvent<WesternDragonBossComponent, DevourDoAfterEvent>(OnDevourFinished,
            before: [typeof(DevourSystem)]);
    }

    private void OnBossMapInit(EntityUid uid, WesternDragonBossComponent boss, MapInitEvent args)
        => boss.DevourAvailableAt = _timing.CurTime + TimeSpan.FromSeconds(boss.DevourSafeTime);

    public bool CanConsiderDevour(EntityUid uid)
        => TryComp<WesternDragonBossComponent>(uid, out var boss) &&
           _timing.CurTime >= boss.DevourAvailableAt && _timing.CurTime >= boss.NextDevourAttempt;

    private bool ValidFood(EntityUid uid, EntityUid food, DevourerComponent devour)
        => !TerminatingOrDeleted(food) && food != uid && !_containers.IsEntityInContainer(food) &&
           TryComp<MobStateComponent>(food, out var state) && state.CurrentState is MobState.Critical or MobState.Dead &&
           !_factions.IsEntityFriendly(uid, food) && _interaction.IsAccessible(uid, food) &&
           _whitelist.CheckBoth(food, devour.Blacklist, devour.Whitelist) &&
           _whitelist.IsWhitelistPassOrNull(devour.FoodPreferenceWhitelist, food);

    private bool TryDevour(EntityUid uid, WesternDragonBossComponent boss, NPCBlackboard board, List<Enemy> enemies)
    {
        if (!TryComp<DevourerComponent>(uid, out var devour))
            return false;

        if (boss.DevourDoAfter is { } active && _doAfter.IsRunning(active))
        {
            if (_timing.CurTime < boss.DevourAvailableAt || boss.FoodTarget is not { } food ||
                !ValidFood(uid, food, devour) || enemies.Any(e => e.Distance < 3))
            {
                CancelDevour(boss);
                return false;
            }
            _steering.Unregister(uid);
            return true;
        }
        boss.DevourDoAfter = null;
        if (!CanConsiderDevour(uid) || _timing.CurTime < boss.RecoverUntil || enemies.Any(e => e.Distance < 3) ||
            _actions.GetAction(devour.DevourActionEntity) is not { } action || !_actions.ValidAction(action))
            return false;

        // Keep the selected meal while approaching it; don't alternate between nearby bodies.
        var foodTarget = boss.FoodTarget;
        if (foodTarget is not { } retained || !ValidFood(uid, retained, devour) ||
            !_interaction.InRangeUnobstructed(uid, retained, 6))
        {
            foodTarget = null;
            var nearest = 36f;
            var origin = _transform.GetWorldPosition(uid);
            foreach (var food in _utility.GetEntities(board, "WesternDragonFoodTargets", bestOnly: false).Entities.Keys)
            {
                if (!ValidFood(uid, food, devour))
                    continue;
                var distance = (_transform.GetWorldPosition(food) - origin).LengthSquared();
                if (distance >= nearest)
                    continue;
                foodTarget = food;
                nearest = distance;
            }
        }
        if (foodTarget is not { } target)
        {
            boss.NextDevourAttempt = _timing.CurTime + TimeSpan.FromSeconds(2);
            return false;
        }

        // Give up a failed food route instead of retrying it every tick.
        if (boss.FoodTarget == target && TryComp<NPCSteeringComponent>(uid, out var old) && old.Status == SteeringStatus.NoPath)
        {
            boss.NextDevourAttempt = _timing.CurTime + TimeSpan.FromSeconds(5);
            boss.FoodTarget = null;
            _steering.Unregister(uid);
            return false;
        }
        boss.FoodTarget = target;
        boss.Positioning = DragonPositioning.Feed;
        if (!_interaction.InRangeUnobstructed(uid, target, 1.2f))
        {
            var steering = _steering.Register(uid, Transform(target).Coordinates);
            steering.DirectMove = Transform(uid).GridUid == null;
            steering.Range = 0.9f;
            steering.InRangeMaxSpeed = 0.1f;
            return true;
        }
        // Brake before starting the existing movement-sensitive devour do-after.
        if (TryComp<PhysicsComponent>(uid, out var body) && body.LinearVelocity.LengthSquared() > 0.01f)
        {
            var brake = _steering.Register(uid, Transform(uid).Coordinates);
            brake.DirectMove = true;
            brake.Range = 0.1f;
            brake.InRangeMaxSpeed = 0.1f;
            return true;
        }
        _steering.Unregister(uid);
        _combat.SetInCombatMode(uid, false);
        boss.NextDevourAttempt = _timing.CurTime + TimeSpan.FromSeconds(1);
        if (!_actions.TryPerformDragonAction(uid, action, target) || !TryComp<DoAfterComponent>(uid, out var doAfters))
            return false;
        foreach (var pending in doAfters.DoAfters.Values)
        {
            if (pending.Args.Event is DevourDoAfterEvent && pending.Args.Target == target && _doAfter.IsRunning(pending.Id))
            {
                boss.DevourDoAfter = pending.Id;
                return true;
            }
        }
        return false;
    }

    private void CancelDevour(WesternDragonBossComponent boss)
    {
        var pending = boss.DevourDoAfter;
        var wasFeeding = pending != null || boss.FoodTarget != null;
        boss.DevourDoAfter = null;
        boss.FoodTarget = null;
        if (wasFeeding)
            boss.NextPosition = TimeSpan.Zero;
        if (_doAfter.IsRunning(pending))
            _doAfter.Cancel(pending);
    }

    private void OnDevourFinished(EntityUid uid, WesternDragonBossComponent boss, DevourDoAfterEvent args)
    {
        // Manual player devours keep their existing behavior. Only guard a do-after started by this AI.
        if (boss.DevourDoAfter != args.DoAfter.Id)
            return;
        if (!args.Cancelled && (_timing.CurTime < boss.DevourAvailableAt || !_npc.Enabled || !HasComp<ActiveNPCComponent>(uid) ||
            HasComp<ActorComponent>(uid) || !TryComp<HTNComponent>(uid, out var htn) || !htn.Enabled ||
            !TryComp<MobStateComponent>(uid, out var mob) || mob.CurrentState != MobState.Alive ||
            !TryComp<DevourerComponent>(uid, out var devour) || args.Target is not { } target ||
            !ValidFood(uid, target, devour) || !_interaction.InRangeUnobstructed(uid, target, 1.5f)))
            args.Handled = true;
        boss.DevourDoAfter = null;
        boss.FoodTarget = null;
    }
}
