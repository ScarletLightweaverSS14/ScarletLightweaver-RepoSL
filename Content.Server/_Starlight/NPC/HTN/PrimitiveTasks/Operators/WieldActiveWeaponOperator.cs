using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using JetBrains.Annotations;

namespace Content.Server._Starlight.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
/// Wields (two-hands) whatever the NPC is currently holding in their active hand,
/// if it has a <see cref="WieldableComponent"/>. Immediately returns Finished once
/// the weapon is wielded (or if no wielding is required).
/// </summary>
[UsedImplicitly]
public sealed partial class WieldActiveWeaponOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    private SharedHandsSystem _hands = default!;
    private SharedWieldableSystem _wieldable = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _hands = sysManager.GetEntitySystem<SharedHandsSystem>();
        _wieldable = sysManager.GetEntitySystem<SharedWieldableSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entManager.TryGetComponent<HandsComponent>(owner, out var handsComp))
            return (false, null);

        // Valid as long as we're holding something.
        return (_hands.GetActiveItem((owner, handsComp)) != null, null);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entManager.TryGetComponent<HandsComponent>(owner, out var handsComp))
            return HTNOperatorStatus.Failed;

        var held = _hands.GetActiveItem((owner, handsComp));
        if (held == null)
            return HTNOperatorStatus.Failed;

        // If the item doesn't need wielding, nothing to do.
        if (!_entManager.TryGetComponent<WieldableComponent>(held.Value, out var wieldable))
            return HTNOperatorStatus.Finished;

        // Already wielded — done.
        if (wieldable.Wielded)
            return HTNOperatorStatus.Finished;

        _wieldable.TryWield(held.Value, wieldable, owner);
        return HTNOperatorStatus.Finished;
    }
}
