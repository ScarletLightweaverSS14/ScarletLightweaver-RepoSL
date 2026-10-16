using System.Numerics;
using Content.Shared._Starlight.TowerDefense;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.TowerDefense;

public sealed partial class TowerDefenseSystem
{
    private string Place(TowerDefenseArenaComponent arena, TowerDefenseArenaPrototype config, Vector2i tile, string? id)
    {
        if (id == null || !config.Towers.Contains(new ProtoId<TowerDefenseTowerPrototype>(id)))
            return "td-error-invalid-tower";
        var definition = _prototypes.Index<TowerDefenseTowerPrototype>(id);
        if (tile.X < 0 || tile.Y < 0 || tile.X >= config.Width || tile.Y >= config.Height ||
            arena.Road.Contains(tile) || arena.Towers.ContainsKey(tile))
            return "td-error-invalid-placement";
        if (arena.Cash < definition.Cost)
            return "td-error-insufficient-cash";

        var uid = Spawn(definition.Entity, new EntityCoordinates(arena.Grid, TowerDefenseRoute.Center(tile)));
        var tower = EnsureComp<TowerDefenseTowerComponent>(uid);
        tower.Definition = definition.ID;
        if (!_outfits.SetOutfit(uid, definition.Gear.Id, unremovable: true) || !EquipWeapon(uid, tower, definition.Levels[0]))
        {
            QueueDel(uid);
            return "td-error-equipment";
        }

        arena.Towers.Add(tile, uid);
        arena.Cash -= definition.Cost;
        arena.UiDirty = true;
        return "td-purchase-success";
    }

    private string Upgrade(TowerDefenseArenaComponent arena, TowerDefenseArenaPrototype config, TowerDefenseMessage message)
    {
        if (!arena.Towers.TryGetValue(message.Tile, out var uid) || TerminatingOrDeleted(uid) ||
            !TryComp<TowerDefenseTowerComponent>(uid, out var tower) ||
            tower.Definition.Id != message.Tower || !config.Towers.Contains(tower.Definition))
            return "td-error-invalid-upgrade";
        if (message.ExpectedLevel != tower.Level + 1)
            return "td-error-stale-upgrade";

        var definition = _prototypes.Index(tower.Definition);
        if (tower.Level + 1 >= definition.Levels.Count)
            return "td-error-max-level";
        var next = definition.Levels[tower.Level + 1];
        if (arena.Cash < next.UpgradeCost)
            return "td-error-insufficient-cash";
        if (!EquipWeapon(uid, tower, next))
            return "td-error-equipment";

        arena.Cash -= next.UpgradeCost;
        tower.Level++;
        tower.AttackTimer = Math.Min(tower.AttackTimer, next.Cooldown);
        arena.UiDirty = true;
        return "td-upgrade-success";
    }

    private bool EquipWeapon(EntityUid uid, TowerDefenseTowerComponent tower, TowerDefenseTowerLevel level)
    {
        var weapon = Spawn(level.Weapon, Transform(uid).Coordinates);
        var previous = tower.Weapon;
        if (previous is { } held && !TerminatingOrDeleted(held) &&
            !_hands.TryDrop(uid, held, checkActionBlocker: false, doDropInteraction: false))
        {
            QueueDel(weapon);
            return false;
        }

        if (!_hands.TryPickup(uid, weapon, checkActionBlocker: false, animate: false))
        {
            QueueDel(weapon);
            if (previous is { } old && !TerminatingOrDeleted(old))
                _hands.TryPickup(uid, old, checkActionBlocker: false, animate: false);
            return false;
        }

        if (previous is { } replaced)
            QueueDel(replaced);
        tower.Weapon = weapon;
        return true;
    }

    private static TowerDefenseTowerUiData TowerData(TowerDefenseTowerPrototype definition, int index)
    {
        var level = definition.Levels[index];
        return new TowerDefenseTowerUiData
        {
            Id = definition.ID, Name = definition.Name, Symbol = definition.Symbol,
            LevelName = level.Name, Level = index + 1, MaxLevel = definition.Levels.Count, Cost = definition.Cost,
            UpgradeCost = index + 1 < definition.Levels.Count ? definition.Levels[index + 1].UpgradeCost : null,
            Damage = level.Damage, Range = level.Range, Cooldown = level.Cooldown,
        };
    }

    private void Attack(TowerDefenseArenaComponent arena, TowerDefenseArenaPrototype config, float frameTime)
    {
        foreach (var (tile, uid) in arena.Towers)
        {
            var tower = Comp<TowerDefenseTowerComponent>(uid);
            var level = _prototypes.Index(tower.Definition).Levels[tower.Level];
            tower.AttackTimer = Math.Max(0, tower.AttackTimer - frameTime);
            if (tower.AttackTimer > 0)
                continue;

            var origin = TowerDefenseRoute.Center(tile);
            bool InRange(EntityUid target) => arena.Enemies.ContainsKey(target) &&
                !TerminatingOrDeleted(target) && Transform(target).ParentUid == arena.Grid &&
                Vector2.DistanceSquared(origin, Transform(target).LocalPosition) <= level.Range * level.Range;

            // Keep an acquired target until it is gone or outside range.
            if (tower.Target is not { } current || !InRange(current))
            {
                tower.Target = null;
                var furthestWaypoint = -1;
                var shortestRemaining = float.PositiveInfinity;
                foreach (var (enemy, next) in arena.Enemies)
                {
                    if (!InRange(enemy) || next >= config.Route.Count)
                        continue;
                    var remaining = Vector2.DistanceSquared(Transform(enemy).LocalPosition, TowerDefenseRoute.Center(config.Route[next]));
                    if (next < furthestWaypoint || (next == furthestWaypoint && remaining >= shortestRemaining))
                        continue;
                    tower.Target = enemy;
                    furthestWaypoint = next;
                    shortestRemaining = remaining;
                }
            }

            if (tower.Target is not { } target)
                continue;
            if (tower.Weapon is { } weapon && !TerminatingOrDeleted(weapon))
                PlayAttackFeedback(uid, weapon, target, level);
            var health = Comp<TowerDefenseEnemyComponent>(target);
            health.Health = Math.Max(0, health.Health - level.Damage);
            Dirty(target, health);
            tower.AttackTimer = level.Cooldown;
            _transform.SetLocalRotation(uid, (Transform(target).LocalPosition - origin).ToWorldAngle());
            if (health.Health > 0)
                continue;
            arena.Cash += health.Reward;
            RemoveEnemy(arena, target);
            tower.Target = null;
        }
    }
}
