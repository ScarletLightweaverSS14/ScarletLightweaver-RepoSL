using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._Starlight.TowerDefense;
using Content.Server.Hands.Systems;
using Content.Shared._Starlight.TowerDefense;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Starlight.TowerDefense;

public sealed class TowerDefenseTest : GameTest
{
    [Test]
    public async Task FirstWavePurchasesDefeatResetAndVictory()
    {
        var testMap = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var player = SEntMan.SpawnEntity("MobHuman", new MapCoordinates(Vector2.Zero, testMap.MapId));
            var system = SEntMan.System<TowerDefenseSystem>();
            var console = system.CreateArena(player, out var mapId);
            var map = SEntMan.System<SharedMapSystem>().GetMapOrInvalid(mapId);
            try
            {
                var arena = SEntMan.GetComponent<TowerDefenseArenaComponent>(console);
                void Send(TowerDefenseAction action, Vector2i tile = default, string tower = null) =>
                    SEntMan.EventBus.RaiseLocalEvent(console, new TowerDefenseMessage(action, tile, tower) { Actor = player });

                // Road tiles, the surrounding deck, and forged shop entries cannot accept towers.
                Send(TowerDefenseAction.Place, new(0, 7), "TowerDefenseTider");
                Send(TowerDefenseAction.Place, new(-1, 0), "TowerDefenseTider");
                Send(TowerDefenseAction.Place, new(13, 0), "TowerDefenseTider");
                Send(TowerDefenseAction.Place, new(5, 5), "MobHuman");
                Send(TowerDefenseAction.Place, new(5, 5));
                Assert.That(arena.Cash, Is.EqualTo(400));
                Assert.That(arena.Towers, Is.Empty);

                Send(TowerDefenseAction.Start);
                Send(TowerDefenseAction.Start); // A repeated request must not enqueue another wave.
                Assert.That(arena.Pending.Count, Is.EqualTo(5));
                for (var i = 0; i < 600; i++)
                    system.Update(0.1f);
                Assert.That(arena.Phase, Is.EqualTo(TowerDefensePhase.Defeat));
                Assert.That(arena.BaseHealth, Is.Zero);
                Assert.That(arena.Cash, Is.EqualTo(400), "Escaped enemies must not award cash.");

                Send(TowerDefenseAction.Reset);
                Assert.That(arena.Phase, Is.EqualTo(TowerDefensePhase.Ready));
                Assert.That(arena.BaseHealth, Is.EqualTo(5));
                Send(TowerDefenseAction.Place, new(9, 6), "TowerDefenseTider");
                Send(TowerDefenseAction.Place, new(9, 6), "TowerDefenseTider"); // Duplicate placement has no side effects.
                Send(TowerDefenseAction.Place, new(6, 5), "TowerDefenseSecurity");
                Assert.That(arena.Towers.Count, Is.EqualTo(2));
                Assert.That(arena.Cash, Is.EqualTo(250));

                Send(TowerDefenseAction.Start);
                for (var i = 0; i < 600; i++)
                    system.Update(0.1f);
                Assert.That(arena.Phase, Is.EqualTo(TowerDefensePhase.Victory));
                Assert.That(arena.BaseHealth, Is.GreaterThan(0));
                Assert.That(arena.Cash, Is.GreaterThan(250));
                Assert.That(arena.Enemies, Is.Empty);

                var cash = arena.Cash;
                Send(TowerDefenseAction.Place, new(0, 0), "TowerDefenseTider");
                Assert.That(arena.Cash, Is.EqualTo(cash), "Purchases are rejected after the game ends.");
                Send(TowerDefenseAction.Reset);
                Assert.That(arena.Towers, Is.Empty);
                Assert.That(arena.Pending, Is.Empty);
                Assert.That(arena.Wave, Is.Zero);
            }
            finally
            {
                if (SEntMan.EntityExists(map))
                    SEntMan.DeleteEntity(map);
            }
        });
    }

    [TestCase("TowerDefenseTider")]
    [TestCase("TowerDefenseSecurity")]
    public async Task UpgradesReplaceHeldWeaponAndRejectStaleUnaffordableAndForeignRequests(string id)
    {
        var testMap = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var player = SEntMan.SpawnEntity("MobHuman", new MapCoordinates(Vector2.Zero, testMap.MapId));
            var foreignPlayer = SEntMan.SpawnEntity("MobHuman", new MapCoordinates(Vector2.Zero, testMap.MapId));
            var system = SEntMan.System<TowerDefenseSystem>();
            var console = system.CreateArena(player, out var mapId);
            var map = SEntMan.System<SharedMapSystem>().GetMapOrInvalid(mapId);
            try
            {
                var arena = SEntMan.GetComponent<TowerDefenseArenaComponent>(console);
                var definition = SProtoMan.Index<TowerDefenseTowerPrototype>(id);
                var tile = new Vector2i(5, 5);
                void Send(TowerDefenseAction action, int expectedLevel = 0, string requestedId = null, EntityUid? actor = null) =>
                    SEntMan.EventBus.RaiseLocalEvent(console,
                        new TowerDefenseMessage(action, tile, requestedId ?? id, expectedLevel) { Actor = actor ?? player });

                Send(TowerDefenseAction.Place, actor: foreignPlayer);
                Assert.That(arena.Towers, Is.Empty, "An actor on another map cannot buy in this arena.");
                Assert.That(arena.Cash, Is.EqualTo(400));

                Send(TowerDefenseAction.Place);
                var entity = arena.Towers[tile];
                var tower = SEntMan.GetComponent<TowerDefenseTowerComponent>(entity);
                var hands = SEntMan.System<HandsSystem>();
                void AssertWeapon(int level)
                {
                    var held = hands.EnumerateHeld(entity).ToArray();
                    Assert.That(held, Has.Length.EqualTo(1), "Every tier must visibly hold exactly one weapon.");
                    Assert.That(held[0], Is.EqualTo(tower.Weapon));
                    Assert.That(SEntMan.GetComponent<MetaDataComponent>(held[0]).EntityPrototype?.ID,
                        Is.EqualTo(definition.Levels[level].Weapon.Id));
                    Assert.That(tower.Level, Is.EqualTo(level));
                }

                AssertWeapon(0);
                Assert.That(arena.Cash, Is.EqualTo(400 - definition.Cost));
                var previousWeapon = tower.Weapon!.Value;
                var previousCash = arena.Cash;
                Send(TowerDefenseAction.Upgrade, 1, "MobHuman");
                Send(TowerDefenseAction.Upgrade, 0);
                Send(TowerDefenseAction.Upgrade, 1, actor: foreignPlayer);
                Assert.That(arena.Cash, Is.EqualTo(previousCash));
                AssertWeapon(0);

                for (var nextLevel = 1; nextLevel < definition.Levels.Count; nextLevel++)
                {
                    var price = definition.Levels[nextLevel].UpgradeCost;
                    arena.Cash = price - 1;
                    Send(TowerDefenseAction.Upgrade, nextLevel);
                    Assert.That(arena.Cash, Is.EqualTo(price - 1));
                    Assert.That(tower.Weapon, Is.EqualTo(previousWeapon));
                    AssertWeapon(nextLevel - 1);

                    arena.Cash = price;
                    tower.AttackTimer = 0.2f;
                    Send(TowerDefenseAction.Upgrade, nextLevel);
                    Assert.That(arena.Cash, Is.Zero);
                    Assert.That(tower.AttackTimer, Is.EqualTo(0.2f), "Buying a tier must not grant a free attack.");
                    AssertWeapon(nextLevel);
                    Assert.That(tower.Weapon, Is.Not.EqualTo(previousWeapon));
                    Assert.That(SEntMan.IsQueuedForDeletion(previousWeapon), Is.True,
                        "The replaced weapon must be removed rather than dropped onto the arena floor.");
                    previousWeapon = tower.Weapon!.Value;

                    arena.Cash = 1000;
                    Send(TowerDefenseAction.Upgrade, nextLevel); // Repeat a request carrying the old level.
                    Assert.That(arena.Cash, Is.EqualTo(1000));
                    AssertWeapon(nextLevel);
                }

                Send(TowerDefenseAction.Upgrade, definition.Levels.Count);
                Assert.That(arena.Cash, Is.EqualTo(1000), "The maximum tier cannot charge for a further upgrade.");
                AssertWeapon(definition.Levels.Count - 1);
            }
            finally
            {
                if (SEntMan.EntityExists(map))
                    SEntMan.DeleteEntity(map);
            }
        });
    }

    [Test]
    public async Task TargetingRetainsValidTargetAndUpgradeChangesCombat()
    {
        var testMap = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var player = SEntMan.SpawnEntity("MobHuman", new MapCoordinates(Vector2.Zero, testMap.MapId));
            var system = SEntMan.System<TowerDefenseSystem>();
            var console = system.CreateArena(player, out var mapId);
            var map = SEntMan.System<SharedMapSystem>().GetMapOrInvalid(mapId);
            try
            {
                var arena = SEntMan.GetComponent<TowerDefenseArenaComponent>(console);
                var tile = new Vector2i(6, 5);
                void Send(TowerDefenseAction action, int expectedLevel = 0) =>
                    SEntMan.EventBus.RaiseLocalEvent(console,
                        new TowerDefenseMessage(action, tile, "TowerDefenseSecurity", expectedLevel) { Actor = player });
                Send(TowerDefenseAction.Place);
                var tower = SEntMan.GetComponent<TowerDefenseTowerComponent>(arena.Towers[tile]);
                var transform = SEntMan.System<SharedTransformSystem>();
                EntityUid Enemy(Vector2 position, int next)
                {
                    var enemy = SEntMan.SpawnEntity("TowerDefenseCarp", new EntityCoordinates(arena.Grid, position));
                    var health = SEntMan.GetComponent<TowerDefenseEnemyComponent>(enemy);
                    health.Health = health.MaxHealth = 1000;
                    health.Speed = 0.001f;
                    arena.Enemies.Add(enemy, next);
                    return enemy;
                }

                // Both are on the westbound middle segment; the western carp has traveled farther.
                var leading = Enemy(new(4.5f, 4.5f), 3);
                var trailing = Enemy(new(5.5f, 4.5f), 3);
                arena.Phase = TowerDefensePhase.Running;
                arena.Wave = 1;
                system.Update(0.01f);
                Assert.That(tower.Target, Is.EqualTo(leading));
                Assert.That(SEntMan.GetComponent<TowerDefenseEnemyComponent>(leading).Health, Is.EqualTo(992));
                Assert.That(SEntMan.GetComponent<TowerDefenseEnemyComponent>(trailing).Health, Is.EqualTo(1000));

                // Even a newly leading candidate does not steal a still-valid target between shots.
                transform.SetLocalPosition(trailing, new Vector2(3.5f, 4.4f));
                arena.Enemies[trailing] = 4;
                system.Update(0.81f);
                Assert.That(tower.Target, Is.EqualTo(leading));
                Assert.That(SEntMan.GetComponent<TowerDefenseEnemyComponent>(leading).Health, Is.EqualTo(984));

                // Leave just a carp beyond level one's range, but within levels two and three.
                arena.Enemies.Remove(leading);
                SEntMan.DeleteEntity(leading);
                transform.SetLocalPosition(trailing, new Vector2(10.5f, 7.5f));
                arena.Enemies[trailing] = 2;
                tower.AttackTimer = 0;
                system.Update(0.01f);
                Assert.That(tower.Target, Is.Null);
                var healthBeforeUpgrade = SEntMan.GetComponent<TowerDefenseEnemyComponent>(trailing).Health;
                Assert.That(healthBeforeUpgrade, Is.EqualTo(1000));

                arena.Cash = 1000;
                Send(TowerDefenseAction.Upgrade, 1);
                system.Update(0.01f);
                Assert.That(tower.Target, Is.EqualTo(trailing));
                Assert.That(SEntMan.GetComponent<TowerDefenseEnemyComponent>(trailing).Health, Is.EqualTo(healthBeforeUpgrade - 16));
                Assert.That(tower.AttackTimer, Is.EqualTo(0.7f));

                Send(TowerDefenseAction.Upgrade, 2);
                Assert.That(tower.AttackTimer, Is.EqualTo(0.45f), "An upgrade caps an existing cooldown to the new tier's cooldown.");
                system.Update(0.2f);
                Assert.That(SEntMan.GetComponent<TowerDefenseEnemyComponent>(trailing).Health, Is.EqualTo(healthBeforeUpgrade - 16));
                system.Update(0.26f);
                Assert.That(SEntMan.GetComponent<TowerDefenseEnemyComponent>(trailing).Health, Is.EqualTo(healthBeforeUpgrade - 16 - 24));
                Assert.That(tower.AttackTimer, Is.EqualTo(0.45f));
            }
            finally
            {
                if (SEntMan.EntityExists(map))
                    SEntMan.DeleteEntity(map);
            }
        });
    }
}
