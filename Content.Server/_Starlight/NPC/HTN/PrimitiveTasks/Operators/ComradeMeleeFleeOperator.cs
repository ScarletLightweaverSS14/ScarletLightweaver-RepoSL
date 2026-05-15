using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
/// Flee operator activated when <see cref="ComradeMeleeApproachOperator"/> detects a NoPath.
/// Reads the flee-until timestamp from the blackboard and steers the NPC away from the
/// last-known threat coordinates until the timer expires, then removes the key so normal
/// combat can resume.
/// </summary>
[UsedImplicitly]
public sealed partial class ComradeMeleeFleeOperator : HTNOperator, IHtnConditionalShutdown
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private NPCSteeringSystem _steering = default!;
    private SharedTransformSystem _transform = default!;

    [DataField("shutdownState")]
    public HTNPlanState ShutdownState { get; private set; } = HTNPlanState.TaskFinished;

    /// <summary>Blackboard key with the TimeSpan timestamp of when to stop fleeing.</summary>
    [DataField]
    public string FleeUntilKey = "_ComradeMeleeFleeUntil";

    /// <summary>Blackboard key with the last-known threat coordinates to flee from.</summary>
    [DataField]
    public string TargetCoordinatesKey = "TargetCoordinates";

    /// <summary>Distance to flee in tiles.</summary>
    [DataField]
    public float FleeRange = 8.0f;

    private const string PathfindKey = NPCBlackboard.PathfindKey;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _steering  = sysManager.GetEntitySystem<NPCSteeringSystem>();
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        // Only valid while the flee timer key exists.
        if (!blackboard.TryGetValue<TimeSpan>(FleeUntilKey, out _, _entManager))
            return (false, null);

        return (true, null);
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);

        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        // Steer away from last-known threat position.
        if (blackboard.TryGetValue<EntityCoordinates>(TargetCoordinatesKey, out var threatCoords, _entManager))
            RegisterFleeWaypoint(owner, threatCoords);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        // If the timer has expired, stop fleeing.
        if (!blackboard.TryGetValue<TimeSpan>(FleeUntilKey, out var fleeUntil, _entManager) ||
            _timing.CurTime >= fleeUntil)
        {
            blackboard.Remove<TimeSpan>(FleeUntilKey);
            return HTNOperatorStatus.Finished;
        }

        // If we can't path anywhere, just wait out the timer.
        if (!_entManager.TryGetComponent<NPCSteeringComponent>(GetOwner(blackboard), out var steeringComp))
            return HTNOperatorStatus.Continuing;

        return steeringComp.Status switch
        {
            SteeringStatus.InRange  => HTNOperatorStatus.Finished,
            SteeringStatus.NoPath   => HTNOperatorStatus.Continuing, // wait out the timer
            SteeringStatus.Moving   => HTNOperatorStatus.Continuing,
            _                       => HTNOperatorStatus.Continuing,
        };
    }

    public void ConditionalShutdown(NPCBlackboard blackboard)
    {
        var owner = GetOwner(blackboard);
        _steering.Unregister(owner);
        blackboard.Remove<PathResultEvent>(PathfindKey);
    }

    private EntityUid GetOwner(NPCBlackboard blackboard)
        => blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

    private void RegisterFleeWaypoint(EntityUid owner, EntityCoordinates threatCoords)
    {
        var ownerXform  = _entManager.GetComponent<TransformComponent>(owner);
        var ownerWorld  = _transform.GetWorldPosition(ownerXform);

        Vector2 threatWorld;
        if (threatCoords.IsValid(_entManager))
        {
            var mapCoords = _transform.ToMapCoordinates(threatCoords);
            threatWorld   = mapCoords.Position;
        }
        else
        {
            // No valid threat coords — flee in a random-ish direction.
            threatWorld = ownerWorld + new Vector2(1f, 0f);
        }

        var dir      = ownerWorld - threatWorld;
        var dirLen   = dir.Length();
        var fleeDir  = dirLen > 0.01f ? dir / dirLen : new Vector2(1f, 0f);
        var fleeDest = ownerWorld + fleeDir * FleeRange;

        var mapUid = ownerXform.MapUid;
        if (mapUid == null)
            return;

        var comp = _steering.Register(owner, new EntityCoordinates(mapUid.Value, fleeDest));
        comp.Range = 1.0f;
    }
}
