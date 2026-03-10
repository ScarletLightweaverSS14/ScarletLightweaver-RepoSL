using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Tag;
using JetBrains.Annotations;

namespace Content.Server._Starlight.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
/// Searches the NPC's held items (hands) for one bearing a specified tag (e.g. "Medipen"),
/// directly heals the owner via DamageableSystem, then deletes the item.
/// Pair this with <see cref="EquipFromPocketOperator"/> so the NPC visibly draws the item
/// before consuming it — mimicking a player's "pull from pocket → use" flow.
/// </summary>
[UsedImplicitly]
public sealed partial class UseItemInHandOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    private SharedHandsSystem _hands = default!;
    private TagSystem _tagSystem = default!;
    private DamageableSystem _damageable = default!;

    /// <summary>The tag the held item must have (e.g. "Medipen").</summary>
    [DataField(required: true)]
    public string ItemTag = string.Empty;

    /// <summary>
    /// Healing applied directly when the item is consumed.
    /// Use negative values, e.g. groups: { Brute: -30 }.
    /// </summary>
    [DataField(required: true)]
    public DamageSpecifier DirectHeal = new();

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _hands     = sysManager.GetEntitySystem<SharedHandsSystem>();
        _tagSystem = sysManager.GetEntitySystem<TagSystem>();
        _damageable = sysManager.GetEntitySystem<DamageableSystem>();
    }

    private EntityUid? FindTaggedHeldItem(EntityUid owner)
    {
        if (!_entManager.TryGetComponent<HandsComponent>(owner, out var handsComp))
            return null;

        foreach (var handName in handsComp.SortedHands)
        {
            var held = _hands.GetHeldItem((owner, handsComp), handName);
            if (held != null && _tagSystem.HasTag(held.Value, ItemTag))
                return held.Value;
        }
        return null;
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entManager))
            return (false, null);

        return (FindTaggedHeldItem(owner) != null, null);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        var item = FindTaggedHeldItem(owner);
        if (item == null)
            return HTNOperatorStatus.Failed;

        // Apply healing directly — works without bloodstream/chemistry on NPC mobs.
        _damageable.TryChangeDamage(owner, DirectHeal, ignoreResistances: true);
        _entManager.QueueDeleteEntity(item.Value);
        return HTNOperatorStatus.Finished;
    }
}
