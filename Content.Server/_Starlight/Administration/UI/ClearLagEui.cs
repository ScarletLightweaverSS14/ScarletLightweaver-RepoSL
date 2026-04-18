using Content.Server.Administration.Logs;
using Content.Server.EUI;
using Content.Server.Storage.EntitySystems;
using Content.Shared._Starlight.Administration;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Eui;
using Content.Shared.Mobs.Components;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Tag;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.IoC;

namespace Content.Server._Starlight.Administration.UI;

/// <summary>
/// Server-side EUI for the admin Clear Lag tool.
/// Counts and deletes lag-causing entities by category.
/// </summary>
public sealed class ClearLagEui : BaseEui
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;

    private TagSystem _tagSystem = default!;
    private EntityStorageSystem _entityStorageSystem = default!;

    private const string TrashTag = "Trash";
    private const string SovietCrateProtoId = "CrateSovietArmaments";

    public ClearLagEui()
    {
        IoCManager.InjectDependencies(this);
    }

    public override void Opened()
    {
        _tagSystem = _entities.System<TagSystem>();
        _entityStorageSystem = _entities.System<EntityStorageSystem>();
        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        return new ClearLagEuiState
        {
            TrashCount = CountTrash(),
            SpentCasingsCount = CountSpentCasings(),
            SovietArmsCratesCount = CountSovietCrates(),
        };
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        switch (msg)
        {
            case ClearLagEuiMsg.DoClear clearMsg:
                DoClear(clearMsg.Category);
                StateDirty();
                break;
            case ClearLagEuiMsg.RequestRefresh:
                StateDirty();
                break;
        }
    }

    // ── Counting ──────────────────────────────────────────────────────────────

    private int CountTrash()
    {
        var count = 0;
        var query = _entities.AllEntityQueryEnumerator<TagComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var tag, out var meta))
        {
            // Spent casings are counted in their own category; don't double-count.
            if (_entities.TryGetComponent(uid, out CartridgeAmmoComponent? cartridge) && cartridge.Spent)
                continue;

            if (IsTrash(uid, tag))
                count++;
        }
        return count;
    }

    private int CountSpentCasings()
    {
        var count = 0;
        var query = _entities.AllEntityQueryEnumerator<CartridgeAmmoComponent>();
        while (query.MoveNext(out _, out var cartridge))
        {
            if (cartridge.Spent)
                count++;
        }
        return count;
    }

    private int CountSovietCrates()
    {
        var count = 0;
        var query = _entities.AllEntityQueryEnumerator<MetaDataComponent, EntityStorageComponent>();
        while (query.MoveNext(out _, out var meta, out _))
        {
            if (meta.EntityPrototype?.ID == SovietCrateProtoId)
                count++;
        }
        return count;
    }

    // ── Clearing ──────────────────────────────────────────────────────────────

    private void DoClear(ClearLagCategory category)
    {
        switch (category)
        {
            case ClearLagCategory.Trash:
                ClearTrash();
                _adminLog.Add(LogType.AdminMessage, LogImpact.Medium,
                    $"{Player} used Clear Lag to delete trash entities.");
                break;
            case ClearLagCategory.SpentCasings:
                ClearSpentCasings();
                _adminLog.Add(LogType.AdminMessage, LogImpact.Low,
                    $"{Player} used Clear Lag to delete spent casings.");
                break;
            case ClearLagCategory.SovietArmsCrates:
                ClearSovietCrates();
                _adminLog.Add(LogType.AdminMessage, LogImpact.Medium,
                    $"{Player} used Clear Lag to empty and delete Soviet arms crates.");
                break;
        }
    }

    private void ClearTrash()
    {
        var toDelete = new List<EntityUid>();
        var query = _entities.AllEntityQueryEnumerator<TagComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var tag, out _))
        {
            // Spent casings belong to their own category.
            if (_entities.TryGetComponent(uid, out CartridgeAmmoComponent? cartridge) && cartridge.Spent)
                continue;

            if (IsTrash(uid, tag))
                toDelete.Add(uid);
        }
        foreach (var uid in toDelete)
            _entities.QueueDeleteEntity(uid);
    }

    private void ClearSpentCasings()
    {
        var toDelete = new List<EntityUid>();
        var query = _entities.AllEntityQueryEnumerator<CartridgeAmmoComponent>();
        while (query.MoveNext(out var uid, out var cartridge))
        {
            if (cartridge.Spent)
                toDelete.Add(uid);
        }
        foreach (var uid in toDelete)
            _entities.QueueDeleteEntity(uid);
    }

    private void ClearSovietCrates()
    {
        var toOpen = new List<(EntityUid Uid, EntityStorageComponent Storage)>();
        var query = _entities.AllEntityQueryEnumerator<MetaDataComponent, EntityStorageComponent>();
        while (query.MoveNext(out var uid, out var meta, out var storage))
        {
            if (meta.EntityPrototype?.ID == SovietCrateProtoId)
                toOpen.Add((uid, storage));
        }

        foreach (var (uid, storage) in toOpen)
        {
            // Eject all contents into the world before deletion so nothing is voided.
            if (!storage.Open)
                _entityStorageSystem.OpenStorage(uid, storage);
            _entities.QueueDeleteEntity(uid);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if this entity is safe to auto-delete as floor clutter.
    /// Rules:
    ///   MUST have the "Trash" tag.
    ///   MUST NOT have a StorageComponent (trash bags, bins — may contain player items).
    ///   MUST NOT have an EntityStorageComponent (crates, lockers).
    ///   MUST NOT be anchored (placed structures).
    ///   MUST NOT have a MobStateComponent (living or dead creatures).
    /// </summary>
    private bool IsTrash(EntityUid uid, TagComponent tag)
    {
        if (!_tagSystem.HasTag(tag, TrashTag))
            return false;

        // Skip containers — they may hold player inventory.
        if (_entities.HasComponent<StorageComponent>(uid))
            return false;

        if (_entities.HasComponent<EntityStorageComponent>(uid))
            return false;

        // Skip anchored structures (e.g. trash chutes, wall-mounted bins).
        if (_entities.TryGetComponent(uid, out TransformComponent? xform) && xform.Anchored)
            return false;

        // Skip mobs — nothing with a health state should ever be auto-deleted.
        if (_entities.HasComponent<MobStateComponent>(uid))
            return false;

        return true;
    }
}
