using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Tag;
using JetBrains.Annotations;

namespace Content.Server._Starlight.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
/// Searches the NPC's pocket inventory slots for an item bearing a specified tag (e.g. "Medipen").
/// Visually moves it to a free hand, applies direct healing via DamageableSystem, then deletes it.
/// Works without bloodstream/chemistry — safe for any HTN NPC.
/// </summary>
[UsedImplicitly]
public sealed partial class UseItemFromPocketOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    private InventorySystem _inventory = default!;
    private SharedHandsSystem _hands = default!;
    private TagSystem _tagSystem = default!;
    private DamageableSystem _damageable = default!;

    /// <summary>
    /// The tag that the target item must have (e.g. "Medipen").
    /// </summary>
    [DataField(required: true)]
    public string ItemTag = string.Empty;

    /// <summary>
    /// How much healing (negative damage) to apply directly when the item is consumed.
    /// Use negative values to heal, e.g. groups: { Brute: -30 }.
    /// </summary>
    [DataField(required: true)]
    public DamageSpecifier DirectHeal = new();

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _inventory  = sysManager.GetEntitySystem<InventorySystem>();
        _hands      = sysManager.GetEntitySystem<SharedHandsSystem>();
        _tagSystem  = sysManager.GetEntitySystem<TagSystem>();
        _damageable = sysManager.GetEntitySystem<DamageableSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entManager))
            return (false, null);

        var enumerator = _inventory.GetSlotEnumerator(owner, SlotFlags.POCKET);
        while (enumerator.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is not { } content)
                continue;

            if (_tagSystem.HasTag(content, ItemTag))
                return (true, null);
        }

        return (false, null);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        // Find the tagged item and the slot name it lives in.
        var enumerator = _inventory.GetSlotEnumerator(owner, SlotFlags.POCKET);
        while (enumerator.NextItem(out var content, out var slotDef))
        {
            if (!_tagSystem.HasTag(content, ItemTag))
                continue;

            // Unequip from pocket so ownership leaves the inventory container.
            _inventory.TryUnequip(owner, slotDef.Name, silent: true, force: true);

            // Try to move the item into a free hand for the visual beat.
            if (_entManager.TryGetComponent<HandsComponent>(owner, out var handsComp))
            {
                foreach (var handName in handsComp.SortedHands)
                {
                    if (_hands.GetHeldItem((owner, handsComp), handName) != null)
                        continue;
                    _hands.TryPickup(owner, content, handName, checkActionBlocker: false, handsComp: handsComp);
                    break;
                }
            }

            // Apply healing directly — no chemistry or bloodstream required.
            _damageable.TryChangeDamage(owner, DirectHeal, ignoreResistances: true);
            _entManager.QueueDeleteEntity(content);
            return HTNOperatorStatus.Finished;
        }

        return HTNOperatorStatus.Failed;
    }
}
