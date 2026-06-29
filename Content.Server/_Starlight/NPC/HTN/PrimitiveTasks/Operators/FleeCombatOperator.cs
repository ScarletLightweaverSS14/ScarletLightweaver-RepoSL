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
using Robust.Shared.Physics.Components;

namespace Content.Server._Starlight.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
/// Kiting operator: simultaneously steers the NPC away from the threat while keeping
/// NPCRangedCombatComponent active so they keep shooting as they retreat.
/// Finishes when the NPC reaches <see cref="Range"/> tiles from the threat coordinates.
/// </summary>
[UsedImplicitly]
public sealed partial class FleeCombatOperator : HTNOperator, IHtnConditionalShutdown
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    private NPCSteeringSystem _steering = default!;
    private PathfindingSystem _pathfind = default!;
    private SharedTransformSystem _transform = default!;
    private SharedCombatModeSystem _combatMode = default!;

    [DataField("shutdownState")]
    public HTNPlanState ShutdownState { get; private set; } = HTNPlanState.TaskFinished;

    /// <summary>Blackboard key containing the threat coordinates to flee FROM.</summary>
    [DataField("targetKey")]
    public string TargetKey = "TargetCoordinates";

    /// <summary>Blackboard key containing the entity to shoot at.</summary>
    [DataField("gunTargetKey")]
    public string GunTargetKey = "Target";

    /// <summary>Safe distance in tiles. Operator finishes when this far from the threat.</summary>
    [DataField]
    public float Range = 9.0f;

    [DataField("pathfindKey")]
    public string PathfindKey = NPCBlackboard.PathfindKey;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _pathfind    = sysManager.GetEntitySystem<PathfindingSystem>();
        _steering    = sysManager.GetEntitySystem<NPCSteeringSystem>();
        _transform   = sysManager.GetEntitySystem<SharedTransformSystem>();
        _combatMode  = sysManager.GetEntitySystem<SharedCombatModeSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!blackboard.TryGetValue<EntityCoordinates>(TargetKey, out var threatCoords, _entManager))
            return (false, null);

        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_entManager.TryGetComponent<TransformComponent>(owner, out var xform) ||
            !_entManager.HasComponent<PhysicsComponent>(owner))
            return (false, null);

        // If already far enough away, nothing to do.
        if (xform.Coordinates.TryDistance(_entManager, threatCoords, out var dist) && dist >= Range)
            return (false, null);

        return (true, null);
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);

        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var threatCoords = blackboard.GetValue<EntityCoordinates>(TargetKey);
        blackboard.Remove<EntityCoordinates>(NPCBlackboard.OwnerCoordinates);

        // Steer away from threat.
        var ownerPos  = _transform.GetMoverCoordinates(owner);
        var dir       = (ownerPos.Position - threatCoords.Position).Normalized();
        var fleePos   = threatCoords.Offset(dir * Range * 1.5f);
        var comp      = _steering.Register(owner, fleePos);
        comp.Range    = Range;

        // Enable shooting at the stored gun target while retreating.
        if (blackboard.TryGetValue<EntityUid>(GunTargetKey, out var target, _entManager) && target.IsValid())
        {
            var ranged = _entManager.EnsureComponent<NPCRangedCombatComponent>(owner);
            ranged.Target = target;
        }
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        // Update gun target in case it changed.
        if (_entManager.TryGetComponent<NPCRangedCombatComponent>(owner, out var combat) &&
            blackboard.TryGetValue<EntityUid>(GunTargetKey, out var target, _entManager))
        {
            combat.Target = target;
        }

        // Finish when safe distance has been reached.
        if (blackboard.TryGetValue<EntityCoordinates>(TargetKey, out var threatCoords, _entManager))
        {
            var xform = _entManager.GetComponent<TransformComponent>(owner);
            if (xform.Coordinates.TryDistance(_entManager, threatCoords, out var dist) && dist >= Range)
                return HTNOperatorStatus.Finished;
        }

        if (!_entManager.TryGetComponent<NPCSteeringComponent>(owner, out var steering))
            return HTNOperatorStatus.Failed;

        return steering.Status switch
        {
            SteeringStatus.InRange  => HTNOperatorStatus.Finished,
            SteeringStatus.NoPath   => HTNOperatorStatus.Failed,
            SteeringStatus.Moving   => HTNOperatorStatus.Continuing,
            _                       => throw new ArgumentOutOfRangeException()
        };
    }

    public void ConditionalShutdown(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        // Stop steering.
        _steering.Unregister(owner);
        blackboard.Remove<PathResultEvent>(PathfindKey);

        // Stop shooting.
        _combatMode.SetInCombatMode(owner, false);
        _entManager.RemoveComponent<NPCRangedCombatComponent>(owner);
    }
}
