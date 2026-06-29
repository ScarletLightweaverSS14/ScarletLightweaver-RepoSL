using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Tag;
using JetBrains.Annotations;

namespace Content.Server._Starlight.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
/// Finds an item with the specified tag in the NPC's pocket slots and moves it
/// into an empty hand. Used to let NPCs draw backup melee weapons when their
/// ranged weapon runs dry (e.g. shotgunner pulls a knife from their pocket).
/// </summary>
[UsedImplicitly]
public sealed partial class EquipFromPocketOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    private InventorySystem _inventory = default!;
    private SharedHandsSystem _hands = default!;
    private TagSystem _tagSystem = default!;

    /// <summary>The tag the item in the pocket must have (e.g. "Knife").</summary>
    [DataField(required: true)]
    public string ItemTag = string.Empty;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _inventory = sysManager.GetEntitySystem<InventorySystem>();
        _hands     = sysManager.GetEntitySystem<SharedHandsSystem>();
        _tagSystem = sysManager.GetEntitySystem<TagSystem>();
    }

    private (EntityUid Item, string SlotId)? FindTaggedPocketItem(EntityUid owner)
    {
        var enumerator = _inventory.GetSlotEnumerator(owner, SlotFlags.POCKET);
        while (enumerator.NextItem(out var item, out var slotDef))
        {
            if (_tagSystem.HasTag(item, ItemTag))
                return (item, slotDef.Name);
        }
        return null;
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entManager))
            return (false, null);

        return (FindTaggedPocketItem(owner) != null, null);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        var found = FindTaggedPocketItem(owner);
        if (found == null)
            return HTNOperatorStatus.Failed;

        var (item, slotId) = found.Value;

        if (!_entManager.TryGetComponent<HandsComponent>(owner, out var handsComp))
            return HTNOperatorStatus.Failed;

        // Find an empty hand to receive the item.
        foreach (var handName in handsComp.SortedHands)
        {
            if (_hands.GetHeldItem((owner, handsComp), handName) != null)
                continue;

            // Unequip from pocket by slot name, then pick up into the empty hand.
            if (_inventory.TryUnequip(owner, slotId, silent: true, force: true))
            {
                _hands.TryPickup(owner, item, handName, checkActionBlocker: false, handsComp: handsComp);
                return HTNOperatorStatus.Finished;
            }
        }

        return HTNOperatorStatus.Failed;
    }
}
