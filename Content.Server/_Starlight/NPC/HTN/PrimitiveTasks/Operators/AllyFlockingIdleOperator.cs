using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Shared.NPC.Systems;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._Starlight.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
/// Idle operator that makes the NPC move toward a position near its faction allies,
/// while keeping a minimum spread distance so the group doesn't fully cluster.
/// Falls through (returns Finished immediately) when no allies are nearby, allowing
/// the parent compound to fall back to the regular IdleCompound.
/// </summary>
[UsedImplicitly]
public sealed partial class AllyFlockingIdleOperator : HTNOperator, IHtnConditionalShutdown
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private NPCSteeringSystem _steering = default!;
    private NpcFactionSystem _faction = default!;
    private SharedTransformSystem _transform = default!;

    [DataField("shutdownState")]
    public HTNPlanState ShutdownState { get; private set; } = HTNPlanState.TaskFinished;

    /// <summary>Radius in which to search for faction allies.</summary>
    [DataField]
    public float SearchRange = 18.0f;

    /// <summary>Desired spread radius around the group centroid.</summary>
    [DataField]
    public float SpreadRadius = 5.0f;

    /// <summary>Minimum distance to maintain from any individual ally.</summary>
    [DataField]
    public float MinAllyDistance = 2.5f;

    /// <summary>How close is "arrived" at the flock position.</summary>
    [DataField]
    public float ArrivalRange = 1.5f;

    /// <summary>Max seconds to spend travelling to the flock position before giving up.</summary>
    [DataField]
    public float Timeout = 6.0f;

    private const string ElapsedKey    = "_AllyFlockElapsed";
    private const string PathfindKey   = NPCBlackboard.PathfindKey;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _steering  = sysManager.GetEntitySystem<NPCSteeringSystem>();
        _faction   = sysManager.GetEntitySystem<NpcFactionSystem>();
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entManager))
            return (false, null);

        // Only valid if there are allies nearby.
        var allies = _faction.GetNearbyFriendlies(owner, SearchRange).ToList();
        return allies.Count > 0 ? (true, null) : (false, null);
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        blackboard.SetValue(ElapsedKey, 0f);

        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        RegisterFlockWaypoint(owner);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        var elapsed = blackboard.TryGetValue<float>(ElapsedKey, out var e, _entManager) ? e : 0f;
        elapsed += frameTime;
        blackboard.SetValue(ElapsedKey, elapsed);

        if (elapsed >= Timeout)
            return HTNOperatorStatus.Finished;

        if (!_entManager.TryGetComponent<NPCSteeringComponent>(owner, out var steeringComp))
            return HTNOperatorStatus.Finished;

        return steeringComp.Status switch
        {
            SteeringStatus.InRange => HTNOperatorStatus.Finished,
            SteeringStatus.NoPath  => HTNOperatorStatus.Finished,
            _                      => HTNOperatorStatus.Continuing,
        };
    }

    public void ConditionalShutdown(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        _steering.Unregister(owner);
        blackboard.Remove<PathResultEvent>(PathfindKey);
        blackboard.Remove<float>(ElapsedKey);
    }

    private void RegisterFlockWaypoint(EntityUid owner)
    {
        var ownerXform = _entManager.GetComponent<TransformComponent>(owner);
        var ownerWorld = _transform.GetWorldPosition(ownerXform);

        var allies = _faction.GetNearbyFriendlies(owner, SearchRange).ToList();
        if (allies.Count == 0)
            return;

        // Compute centroid of all allies.
        var centroid = Vector2.Zero;
        foreach (var ally in allies)
        {
            var allyXform = _entManager.GetComponent<TransformComponent>(ally);
            centroid += _transform.GetWorldPosition(allyXform);
        }
        centroid /= allies.Count;

        // Pick a random position within SpreadRadius of the centroid.
        var angle    = _random.NextFloat(0f, MathF.PI * 2f);
        var radius   = _random.NextFloat(1.5f, SpreadRadius);
        var candidate = centroid + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);

        // Make sure it's not too close to any ally; if so, nudge outward.
        foreach (var ally in allies)
        {
            var allyWorld = _transform.GetWorldPosition(_entManager.GetComponent<TransformComponent>(ally));
            var toAlly    = candidate - allyWorld;
            if (toAlly.Length() < MinAllyDistance)
            {
                var awayDir = toAlly.Length() > 0.01f ? toAlly.Normalized() : new Vector2(1f, 0f);
                candidate   = allyWorld + awayDir * MinAllyDistance;
            }
        }

        var mapUid = ownerXform.MapUid;
        if (mapUid == null)
            return;

        var comp = _steering.Register(owner, new EntityCoordinates(mapUid.Value, candidate));
        comp.Range = ArrivalRange;
    }
}
