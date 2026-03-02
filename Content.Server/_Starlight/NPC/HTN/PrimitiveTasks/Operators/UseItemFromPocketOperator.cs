using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Tag;
using JetBrains.Annotations;

namespace Content.Server._Starlight.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
/// Searches the NPC's pocket inventory slots for an item bearing a specified tag (e.g. "Medipen"),
/// then directly injects its reagent solution into the owner — bypassing the normal do-after so
/// NPCs can heal reliably while moving.
/// </summary>
[UsedImplicitly]
public sealed partial class UseItemFromPocketOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    private InventorySystem _inventory = default!;
    private TagSystem _tagSystem = default!;
    private SharedSolutionContainerSystem _solutions = default!;
    private ReactiveSystem _reactive = default!;

    // ChemicalMedipen (and its descendants like BruteAutoInjector) store reagents in "pen".
    private const string InjectorSolutionName = "pen";

    /// <summary>
    /// The tag that the target item must have (e.g. "Medipen").
    /// </summary>
    [DataField(required: true)]
    public string ItemTag = string.Empty;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _inventory = sysManager.GetEntitySystem<InventorySystem>();
        _tagSystem = sysManager.GetEntitySystem<TagSystem>();
        _solutions = sysManager.GetEntitySystem<SharedSolutionContainerSystem>();
        _reactive = sysManager.GetEntitySystem<ReactiveSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        var enumerator = _inventory.GetSlotEnumerator(owner, SlotFlags.POCKET);
        while (enumerator.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is not { } content)
                continue;

            if (_tagSystem.HasTag(content, ItemTag) && HasChemicals(content))
                return (true, null);
        }

        return (false, null);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        var enumerator = _inventory.GetSlotEnumerator(owner, SlotFlags.POCKET);
        while (enumerator.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is not { } content)
                continue;

            if (!_tagSystem.HasTag(content, ItemTag))
                continue;

            if (TryDirectInject(content, owner))
                return HTNOperatorStatus.Finished;
        }

        return HTNOperatorStatus.Failed;
    }

    private bool HasChemicals(EntityUid item)
    {
        Entity<SolutionComponent>? solRef = null;
        return _solutions.ResolveSolution(item, InjectorSolutionName, ref solRef, out var sol)
               && sol.Volume > FixedPoint2.Zero;
    }

    /// <summary>
    /// Pulls the entire injector solution out of <paramref name="item"/> and applies it
    /// to <paramref name="target"/> via DoEntityReaction — no do-after or injectable
    /// solution component required. Healing reagents (Brutemend etc.) take effect immediately.
    /// Deletes the item once consumed.
    /// </summary>
    private bool TryDirectInject(EntityUid item, EntityUid target)
    {
        Entity<SolutionComponent>? solRef = null;
        if (!_solutions.ResolveSolution(item, InjectorSolutionName, ref solRef, out var injSol)
            || injSol.Volume <= FixedPoint2.Zero)
            return false;

        // Split the full dose out of the pen and react it against the NPC.
        // DoEntityReaction triggers all reagent effects (healing, etc.) regardless of
        // whether the target has an InjectableSolutionComponent.
        var split = _solutions.SplitSolution(solRef!.Value, injSol.Volume);
        _reactive.DoEntityReaction(target, split, ReactionMethod.Injection);

        // Delete the now-empty medipen.
        _entManager.QueueDeleteEntity(item);
        return true;
    }
}
