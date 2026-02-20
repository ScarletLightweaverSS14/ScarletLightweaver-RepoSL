using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators.Combat;

/// <summary>
/// Operator that makes hostile mobs flee to a safer position when injured.
/// Does not use pathfinding - moves directly away from detected threats.
/// </summary>
public sealed partial class FleeOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    
    private SharedTransformSystem _transform = default!;
    private NPCSteeringSystem _steering = default!;

    /// <summary>
    /// Key where the threat entity is stored (optional - if not provided, flees from Target)
    /// </summary>
    [DataField("threatKey")]
    public string ThreatKey = "Target";

    /// <summary>
    /// Minimum distance to flee to
    /// </summary>
    [DataField("fleeDistance")]
    public float FleeDistance = 10f;

    /// <summary>
    /// Maximum distance to attempt to flee to
    /// </summary>
    [DataField("fleeDistanceMax")]
    public float FleeDistanceMax = 15f;

    /// <summary>
    /// Store the flee target coordinates in this key
    /// </summary>
    [DataField("fleeTargetKey")]
    public string FleeTargetKey = "FleeTarget";

    /// <summary>
    /// When to shut the task down.
    /// </summary>
    [DataField("shutdownState")]
    public HTNPlanState ShutdownState { get; private set; } = HTNPlanState.TaskFinished;

    private EntityCoordinates? _fleeTarget;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
        _steering = sysManager.GetEntitySystem<NPCSteeringSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        Logger.Debug($"FleeOperator.Plan: Called for entity {_entManager.ToPrettyString(owner)}");

        // Check if entity still exists
        if (!_entManager.EntityExists(owner))
        {
            Logger.Debug($"FleeOperator.Plan: Owner entity no longer exists");
            return (false, null);
        }

        if (!_entManager.TryGetComponent<TransformComponent>(owner, out var xform))
        {
            Logger.Debug($"FleeOperator.Plan: Owner missing TransformComponent");
            return (false, null);
        }

        // Try to find the threat
        EntityUid? threat = null;
        if (blackboard.TryGetValue<EntityUid>(ThreatKey, out var threatEntity, _entManager))
        {
            threat = threatEntity;
            Logger.Debug($"FleeOperator.Plan: Found threat entity {_entManager.ToPrettyString(threat.Value)} from blackboard key '{ThreatKey}'");
        }
        else
        {
            Logger.Debug($"FleeOperator.Plan: No threat found in blackboard key '{ThreatKey}'");
        }

        if (threat == null || !_entManager.EntityExists(threat.Value))
        {
            Logger.Debug($"FleeOperator.Plan: Threat is null or doesn't exist");
            return (false, null);
        }

        if (!_entManager.TryGetComponent<TransformComponent>(threat.Value, out var threatXform))
            return (false, null);

        // Calculate flee direction (opposite of threat)
        var ownerPos = _transform.GetWorldPosition(xform);
        var threatPos = _transform.GetWorldPosition(threatXform);
        var fleeDirection = Vector2.Normalize(ownerPos - threatPos);

        // Add some randomization to avoid predictable patterns
        var angle = _random.NextFloat(-0.3f, 0.3f); // ~17 degrees variation
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);
        fleeDirection = new Vector2(
            fleeDirection.X * cos - fleeDirection.Y * sin,
            fleeDirection.X * sin + fleeDirection.Y * cos
        );

        // Calculate target flee distance
        var targetDistance = _random.NextFloat(FleeDistance, FleeDistanceMax);
        var fleeTargetWorld = ownerPos + fleeDirection * targetDistance;

        // Convert back to entity coordinates
        var fleeTargetCoords = new EntityCoordinates(xform.MapUid ?? EntityUid.Invalid, fleeTargetWorld);

        Logger.Info($"FleeOperator.Plan: SUCCESS - Entity {_entManager.ToPrettyString(owner)} will flee {targetDistance:F1} tiles from threat {_entManager.ToPrettyString(threat.Value)}");

        return (true, new Dictionary<string, object>()
        {
            {FleeTargetKey, fleeTargetCoords},
            {NPCBlackboard.OwnerCoordinates, fleeTargetCoords}
        });
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        
        // Check if entity still exists
        if (!_entManager.EntityExists(owner))
            return;
        
        // Store flee target if it exists in blackboard
        if (blackboard.TryGetValue<EntityCoordinates>(FleeTargetKey, out var fleeTarget, _entManager))
        {
            _fleeTarget = fleeTarget;
            
            // Start steering toward flee target
            var steering = _steering.Register(owner, fleeTarget);
            steering.Range = 1f;
        }
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        base.Update(blackboard, frameTime);
        
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        // Check if entity still exists
        if (!_entManager.EntityExists(owner))
            return HTNOperatorStatus.Failed;

        if (!_entManager.TryGetComponent<TransformComponent>(owner, out var xform))
            return HTNOperatorStatus.Failed;

        if (_fleeTarget == null)
            return HTNOperatorStatus.Failed;

        // Check if we've reached the flee destination or are far enough
        if (xform.Coordinates.TryDistance(_entManager, _fleeTarget.Value, out var distance))
        {
            if (distance <= 2f)
            {
                // Reached flee destination
                return HTNOperatorStatus.Finished;
            }
        }

        // Continue fleeing
        return HTNOperatorStatus.Continuing;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        
        // Clean up steering - check if entity still exists first
        if (_entManager.EntityExists(owner))
        {
            _entManager.RemoveComponent<NPCSteeringComponent>(owner);
        }
        
        _fleeTarget = null;
        
        // Remove flee target from blackboard
        blackboard.Remove<EntityCoordinates>(FleeTargetKey);
    }
}
