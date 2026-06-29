using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
/// When an NPC has already reloaded once and is now completely out of ammo with no spare
/// magazines, this operator "breaks" the gun:
///   1. Spawns the broken variant (prototype ID + "Broken") at the NPC's feet.
///   2. Removes the gun from the NPC's hand and queues it for deletion.
/// After this the NPC falls through to melee combat.
/// The broken entity has no GunComponent so NPCs will never try to pick it up.
/// </summary>
[UsedImplicitly]
public sealed partial class GunBreakOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    private SharedHandsSystem _hands = default!;
    private SharedTransformSystem _xforms = default!;

    /// <summary>Blackboard key set by ReloadFromPocketOperator after a successful reload.</summary>
    public const string HasReloadedKey = "_GunHasBeenReloaded";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _hands  = sysManager.GetEntitySystem<SharedHandsSystem>();
        _xforms = sysManager.GetEntitySystem<SharedTransformSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        // Only valid if the NPC has already reloaded at least once this life.
        if (!blackboard.TryGetValue<bool>(HasReloadedKey, out var reloaded, _entManager) || !reloaded)
            return (false, null);

        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_hands.TryGetActiveItem(owner, out var heldItem) || heldItem == null)
            return (false, null);

        if (!_entManager.HasComponent<GunComponent>(heldItem.Value))
            return (false, null);

        return (true, null);
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);

        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_hands.TryGetActiveItem(owner, out var heldItem) || heldItem == null)
            return;

        if (!_entManager.HasComponent<GunComponent>(heldItem.Value))
            return;

        // Resolve the broken prototype name.
        var meta = _entManager.GetComponent<MetaDataComponent>(heldItem.Value);
        var protoId = meta.EntityPrototype?.ID;
        var brokenId = protoId != null ? protoId + "Broken" : null;

        // Get world coordinates *before* dropping so the gun lands at the NPC's feet.
        var coords = _xforms.GetMoverCoordinates(owner);

        // Drop the gun so it leaves the hand container cleanly, then delete it.
        _hands.TryDrop((owner, null), heldItem.Value, coords, checkActionBlocker: false, doDropInteraction: false);
        _entManager.QueueDeleteEntity(heldItem.Value);

        // Spawn broken variant if one is defined.
        if (brokenId != null && _protoManager.HasIndex<EntityPrototype>(brokenId))
            _entManager.SpawnEntity(brokenId, coords);

        // Clear the reload flag so the operator won't trigger again if somehow the NPC gets another gun.
        blackboard.Remove<bool>(HasReloadedKey);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        // All work done in Startup — finish immediately.
        return HTNOperatorStatus.Finished;
    }
}
