using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using JetBrains.Annotations;

namespace Content.Server._Starlight.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
/// Switches the NPC's active hand to whichever hand is holding a gun.
/// Allows dual-weapon NPCs (e.g. hammer + pistol) to correctly enter ranged combat.
/// </summary>
[UsedImplicitly]
public sealed partial class SwitchHandToGunOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    private SharedHandsSystem _hands = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _hands = sysManager.GetEntitySystem<SharedHandsSystem>();
    }

    private string? FindGunHand(EntityUid owner)
    {
        if (!_entManager.TryGetComponent<HandsComponent>(owner, out var handsComp))
            return null;

        foreach (var handName in handsComp.SortedHands)
        {
            if (_hands.GetHeldItem((owner, handsComp), handName) is { } held &&
                _entManager.HasComponent<GunComponent>(held))
                return handName;
        }

        return null;
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        return (FindGunHand(owner) != null, null);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var handName = FindGunHand(owner);

        if (handName == null)
            return HTNOperatorStatus.Failed;

        if (!_entManager.TryGetComponent<HandsComponent>(owner, out var handsComp))
            return HTNOperatorStatus.Failed;

        // Already using the gun hand — nothing to do.
        if (handsComp.ActiveHandId == handName)
            return HTNOperatorStatus.Finished;

        _hands.TrySetActiveHand((owner, handsComp), handName);
        return HTNOperatorStatus.Finished;
    }
}
