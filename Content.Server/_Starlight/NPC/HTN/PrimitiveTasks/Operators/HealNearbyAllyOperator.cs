using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Shared.Damage.Components;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Tag;
using JetBrains.Annotations;
using Robust.Shared.Map;

namespace Content.Server._Starlight.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
/// Used by the hybrid Red Vanguard class.
/// Finds a nearby wounded faction ally, moves to them, then uses a pocket medipen
/// on the ally via <see cref="AfterInteractEvent"/>.
/// </summary>
[UsedImplicitly]
public sealed partial class HealNearbyAllyOperator : HTNOperator, IHtnConditionalShutdown
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    private NPCSteeringSystem _steering = default!;
    private NpcFactionSystem _faction = default!;
    private MobThresholdSystem _thresholds = default!;
    private SharedTransformSystem _transform = default!;
    private InventorySystem _inventory = default!;
    private TagSystem _tagSystem = default!;

    [DataField("shutdownState")]
    public HTNPlanState ShutdownState { get; private set; } = HTNPlanState.TaskFinished;

    /// <summary>Range in which to search for wounded allies.</summary>
    [DataField]
    public float SearchRange = 8.0f;

    /// <summary>Range in tiles at which the medipen can be applied.</summary>
    [DataField]
    public float UseRange = 1.5f;

    /// <summary>Heal ally when their damage exceeds this fraction of their crit threshold.</summary>
    [DataField]
    public float MinDamagePercent = 0.35f;

    /// <summary>Tag the medipen item must have.</summary>
    [DataField]
    public string ItemTag = "Medipen";

    private const string AllyKey      = "_HealAllyTarget";
    private const string PathfindKey  = NPCBlackboard.PathfindKey;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _steering   = sysManager.GetEntitySystem<NPCSteeringSystem>();
        _faction    = sysManager.GetEntitySystem<NpcFactionSystem>();
        _thresholds = sysManager.GetEntitySystem<MobThresholdSystem>();
        _transform  = sysManager.GetEntitySystem<SharedTransformSystem>();
        _inventory  = sysManager.GetEntitySystem<InventorySystem>();
        _tagSystem  = sysManager.GetEntitySystem<TagSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entManager))
            return (false, null);

        // Must have a medipen in pocket to be able to heal.
        if (!HasMedipen(owner))
            return (false, null);

        // Must have at least one wounded ally in range.
        var ally = FindWoundedAlly(owner);
        return ally.HasValue ? (true, null) : (false, null);
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);

        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var ally = FindWoundedAlly(owner);
        if (!ally.HasValue)
            return;

        blackboard.SetValue(AllyKey, ally.Value);

        var allyXform = _entManager.GetComponent<TransformComponent>(ally.Value);
        var allyWorld = _transform.GetWorldPosition(allyXform);
        var ownerXform = _entManager.GetComponent<TransformComponent>(owner);
        var mapUid = ownerXform.MapUid;
        if (mapUid == null)
            return;

        var comp = _steering.Register(owner, new EntityCoordinates(mapUid.Value, allyWorld));
        comp.Range = UseRange;
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!blackboard.TryGetValue<EntityUid>(AllyKey, out var ally, _entManager) ||
            !_entManager.EntityExists(ally))
            return HTNOperatorStatus.Finished;

        // Check distance.
        var ownerXform = _entManager.GetComponent<TransformComponent>(owner);
        var allyXform  = _entManager.GetComponent<TransformComponent>(ally);
        var dist = (_transform.GetWorldPosition(ownerXform) - _transform.GetWorldPosition(allyXform)).Length();

        if (dist > UseRange)
        {
            // Still moving toward ally — check for NoPath.
            if (_entManager.TryGetComponent<NPCSteeringComponent>(owner, out var steeringComp) &&
                steeringComp.Status == SteeringStatus.NoPath)
                return HTNOperatorStatus.Finished;

            return HTNOperatorStatus.Continuing;
        }

        // In range — find and apply medipen.
        return ApplyMedipenToAlly(owner, ally);
    }

    public void ConditionalShutdown(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        _steering.Unregister(owner);
        blackboard.Remove<PathResultEvent>(PathfindKey);
        blackboard.Remove<EntityUid>(AllyKey);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private bool HasMedipen(EntityUid owner)
    {
        var enumerator = _inventory.GetSlotEnumerator(owner, SlotFlags.POCKET);
        while (enumerator.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is { } item && _tagSystem.HasTag(item, ItemTag))
                return true;
        }
        return false;
    }

    private EntityUid? FindWoundedAlly(EntityUid owner)
    {
        EntityUid? best = null;
        float bestDamagePercent = MinDamagePercent;

        foreach (var ally in _faction.GetNearbyFriendlies(owner, SearchRange))
        {
            if (!_entManager.TryGetComponent<DamageableComponent>(ally, out var damageable))
                continue;
            if (!_thresholds.TryGetThresholdForState(ally, MobState.Critical, out var crit) || crit.Value == 0)
                continue;

            var pct = (float) damageable.TotalDamage / (float) crit.Value;
            if (pct >= bestDamagePercent)
            {
                bestDamagePercent = pct;
                best = ally;
            }
        }

        return best;
    }

    private HTNOperatorStatus ApplyMedipenToAlly(EntityUid owner, EntityUid ally)
    {
        var enumerator = _inventory.GetSlotEnumerator(owner, SlotFlags.POCKET);
        while (enumerator.NextItem(out var item, out var slotDef))
        {
            if (!_tagSystem.HasTag(item, ItemTag))
                continue;

            // Unequip from pocket.
            _inventory.TryUnequip(owner, slotDef.Name, silent: true, force: true);

            // Raise AfterInteractEvent on the injector targeting the ally.
            var allyXform = _entManager.GetComponent<TransformComponent>(ally);
            var ev = new AfterInteractEvent(owner, item, ally, allyXform.Coordinates, canReach: true);
            _entManager.EventBus.RaiseLocalEvent(item, ev);

            // Delete the spent pen if it still exists.
            if (_entManager.EntityExists(item))
                _entManager.DeleteEntity(item);

            return HTNOperatorStatus.Finished;
        }

        return HTNOperatorStatus.Finished;
    }
}
