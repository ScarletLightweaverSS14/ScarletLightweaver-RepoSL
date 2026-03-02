using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using JetBrains.Annotations;

namespace Content.Server._Starlight.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
/// Switches the NPC's active hand to whichever hand is holding a dedicated melee weapon
/// (MeleeWeaponComponent present, GunComponent absent).
/// Allows dual-weapon NPCs to cleanly switch to melee after going out of ammunition.
/// </summary>
[UsedImplicitly]
public sealed partial class SwitchHandToMeleeOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    private SharedHandsSystem _hands = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _hands = sysManager.GetEntitySystem<SharedHandsSystem>();
    }

    private string? FindMeleeHand(EntityUid owner)
    {
        if (!_entManager.TryGetComponent<HandsComponent>(owner, out var handsComp))
            return null;

        foreach (var handName in handsComp.SortedHands)
        {
            if (_hands.GetHeldItem((owner, handsComp), handName) is not { } held)
                continue;

            // Prefer a dedicated melee weapon — has MeleeWeapon but no Gun.
            if (_entManager.HasComponent<MeleeWeaponComponent>(held) &&
                !_entManager.HasComponent<GunComponent>(held))
                return handName;
        }

        return null;
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        // Always valid — if no dedicated melee hand is found at execution time,
        // the MeleeCombatCompound will fall back to bare-hand attacks.
        return (true, null);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var handName = FindMeleeHand(owner);

        // No dedicated melee weapon found — stay on current hand
        // (MeleeCombatCompound will handle bare-fists or whatever is held).
        if (handName == null)
            return HTNOperatorStatus.Finished;

        if (!_entManager.TryGetComponent<HandsComponent>(owner, out var handsComp))
            return HTNOperatorStatus.Finished;

        if (handsComp.ActiveHandId != handName)
            _hands.TrySetActiveHand((owner, handsComp), handName);

        return HTNOperatorStatus.Finished;
    }
}
