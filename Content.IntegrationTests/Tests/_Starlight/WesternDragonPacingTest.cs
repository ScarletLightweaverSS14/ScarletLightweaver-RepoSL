using System;
using System.Linq;
using System.Numerics;
using Content.Server._Starlight.Dragon;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Starlight;

[TestFixture]
public sealed class WesternDragonPacingTest
{
    [TestCase("ActionWesternTailSlam", DragonAbility.TailSlam, 2f, 230)]
    [TestCase("ActionWesternFireBreath", DragonAbility.Breath, 4f, 460)]
    [TestCase("ActionWesternDragonsBreath", DragonAbility.Fireball, 8f, 760)]
    public async Task DamageUnlocksAttacksAndHealingDoesNotRelock(string actionId, DragonAbility expected, float distance, int damage)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var arena = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var map = server.System<SharedMapSystem>();
            for (var x = -4; x <= 12; x++)
            for (var y = -4; y <= 4; y++)
                map.SetTile(arena.Grid, new Vector2i(x, y), arena.Tile.Tile);
            var dragon = em.SpawnEntity("DragonWesternDefault", new EntityCoordinates(arena.Grid, 0.5f, 0.5f));
            em.SpawnEntity("MobHuman", new EntityCoordinates(arena.Grid, distance + 0.5f, 0.5f));
            var actions = server.System<SharedActionsSystem>();
            foreach (var action in actions.GetActions(dragon).ToArray())
                actions.SetEnabled(action, em.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == actionId);
            var boss = em.GetComponent<WesternDragonBossComponent>(dragon);
            var ai = server.System<WesternDragonBossSystem>();
            var board = em.GetComponent<HTNComponent>(dragon).Blackboard;
            ai.Tick(dragon, board);
            Assert.That(boss.AbilitiesUsed, Is.Zero, "Fresh dragons should start with melee, roar and dash.");
            server.System<DamageableSystem>().ChangeDamage(dragon,
                new DamageSpecifier { DamageDict = { ["Slash"] = FixedPoint2.New(damage) } }, ignoreResistances: true);
            boss.NextThink = TimeSpan.Zero;
            ai.Tick(dragon, board);
            Assert.That(boss.LastAbility, Is.EqualTo(expected), "A single nearby opponent must be enough for unlocked Tail Slam.");
            var now = server.ResolveDependency<IGameTiming>().CurTime;
            Assert.That(boss.NextAbility, Is.GreaterThanOrEqualTo(now + TimeSpan.FromSeconds(6)));
            var lowest = boss.LowestHealth;
            ai.Stop(dragon, board);
            server.System<DamageableSystem>().ClearAllDamage(dragon);
            boss.NextThink = TimeSpan.Zero;
            ai.Tick(dragon, board);
            Assert.That(boss.LowestHealth, Is.EqualTo(lowest));
            Assert.That(boss.AbilitiesUsed, Is.EqualTo(1), "Healing or replanning must not bypass shared pacing.");
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(true, false)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    public async Task RoarFollowUpEndsInMeleeEvenWhenDashUnavailable(bool dashAvailable, bool replan)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var arena = await pair.CreateTestMap();
        EntityUid dragon = default;
        EntityUid human = default;
        await server.WaitAssertion(() =>
        {
            var map = server.System<SharedMapSystem>();
            for (var x = -3; x <= 10; x++)
            for (var y = -3; y <= 3; y++)
                map.SetTile(arena.Grid, new Vector2i(x, y), arena.Tile.Tile);
            dragon = em.SpawnEntity("DragonWesternDefault", new EntityCoordinates(arena.Grid, 0.5f, 0.5f));
            human = em.SpawnEntity("MobHuman", new EntityCoordinates(arena.Grid, 7.5f, 0.5f));
            var actions = server.System<SharedActionsSystem>();
            foreach (var action in actions.GetActions(dragon).ToArray())
            {
                var id = em.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID;
                actions.SetEnabled(action, id == "ActionDragonRoar" || (dashAvailable && id == "ActionWingDash"));
            }
            var boss = em.GetComponent<WesternDragonBossComponent>(dragon);
            var ai = server.System<WesternDragonBossSystem>();
            var board = em.GetComponent<HTNComponent>(dragon).Blackboard;
            ai.Tick(dragon, board);
            Assert.That(boss.LastAbility, Is.EqualTo(DragonAbility.Roar));
            Assert.That(boss.ComboTarget, Is.EqualTo(human));
            Assert.That(em.HasComponent<NPCSteeringComponent>(dragon), Is.True, "Roaring must not cancel pursuit.");
            if (replan)
            {
                ai.Stop(dragon, board);
                Assert.That(boss.ComboTarget, Is.Null);
                Assert.That(boss.NextAbility, Is.GreaterThanOrEqualTo(
                    server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromSeconds(6)),
                    "Cancelling a combo must not retain its short follow-up timer.");
            }
        });
        // Let real HTN timing and movement decide the follow-up, without forcing its timer due.
        await pair.RunSeconds(1.5f);
        await server.WaitAssertion(() =>
        {
            var boss = em.GetComponent<WesternDragonBossComponent>(dragon);
            Assert.That(boss.ComboTarget, Is.Null);
            Assert.That(boss.AbilitiesUsed, Is.EqualTo(dashAvailable && !replan ? 2 : 1));
            Assert.That(boss.NextAbility, Is.GreaterThanOrEqualTo(
                server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromSeconds(4)));
        });
        await pair.RunSeconds(2.5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<DamageableComponent>(human).TotalDamage, Is.GreaterThan(FixedPoint2.Zero),
                "The combo or failed-combo fallback must actually reach and melee the target.");
            Assert.That(em.GetComponent<WesternDragonBossComponent>(dragon).AbilitiesUsed, Is.EqualTo(dashAvailable && !replan ? 2 : 1),
                "The melee window must not be interrupted by another spell.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PursuitTracksMovingTargetAndCompletedManeuversResumeImmediately()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var arena = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var dragon = em.SpawnEntity("DragonWesternDefault", arena.GridCoords);
            var human = em.SpawnEntity("MobHuman", new EntityCoordinates(arena.Grid, 5, 0));
            foreach (var action in server.System<SharedActionsSystem>().GetActions(dragon).ToArray())
                server.System<SharedActionsSystem>().SetEnabled(action, false);
            var boss = em.GetComponent<WesternDragonBossComponent>(dragon);
            var ai = server.System<WesternDragonBossSystem>();
            var board = em.GetComponent<HTNComponent>(dragon).Blackboard;
            ai.Tick(dragon, board);
            var steering = em.GetComponent<NPCSteeringComponent>(dragon);
            Assert.That(boss.Positioning, Is.EqualTo(DragonPositioning.Approach));
            Assert.That(steering.Coordinates, Is.EqualTo(new EntityCoordinates(human, Vector2.Zero)));
            boss.Positioning = DragonPositioning.Circle;
            boss.NextPosition = server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromSeconds(2);
            steering.Status = SteeringStatus.InRange;
            boss.NextThink = TimeSpan.Zero;
            ai.Tick(dragon, board);
            Assert.That(boss.Positioning, Is.EqualTo(DragonPositioning.Approach), "Arriving early must not cause a two-second stall.");
            server.System<SharedTransformSystem>().SetCoordinates(human, new EntityCoordinates(arena.Grid, 6, 1));
            var mapTarget = server.System<SharedTransformSystem>().ToMapCoordinates(steering.Coordinates);
            Assert.That(mapTarget, Is.EqualTo(server.System<SharedTransformSystem>().GetMapCoordinates(human)),
                "Steering should follow the visible target between boss decisions.");
        });
        await pair.CleanReturnAsync();
    }
}
