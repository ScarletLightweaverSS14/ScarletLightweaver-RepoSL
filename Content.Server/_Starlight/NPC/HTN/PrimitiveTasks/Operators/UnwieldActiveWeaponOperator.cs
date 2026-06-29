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
/// Unwields the NPC's active held item if it is currently wielded (two-handed).
/// Immediately returns Finished once the weapon is unwielded (or if no unwield is needed).
/// </summary>
[UsedImplicitly]
public sealed partial class UnwieldActiveWeaponOperator : HTNOperator
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

        // Not wieldable or already unwielded — nothing to do.
        if (!_entManager.TryGetComponent<WieldableComponent>(held.Value, out var wieldable)
            || !wieldable.Wielded)
            return HTNOperatorStatus.Finished;

        _wieldable.TryUnwield(held.Value, wieldable, owner, force: true);
        return HTNOperatorStatus.Finished;
    }
}
