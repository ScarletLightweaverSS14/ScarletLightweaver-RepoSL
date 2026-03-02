using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._Starlight.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
/// When the NPC's active-hand gun is empty, ejects the spent magazine (if any)
/// then inserts a fresh magazine found in pockets.
/// For guns with a ChamberMagazineAmmoProvider (bolt-action/semi-auto), closes the bolt
/// after inserting the magazine so the chamber is ready.
/// </summary>
[UsedImplicitly]
public sealed partial class ReloadFromPocketOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    private InventorySystem _inventory = default!;
    private ItemSlotsSystem _itemSlots = default!;
    private SharedHandsSystem _hands = default!;
    private SharedGunSystem _gunSystem = default!;
    private NPCSteeringSystem _steering = default!;
    private SharedTransformSystem _xforms = default!;

    /// <summary>How often (seconds) to pick a new random strafe waypoint while reloading.</summary>
    [DataField]
    public float StrafeInterval = 0.55f;

    private const string StrafeTimerKey = "_ReloadStrafeTimer";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _inventory  = sysManager.GetEntitySystem<InventorySystem>();
        _itemSlots  = sysManager.GetEntitySystem<ItemSlotsSystem>();
        _hands      = sysManager.GetEntitySystem<SharedHandsSystem>();
        _gunSystem  = sysManager.GetEntitySystem<SharedGunSystem>();
        _steering   = sysManager.GetEntitySystem<NPCSteeringSystem>();
        _xforms     = sysManager.GetEntitySystem<SharedTransformSystem>();
    }

    private bool GunHasEmptyMagazine(EntityUid gun)
    {
        if (!_entManager.TryGetComponent<ItemSlotsComponent>(gun, out var slots))
            return false;

        foreach (var (_, gunSlot) in slots.Slots)
        {
            if (gunSlot.ContainerSlot?.ContainedEntity is not { } existing)
                continue;

            // Consider it empty if it has a BallisticAmmoProvider with Count == 0
            if (_entManager.TryGetComponent<BallisticAmmoProviderComponent>(existing, out var bap) &&
                bap.Count == 0)
                return true;
        }

        return false;
    }

    private bool EjectEmptyMagazine(EntityUid owner, EntityUid gun)
    {
        if (!_entManager.TryGetComponent<ItemSlotsComponent>(gun, out var slots))
            return false;

        foreach (var (id, gunSlot) in slots.Slots)
        {
            if (gunSlot.ContainerSlot?.ContainedEntity is not { } existing)
                continue;

            if (_entManager.TryGetComponent<BallisticAmmoProviderComponent>(existing, out var bap) &&
                bap.Count == 0)
            {
                // Eject the empty magazine — try to return it to a pocket so it isn't wasted on the floor.
                if (_itemSlots.TryEject(gun, id, null, out var ejected) && ejected.HasValue)
                    TryReturnToPocket(owner, ejected.Value);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to put <paramref name="item"/> into one of the owner's pocket slots.
    /// If no pocket space is available the item remains wherever it landed (floor).
    /// </summary>
    private void TryReturnToPocket(EntityUid owner, EntityUid item)
    {
        var enumerator = _inventory.GetSlotEnumerator(owner, SlotFlags.POCKET);
        while (enumerator.MoveNext(out var pocketSlot))
        {
            if (pocketSlot.ContainedEntity != null)
                continue; // pocket full

            if (pocketSlot.ID != null &&
                _inventory.TryEquip(owner, item, pocketSlot.ID, silent: true, force: false))
                return;
        }
        // No free pocket — item stays on the floor.
    }

    private EntityUid? FindPocketMagForGun(EntityUid owner, EntityUid gun, out string? slotId)
    {
        slotId = null;

        if (!_entManager.TryGetComponent<ItemSlotsComponent>(gun, out var slots))
            return null;

        var enumerator = _inventory.GetSlotEnumerator(owner, SlotFlags.POCKET);
        while (enumerator.MoveNext(out var pocketSlot))
        {
            if (pocketSlot.ContainedEntity is not { } mag)
                continue;

            // Skip magazines that are already empty — inserting them would loop forever.
            if (_entManager.TryGetComponent<BallisticAmmoProviderComponent>(mag, out var pocketBap) &&
                pocketBap.Count == 0)
                continue;

            foreach (var (id, gunSlot) in slots.Slots)
            {
                if (gunSlot.ContainerSlot?.ContainedEntity != null)
                    continue;

                if (!_itemSlots.CanInsert(gun, mag, owner, gunSlot))
                    continue;

                slotId = id;
                return mag;
            }
        }

        return null;
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_hands.TryGetActiveItem(owner, out var heldGun) || heldGun == null)
            return (false, null);

        if (!_entManager.HasComponent<GunComponent>(heldGun.Value))
            return (false, null);

        // Valid if: a replacement mag is in pockets OR there's an empty mag to eject first
        var mag = FindPocketMagForGun(owner, heldGun.Value, out _);
        if (mag != null)
            return (true, null);

        // If the gun has an empty magazine we still plan as valid —
        // Update() will eject it, after which the next plan will find the open slot.
        if (GunHasEmptyMagazine(heldGun.Value))
            return (true, null);

        return (false, null);
    }

    // Blackboard keys for the two delay timers.
    private const string InsertTimerKey = "_ReloadInsertTimer";
    private const string BoltTimerKey   = "_ReloadBoltTimer";

    /// <summary>Seconds to wait after ejecting the empty mag before inserting the fresh one.</summary>
    [DataField]
    public float InsertDelay = 0.6f;

    /// <summary>Seconds to wait after inserting the mag before closing the bolt.</summary>
    [DataField]
    public float BoltDelay = 0.8f;

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);

        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        // Visually open the bolt so there's always a clear open → close animation.
        if (_hands.TryGetActiveItem(owner, out var gun) && gun != null &&
            _entManager.TryGetComponent<ChamberMagazineAmmoProviderComponent>(gun.Value, out var chamber))
        {
            _gunSystem.SetBoltClosed(gun.Value, chamber, false);
        }

        // Kick off the first strafe immediately.
        PickNewStrafeTarget(owner, blackboard);
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        CleanupAndFinish(blackboard);
    }

    public override void PlanShutdown(NPCBlackboard blackboard)
    {
        base.PlanShutdown(blackboard);
        CleanupAndFinish(blackboard);
    }

    /// <summary>
    /// Called on any shutdown path. If the bolt-delay was pending (mag already inserted),
    /// close the bolt immediately so the gun is left in a usable state.
    /// </summary>
    private void CleanupAndFinish(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (blackboard.TryGetValue<float>(BoltTimerKey, out _, _entManager))
        {
            blackboard.Remove<float>(BoltTimerKey);
            // Mag is already in — close the bolt so the NPC can shoot.
            if (_hands.TryGetActiveItem(owner, out var gun) && gun != null)
                CloseBolt(gun.Value);
        }

        if (blackboard.TryGetValue<float>(InsertTimerKey, out _, _entManager))
            blackboard.Remove<float>(InsertTimerKey);

        if (blackboard.TryGetValue<float>(StrafeTimerKey, out _, _entManager))
            blackboard.Remove<float>(StrafeTimerKey);

        // Stop the strafe movement so we don't drift after reload finishes.
        _steering.Unregister(owner);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        // ── Strafe tick — runs every phase so the NPC moves continuously ──────────
        if (blackboard.TryGetValue<float>(StrafeTimerKey, out var strafeTimer, _entManager))
        {
            strafeTimer -= frameTime;
            if (strafeTimer <= 0f)
                PickNewStrafeTarget(owner, blackboard);
            else
                blackboard.SetValue(StrafeTimerKey, strafeTimer);
        }

        // ── Phase 3: bolt-close delay ─────────────────────────────────────────────
        if (blackboard.TryGetValue<float>(BoltTimerKey, out var boltTimer, _entManager))
        {
            boltTimer -= frameTime;
            if (boltTimer > 0f)
            {
                blackboard.SetValue(BoltTimerKey, boltTimer);
                return HTNOperatorStatus.Continuing;
            }

            blackboard.Remove<float>(BoltTimerKey);
            if (_hands.TryGetActiveItem(owner, out var pendingGun) && pendingGun != null)
                CloseBolt(pendingGun.Value);

            // Mark that this NPC has completed at least one reload.
            blackboard.SetValue(GunBreakOperator.HasReloadedKey, true);
            return HTNOperatorStatus.Finished;
        }

        // ── Phase 2: insert-delay countdown then insert ───────────────────────────
        if (blackboard.TryGetValue<float>(InsertTimerKey, out var insertTimer, _entManager))
        {
            insertTimer -= frameTime;
            if (insertTimer > 0f)
            {
                blackboard.SetValue(InsertTimerKey, insertTimer);
                return HTNOperatorStatus.Continuing;
            }

            blackboard.Remove<float>(InsertTimerKey);

            if (!_hands.TryGetActiveItem(owner, out var insertGun) || insertGun == null)
                return HTNOperatorStatus.Failed;

            if (!_entManager.HasComponent<GunComponent>(insertGun.Value))
                return HTNOperatorStatus.Failed;

            var mag2 = FindPocketMagForGun(owner, insertGun.Value, out var slotId2);
            if (mag2 == null || slotId2 == null)
                return HTNOperatorStatus.Failed;

            if (!_itemSlots.TryInsert(insertGun.Value, slotId2, mag2.Value, owner))
                return HTNOperatorStatus.Failed;

            // Start the bolt-close delay.
            blackboard.SetValue(BoltTimerKey, BoltDelay);
            return HTNOperatorStatus.Continuing;
        }

        // ── Phase 1: eject empty mag, then start insert-delay ────────────────────
        if (!_hands.TryGetActiveItem(owner, out var heldGun) || heldGun == null)
            return HTNOperatorStatus.Failed;

        if (!_entManager.HasComponent<GunComponent>(heldGun.Value))
            return HTNOperatorStatus.Failed;

        EjectEmptyMagazine(owner, heldGun.Value);

        // Start the insert delay — the actual insert happens once it expires.
        blackboard.SetValue(InsertTimerKey, InsertDelay);
        return HTNOperatorStatus.Continuing;
    }

    /// <summary>
    /// Picks a random nearby waypoint and registers it with the steering system so
    /// the NPC shuffles around unpredictably while reloading.
    /// </summary>
    private void PickNewStrafeTarget(EntityUid owner, NPCBlackboard blackboard)
    {
        var pos = _xforms.GetMoverCoordinates(owner);
        // Random offset 2–4 tiles in a random direction.
        var angle = _random.NextFloat(0f, MathF.PI * 2f);
        var dist  = _random.NextFloat(2f, 4f);
        var offset = new Vector2(MathF.Cos(angle) * dist, MathF.Sin(angle) * dist);
        var target = pos.Offset(offset);

        _steering.Register(owner, target);
        blackboard.SetValue(StrafeTimerKey, StrafeInterval);
    }

    /// <summary>
    /// Close the bolt on guns that have one (i.e. chamber a round from the newly inserted magazine).
    /// Sawn-offs and similar use BallisticAmmoProvider directly — no bolt, skip them.
    /// </summary>
    private void CloseBolt(EntityUid gun)
    {
        if (_entManager.TryGetComponent<ChamberMagazineAmmoProviderComponent>(gun, out var chamber))
            _gunSystem.SetBoltClosed(gun, chamber, true);
    }
}

