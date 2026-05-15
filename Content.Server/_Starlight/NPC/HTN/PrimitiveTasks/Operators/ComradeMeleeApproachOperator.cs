using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Shared.CombatMode;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
/// Erratic flanking approach for melee comrades.
/// Instead of charging head-on, the NPC steers to offset waypoints around the target,
/// creating an unpredictable zig-zagging approach.
///
/// When <see cref="SteeringStatus.NoPath"/> is detected for long enough, the operator
/// writes a flee-until timestamp to the blackboard and returns Failed, which triggers
/// a replan and activates the flee branch in the parent compound.
/// </summary>
[UsedImplicitly]
public sealed partial class ComradeMeleeApproachOperator : HTNOperator, IHtnConditionalShutdown
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private NPCSteeringSystem _steering = default!;
    private SharedCombatModeSystem _combatMode = default!;
    private SharedTransformSystem _transform = default!;

    [DataField("shutdownState")]
    public HTNPlanState ShutdownState { get; private set; } = HTNPlanState.PlanFinished;

    /// <summary>Blackboard key for the target entity (set by UtilityOperator).</summary>
    [DataField]
    public string TargetKey = "Target";

    /// <summary>Blackboard key written when a flee period should begin.</summary>
    [DataField]
    public string FleeUntilKey = "_ComradeMeleeFleeUntil";

    /// <summary>Melee range in tiles — operator finishes when within this distance.</summary>
    [DataField]
    public float MeleeRange = 1.5f;

    /// <summary>How long to flee after a NoPath (seconds).</summary>
    [DataField]
    public float FleeDuration = 4.0f;

    /// <summary>How often to recalculate the flanking waypoint (seconds).</summary>
    [DataField]
    public float WaypointInterval = 1.1f;

    /// <summary>Minimum flank angle offset from the direct approach vector (degrees).</summary>
    [DataField]
    public float MinFlankAngle = 25f;

    /// <summary>Maximum flank angle offset (degrees).</summary>
    [DataField]
    public float MaxFlankAngle = 80f;

    private const string WaypointTimerKey = "_ComradeMeleeWaypointTimer";
    private const string PathfindKey = NPCBlackboard.PathfindKey;
    private const string NoPathTimerKey = "_ComradeMeleeNoPathTimer";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _steering   = sysManager.GetEntitySystem<NPCSteeringSystem>();
        _combatMode = sysManager.GetEntitySystem<SharedCombatModeSystem>();
        _transform  = sysManager.GetEntitySystem<SharedTransformSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager) || !target.IsValid())
            return (false, null);

        if (!_entManager.EntityExists(target))
            return (false, null);

        return (true, null);
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        _combatMode.SetInCombatMode(owner, true);

        // Force an immediate waypoint pick on the first Update.
        blackboard.SetValue(WaypointTimerKey, WaypointInterval);
        blackboard.SetValue(NoPathTimerKey, 0f);

        // Register initial steering toward a flanking waypoint.
        if (blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager) && target.IsValid())
            RegisterFlankingWaypoint(blackboard, owner, target);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager) || !target.IsValid() || !_entManager.EntityExists(target))
            return HTNOperatorStatus.Finished;

        // Check if already within melee range.
        var ownerXform = _entManager.GetComponent<TransformComponent>(owner);
        var targetXform = _entManager.GetComponent<TransformComponent>(target);
        var dist = (ownerXform.WorldPosition - targetXform.WorldPosition).Length();
        if (dist <= MeleeRange)
            return HTNOperatorStatus.Finished;

        if (!_entManager.TryGetComponent<NPCSteeringComponent>(owner, out var steeringComp))
            return HTNOperatorStatus.Failed;

        // Re-pick a flanking waypoint immediately when we reach the current one.
        if (steeringComp.Status == SteeringStatus.InRange)
        {
            RegisterFlankingWaypoint(blackboard, owner, target);
            blackboard.SetValue(WaypointTimerKey, 0f);
        }

        // Track consecutive NoPath time.
        var noPathTimer = blackboard.TryGetValue<float>(NoPathTimerKey, out var npt, _entManager) ? npt : 0f;
        if (steeringComp.Status == SteeringStatus.NoPath)
        {
            noPathTimer += frameTime;
            blackboard.SetValue(NoPathTimerKey, noPathTimer);

            if (noPathTimer >= 0.6f)
            {
                // Can't reach the target — trigger flee.
                blackboard.SetValue(FleeUntilKey, _timing.CurTime + TimeSpan.FromSeconds(FleeDuration));
                return HTNOperatorStatus.Failed;
            }
        }
        else
        {
            if (noPathTimer > 0f)
                blackboard.SetValue(NoPathTimerKey, 0f);
        }

        // Periodically pick a new flanking waypoint so the approach is erratic.
        var waypointTimer = blackboard.TryGetValue<float>(WaypointTimerKey, out var wt, _entManager) ? wt : 0f;
        waypointTimer += frameTime;
        if (waypointTimer >= WaypointInterval)
        {
            RegisterFlankingWaypoint(blackboard, owner, target);
            blackboard.SetValue(WaypointTimerKey, 0f);
        }
        else
        {
            blackboard.SetValue(WaypointTimerKey, waypointTimer);
        }

        return HTNOperatorStatus.Continuing;
    }

    public void ConditionalShutdown(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        _steering.Unregister(owner);
        blackboard.Remove<PathResultEvent>(PathfindKey);
        blackboard.Remove<float>(WaypointTimerKey);
        blackboard.Remove<float>(NoPathTimerKey);
        _combatMode.SetInCombatMode(owner, false);
    }

    private void RegisterFlankingWaypoint(NPCBlackboard blackboard, EntityUid owner, EntityUid target)
    {
        var ownerXform  = _entManager.GetComponent<TransformComponent>(owner);
        var targetXform = _entManager.GetComponent<TransformComponent>(target);

        var ownerWorld  = _transform.GetWorldPosition(ownerXform);
        var targetWorld = _transform.GetWorldPosition(targetXform);
        var toTarget    = targetWorld - ownerWorld;
        var dist        = toTarget.Length();

        if (dist < 0.01f)
            return;

        // Rotate the approach direction by a random angle offset for flanking behaviour.
        var angleDeg = _random.NextFloat(MinFlankAngle, MaxFlankAngle) * (_random.Prob(0.5f) ? 1f : -1f);
        var angleRad = angleDeg * MathF.PI / 180f;
        var cos      = MathF.Cos(angleRad);
        var sin      = MathF.Sin(angleRad);
        var forward  = toTarget / dist; // normalised
        var flankDir = new Vector2(forward.X * cos - forward.Y * sin, forward.X * sin + forward.Y * cos);

        // Move to a point just short of the target so we finish inside melee range.
        var closingDist  = MathF.Max(dist - MeleeRange * 0.8f, 0.4f);
        var waypointWorld = ownerWorld + flankDir * closingDist;

        // Build EntityCoordinates in map space (map entity is at world origin, so local = world).
        var mapUid = ownerXform.MapUid;
        if (mapUid == null)
            return;

        var waypointCoords = new EntityCoordinates(mapUid.Value, waypointWorld);
        var comp = _steering.Register(owner, waypointCoords);
        comp.Range = MeleeRange * 0.5f;
    }
}
