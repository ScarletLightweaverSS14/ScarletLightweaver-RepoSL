using System.Linq;
using System.Numerics;
using Content.Server.Clothing.Systems;
using Content.Server.Hands.Systems;
using Content.Shared._Starlight.TowerDefense;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.UserInterface;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.TowerDefense;

public sealed partial class TowerDefenseSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private OutfitSystem _outfits = default!;
    [Dependency] private HandsSystem _hands = default!;
    private static readonly ProtoId<TowerDefenseArenaPrototype> FirstWave = "TowerDefenseFirstWave";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TowerDefenseArenaComponent, TowerDefenseMessage>(OnMessage);
        SubscribeLocalEvent<TowerDefenseArenaComponent, AfterActivatableUIOpenEvent>(OnOpen);
        SubscribeLocalEvent<TowerDefenseArenaComponent, ComponentShutdown>(OnShutdown);
    }

    private void Validate(TowerDefenseArenaPrototype config)
    {
        if (!TowerDefenseRoute.TryValidate(config.Route, config.Width, config.Height, out _) ||
            config.StartingCash < 0 || config.BaseHealth <= 0 ||
            !float.IsFinite(config.WaveBreak) || config.WaveBreak < 0 || config.Waves.Count == 0)
            throw new InvalidOperationException($"Invalid tower defense arena: {config.ID}");

        if (config.Towers.Count == 0 || config.Towers.Distinct().Count() != config.Towers.Count)
            throw new InvalidOperationException($"Invalid tower catalog: {config.ID}");
        foreach (var id in config.Towers)
        {
            var tower = _prototypes.Index(id);
            var entity = _prototypes.Index(tower.Entity);
            var gear = _prototypes.Index(tower.Gear);
            if (tower.Cost <= 0 || tower.Levels.Count == 0 || gear.Inhand.Count != 0 ||
                !entity.TryComp<HandsComponent>(out _, EntityManager.ComponentFactory) ||
                !entity.TryComp<InventoryComponent>(out _, EntityManager.ComponentFactory))
                throw new InvalidOperationException($"Invalid tower defense tower: {tower.ID}");

            for (var i = 0; i < tower.Levels.Count; i++)
            {
                var level = tower.Levels[i];
                if (!Positive(level.Damage) || !Positive(level.Range) || !Positive(level.Cooldown) ||
                    (i > 0 && level.UpgradeCost <= 0) ||
                    !_prototypes.Index(level.Weapon).TryComp<ItemComponent>(out _, EntityManager.ComponentFactory))
                    throw new InvalidOperationException($"Invalid level {i + 1} of tower {id}");
                if (level.ShotVisuals is { } visuals &&
                    (!_prototypes.Index(visuals).TryComp<HitscanBasicVisualsComponent>(out _, EntityManager.ComponentFactory) ||
                     !_prototypes.Index(level.Weapon).TryComp<GunComponent>(out _, EntityManager.ComponentFactory)))
                    throw new InvalidOperationException($"Invalid shot visuals for level {i + 1} of tower {id}");
            }
        }

        foreach (var wave in config.Waves)
        {
            if (!Positive(wave.SpawnDelay) || wave.Enemies.Count == 0 ||
                wave.Enemies.Any(e => e.Count <= 0 || e.Count > 1000))
                throw new InvalidOperationException($"Invalid wave in {config.ID}");
            foreach (var spawn in wave.Enemies)
            {
                if (!_prototypes.Index(spawn.Prototype).TryComp<TowerDefenseEnemyComponent>(out var enemy, EntityManager.ComponentFactory) ||
                    !Positive(enemy.MaxHealth) || !Positive(enemy.Speed) || enemy.Reward < 0 || enemy.BaseDamage <= 0)
                    throw new InvalidOperationException($"Invalid tower defense enemy: {spawn.Prototype}");
            }
        }
    }

    private static bool Positive(float value) => float.IsFinite(value) && value > 0;

    private void OnOpen(EntityUid uid, TowerDefenseArenaComponent arena, AfterActivatableUIOpenEvent args)
    {
        if (Exists(arena.Grid))
            Publish(uid, arena, _prototypes.Index(arena.Arena));
    }

    private void OnShutdown(EntityUid uid, TowerDefenseArenaComponent arena, ComponentShutdown args) => Clear(arena);

    private void Clear(TowerDefenseArenaComponent arena)
    {
        foreach (var enemy in arena.Enemies.Keys)
            QueueDel(enemy);
        foreach (var tower in arena.Towers.Values)
            QueueDel(tower);
        arena.Enemies.Clear();
        arena.Towers.Clear();
        arena.Pending.Clear();
    }

    private void Reset(TowerDefenseArenaComponent arena, TowerDefenseArenaPrototype config)
    {
        Clear(arena);
        arena.Cash = config.StartingCash;
        arena.BaseHealth = config.BaseHealth;
        arena.Wave = 0;
        arena.SpawnTimer = 0;
        arena.BreakTimer = 0;
        arena.Phase = TowerDefensePhase.Ready;
        arena.UiDirty = true;
    }

    private void OnMessage(EntityUid uid, TowerDefenseArenaComponent arena, TowerDefenseMessage message)
    {
        // BUI validates subscribers and interaction. Also bind purchases to this arena's map and vicinity.
        if (!Exists(arena.Grid) || !TryComp(message.Actor, out TransformComponent? actor) ||
            actor.MapUid != Transform(uid).MapUid ||
            Vector2.DistanceSquared(_transform.GetWorldPosition(actor), _transform.GetWorldPosition(uid)) > 400)
            return;

        var config = _prototypes.Index(arena.Arena);
        var playable = arena.Phase is TowerDefensePhase.Ready or TowerDefensePhase.Running or TowerDefensePhase.Break;
        string? feedback = null;
        switch (message.Action)
        {
            case TowerDefenseAction.Start when arena.Phase == TowerDefensePhase.Ready:
                StartWave(arena, config);
                break;
            case TowerDefenseAction.Reset when arena.Phase is TowerDefensePhase.Victory or TowerDefensePhase.Defeat:
                Reset(arena, config);
                break;
            case TowerDefenseAction.Place:
                feedback = playable ? Place(arena, config, message.Tile, message.Tower) : "td-error-game-ended";
                break;
            case TowerDefenseAction.Upgrade:
                feedback = playable ? Upgrade(arena, config, message) : "td-error-game-ended";
                break;
        }

        if (arena.UiDirty)
            Publish(uid, arena, config);
        if (feedback != null)
            _ui.ServerSendUiMessage(uid, TowerDefenseUiKey.Key, new TowerDefenseFeedbackMessage(feedback), message.Actor);
    }

    private void StartWave(TowerDefenseArenaComponent arena, TowerDefenseArenaPrototype config)
    {
        var wave = config.Waves[arena.Wave++];
        foreach (var group in wave.Enemies)
        for (var i = 0; i < group.Count; i++)
            arena.Pending.Enqueue(group.Prototype);
        arena.SpawnTimer = 0;
        arena.Phase = TowerDefensePhase.Running;
        arena.UiDirty = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<TowerDefenseArenaComponent>();
        while (query.MoveNext(out var uid, out var arena))
        {
            if (!Exists(arena.Grid))
                continue;

            var config = _prototypes.Index(arena.Arena);
            // Only arena-owned entities are inspected; no global enemy/target searches.
            foreach (var (tile, tower) in arena.Towers.ToArray())
            {
                if (TerminatingOrDeleted(tower))
                {
                    arena.Towers.Remove(tile);
                    arena.UiDirty = true;
                }
            }

            if (arena.Phase == TowerDefensePhase.Break)
            {
                arena.BreakTimer -= frameTime;
                if (arena.BreakTimer <= 0)
                    StartWave(arena, config);
            }
            if (arena.Phase == TowerDefensePhase.Running)
                TickArena(arena, config, frameTime);
            if (arena.UiDirty)
                Publish(uid, arena, config);
        }
    }

    private void TickArena(TowerDefenseArenaComponent arena, TowerDefenseArenaPrototype config, float frameTime)
    {
        arena.SpawnTimer -= frameTime;
        if (arena.Pending.Count > 0 && arena.SpawnTimer <= 0)
        {
            var enemy = Spawn(arena.Pending.Dequeue(), new EntityCoordinates(arena.Grid, TowerDefenseRoute.Center(config.Route[0])));
            if (!TryComp<TowerDefenseEnemyComponent>(enemy, out var health) ||
                !Positive(health.MaxHealth) || !Positive(health.Speed) || health.Reward < 0 || health.BaseDamage <= 0)
            {
                Log.Error($"Invalid tower defense enemy {ToPrettyString(enemy)}");
                QueueDel(enemy);
                arena.Phase = TowerDefensePhase.Defeat;
                arena.UiDirty = true;
                return;
            }
            health.Health = health.MaxHealth;
            Dirty(enemy, health);
            arena.Enemies.Add(enemy, 1);
            arena.SpawnTimer = config.Waves[arena.Wave - 1].SpawnDelay;
            arena.UiDirty = true;
        }

        foreach (var (enemy, waypoint) in arena.Enemies.ToArray())
        {
            if (TerminatingOrDeleted(enemy) || !TryComp<TowerDefenseEnemyComponent>(enemy, out var health))
            {
                arena.Enemies.Remove(enemy);
                arena.UiDirty = true;
                continue;
            }
            var next = waypoint;
            var position = Transform(enemy).LocalPosition;
            var moved = TowerDefenseRoute.Advance(config.Route, position, ref next, health.Speed * frameTime);
            _transform.SetLocalPosition(enemy, moved);
            // Face the direction of the current cardinal segment, including immediately after a corner.
            if (next < config.Route.Count)
                _transform.SetLocalRotation(enemy, (TowerDefenseRoute.Center(config.Route[next]) - moved).ToWorldAngle());
            arena.Enemies[enemy] = next;
            if (next < config.Route.Count)
                continue;

            arena.BaseHealth = Math.Max(0, arena.BaseHealth - health.BaseDamage);
            RemoveEnemy(arena, enemy);
            if (arena.BaseHealth == 0)
            {
                arena.Phase = TowerDefensePhase.Defeat;
                return;
            }
        }

        Attack(arena, config, frameTime);

        if (arena.Pending.Count != 0 || arena.Enemies.Count != 0)
            return;

        arena.Phase = arena.Wave == config.Waves.Count ? TowerDefensePhase.Victory : TowerDefensePhase.Break;
        arena.BreakTimer = config.WaveBreak;
        arena.UiDirty = true;
    }

    private void RemoveEnemy(TowerDefenseArenaComponent arena, EntityUid enemy)
    {
        arena.Enemies.Remove(enemy);
        QueueDel(enemy);
        arena.UiDirty = true;
    }

    private void Publish(EntityUid uid, TowerDefenseArenaComponent arena, TowerDefenseArenaPrototype config)
    {
        _ui.SetUiState(uid, TowerDefenseUiKey.Key, new TowerDefenseUiState
        {
            Width = config.Width, Height = config.Height,
            Cash = arena.Cash, BaseHealth = arena.BaseHealth,
            Wave = arena.Wave, TotalWaves = config.Waves.Count, Alive = arena.Enemies.Count,
            Remaining = arena.Pending.Count,
            Phase = arena.Phase, Road = new(arena.Road),
            Towers = arena.Towers.ToDictionary(pair => pair.Key, pair =>
            {
                var tower = Comp<TowerDefenseTowerComponent>(pair.Value);
                return TowerData(_prototypes.Index(tower.Definition), tower.Level);
            }),
            Catalog = config.Towers.Select(id => TowerData(_prototypes.Index(id), 0)).ToList(),
            Start = config.Route[0], End = config.Route[^1],
        });
        arena.UiDirty = false;
    }
}
