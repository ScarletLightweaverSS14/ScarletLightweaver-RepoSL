using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Tag;
using JetBrains.Annotations;

namespace Content.Server._Starlight.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
/// Searches the NPC's pocket inventory slots for an item bearing a specified tag (e.g. "Medipen").
/// Moves it to an empty hand, raises <see cref="UseInHandEvent"/> so the item's own system handles
/// usage (e.g. InjectorSystem injects reagents into the NPC's bloodstream), then drops the used item.
/// </summary>
[UsedImplicitly]
public sealed partial class UseItemFromPocketOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    private InventorySystem _inventory = default!;
    private SharedHandsSystem _hands = default!;
    private TagSystem _tagSystem = default!;
    private IEntitySystemManager _sysMan = default!;

    /// <summary>
    /// The tag that the target item must have (e.g. "Medipen").
    /// </summary>
    [DataField(required: true)]
    public string ItemTag = string.Empty;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _sysMan    = sysManager;
        _inventory = sysManager.GetEntitySystem<InventorySystem>();
        _hands     = sysManager.GetEntitySystem<SharedHandsSystem>();
        _tagSystem = sysManager.GetEntitySystem<TagSystem>();
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

        var enumerator = _inventory.GetSlotEnumerator(owner, SlotFlags.POCKET);
        while (enumerator.NextItem(out var content, out var slotDef))
        {
            if (!_tagSystem.HasTag(content, ItemTag))
                continue;

            // Move from pocket to an empty hand.
            _inventory.TryUnequip(owner, slotDef.Name, silent: true, force: true);

            var pickedUp = false;
            if (_entManager.TryGetComponent<HandsComponent>(owner, out var handsComp))
            {
                foreach (var handName in handsComp.SortedHands)
                {
                    if (_hands.GetHeldItem((owner, handsComp), handName) != null)
                        continue;
                    pickedUp = _hands.TryPickup(owner, content, handName, checkActionBlocker: false, handsComp: handsComp);
                    break;
                }
            }

            // If no free hand is available the pen lands on the ground.
            // Plan() searches only pockets, so the heal branch will cleanly fail next replan.
            if (!pickedUp)
                return HTNOperatorStatus.Failed;

            // Trigger the item's normal use-in-hand logic (InjectorSystem + zero-delay do-after
            // injects reagents into the NPC's own bloodstream immediately).
            var useEv = new UseInHandEvent(owner);
            _entManager.EventBus.RaiseLocalEvent(content, useEv);

            // Drop the spent pen. Guard in case it was already deleted by some external system.
            if (_entManager.EntityExists(content))
                _hands.TryDrop(owner, content, checkActionBlocker: false);

            // Force an immediate replan so the NPC re-engages without waiting out the
            // 0.45 s replan cooldown — otherwise it stands idle after healing.
            if (_entManager.TryGetComponent<HTNComponent>(owner, out var htn))
                _sysMan.GetEntitySystem<HTNSystem>().Replan(htn);

            return HTNOperatorStatus.Finished;
        }

        return HTNOperatorStatus.Failed;
    }
}
