using System;
using System.Linq;
using System.Numerics;
using Content.Server._Starlight.Dragon;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Actions;
using Content.Shared._Starlight.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Configuration;

namespace Content.IntegrationTests.Tests._Starlight;

[TestFixture]
public sealed class WesternDragonBossTest
{
    [Test]
    public async Task HtnChoosesAreaAttackAndRespectsSharedRecovery()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var arena = await pair.CreateTestMap();
        EntityUid dragon = default;
        await server.WaitAssertion(() =>
        {
            var map = server.System<SharedMapSystem>();
            for (var x = -7; x <= 7; x++)
            for (var y = -7; y <= 7; y++)
                map.SetTile(arena.Grid, new Vector2i(x, y), arena.Tile.Tile);
            dragon = em.SpawnEntity("DragonWesternDefault", new EntityCoordinates(arena.Grid, 0.5f, 0.5f));
            server.System<DamageableSystem>().ChangeDamage(dragon,
                new DamageSpecifier { DamageDict = { ["Slash"] = FixedPoint2.New(230) } }, ignoreResistances: true);
            em.SpawnEntity("MobHuman", new EntityCoordinates(arena.Grid, 2.5f, 0.5f));
            em.SpawnEntity("MobHuman", new EntityCoordinates(arena.Grid, -1.5f, 0.5f));
        });
        await pair.RunSeconds(1.2f);
        await server.WaitAssertion(() =>
        {
            var boss = em.GetComponent<WesternDragonBossComponent>(dragon);
            Assert.That(boss.LastAbility, Is.EqualTo(DragonAbility.TailSlam));
            Assert.That(boss.AbilitiesUsed, Is.EqualTo(1), "No ability spam during shared recovery.");
            Assert.That(boss.TauntsSpoken, Is.EqualTo(1), "Aggro and ability taunts share one cooldown.");
            Assert.That(em.GetComponent<HTNComponent>(dragon).Plan, Is.Not.Null, "The real HTN must run the boss operator.");
            Assert.That(em.HasComponent<NPCMeleeCombatComponent>(dragon), Is.False, "Generic chase AI must not override positioning.");
            server.System<NPCSystem>().SleepNPC(dragon);
            Assert.That(boss.Target, Is.Null, "Player takeover/sleep must shut down the active combat plan.");
            Assert.That(em.HasComponent<NPCSteeringComponent>(dragon), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("ActionWesternFireBreath", DragonAbility.Breath, 4f)]
    [TestCase("ActionWesternDragonsBreath", DragonAbility.Fireball, 8f)]
    [TestCase("ActionWingDash", DragonAbility.Dash, 7f)]
    public async Task UsesExistingActionsAndTheirCooldowns(string actionId, DragonAbility expected, float distance)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var arena = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var map = server.System<SharedMapSystem>();
            for (var x = -3; x <= 12; x++)
            for (var y = -3; y <= 3; y++)
                map.SetTile(arena.Grid, new Vector2i(x, y), arena.Tile.Tile);
            var dragon = em.SpawnEntity("DragonWesternDefault", new EntityCoordinates(arena.Grid, 0.5f, 0.5f));
            em.GetComponent<WesternDragonBossComponent>(dragon).LowestHealth = 0.4f;
            em.SpawnEntity("MobHuman", new EntityCoordinates(arena.Grid, distance + 0.5f, 0.5f));
            var actions = server.System<SharedActionsSystem>();
            foreach (var action in actions.GetActions(dragon).ToArray())
                actions.SetEnabled(action, em.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == actionId);
            var htn = em.GetComponent<HTNComponent>(dragon);
            Assert.That(server.System<WesternDragonBossSystem>().Tick(dragon, htn.Blackboard), Is.True);
            var boss = em.GetComponent<WesternDragonBossComponent>(dragon);
            Assert.That(boss.LastAbility, Is.EqualTo(expected));
            var used = actions.GetActions(dragon).Single(a => em.GetComponent<MetaDataComponent>(a).EntityPrototype?.ID == actionId);
            Assert.That(actions.ValidAction(used), Is.False, "Successful NPC actions must consume the same cooldown as player actions.");
            boss.NextThink = TimeSpan.Zero;
            boss.NextAbility = TimeSpan.Zero;
            boss.RecoverUntil = TimeSpan.Zero;
            server.System<WesternDragonBossSystem>().Tick(dragon, htn.Blackboard);
            Assert.That(boss.AbilitiesUsed, Is.EqualTo(1));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ThreatChangesTargetsWithoutThrashingAndSleepStopsCombat()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var arena = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var map = server.System<SharedMapSystem>();
            for (var x = -3; x <= 12; x++)
            for (var y = -3; y <= 3; y++)
                map.SetTile(arena.Grid, new Vector2i(x, y), arena.Tile.Tile);
            var dragon = em.SpawnEntity("DragonWesternDefault", new EntityCoordinates(arena.Grid, 0.5f, 0.5f));
            var near = em.SpawnEntity("MobHuman", new EntityCoordinates(arena.Grid, 3.5f, 0.5f));
            var ranged = em.SpawnEntity("MobHuman", new EntityCoordinates(arena.Grid, 8.5f, 0.5f));
            var ai = server.System<WesternDragonBossSystem>();
            var boss = em.GetComponent<WesternDragonBossComponent>(dragon);
            var board = em.GetComponent<HTNComponent>(dragon).Blackboard;
            foreach (var action in server.System<SharedActionsSystem>().GetActions(dragon).ToArray())
                server.System<SharedActionsSystem>().SetEnabled(action, false);
            ai.Tick(dragon, board);
            Assert.That(boss.Target, Is.EqualTo(near));
            // Real damage attribution should make a dangerous distant attacker outrank the closest target.
            server.System<DamageableSystem>().ChangeDamage(dragon,
                new DamageSpecifier { DamageDict = { ["Slash"] = FixedPoint2.New(60) } }, origin: ranged);
            boss.NextThink = TimeSpan.Zero;
            ai.Tick(dragon, board);
            Assert.That(boss.Target, Is.EqualTo(ranged));
            Assert.That(boss.Threat[ranged], Is.GreaterThan(50));
            boss.NextThink = TimeSpan.Zero;
            ai.Tick(dragon, board);
            Assert.That(boss.Target, Is.EqualTo(ranged), "A target commitment should survive a routine reassessment.");
            server.System<NPCSystem>().SleepNPC(dragon);
            Assert.That(ai.Tick(dragon, board), Is.False, "Sleeping/player-controlled NPCs must not execute boss combat.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RetreatUsesSteeringWithoutTeleportation()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var arena = await pair.CreateTestMap();
        EntityUid dragon = default;
        var config = server.ResolveDependency<IConfigurationManager>();
        var disabledPathfinding = config.GetCVar(StarlightCCVars.DisablePathfinding);
        await server.WaitAssertion(() =>
        {
            config.SetCVar(StarlightCCVars.DisablePathfinding, false);
            var map = server.System<SharedMapSystem>();
            for (var x = -7; x <= 7; x++)
            for (var y = -7; y <= 7; y++)
                map.SetTile(arena.Grid, new Vector2i(x, y), arena.Tile.Tile);
            dragon = em.SpawnEntity("DragonWesternDefault", new EntityCoordinates(arena.Grid, 0.5f, 0.5f));
            em.SpawnEntity("MobHuman", new EntityCoordinates(arena.Grid, 3.5f, 0.5f));
            foreach (var action in server.System<SharedActionsSystem>().GetActions(dragon).ToArray())
                server.System<SharedActionsSystem>().SetEnabled(action, false);
            var boss = em.GetComponent<WesternDragonBossComponent>(dragon);
            boss.RecentDamage = 200;
            server.System<WesternDragonBossSystem>().Tick(dragon, em.GetComponent<HTNComponent>(dragon).Blackboard);
            Assert.That(boss.Positioning, Is.EqualTo(DragonPositioning.Retreat));
            var steering = em.GetComponent<NPCSteeringComponent>(dragon);
            var transform = server.System<SharedTransformSystem>();
            var destination = transform.ToCoordinates(arena.Grid.Owner, transform.ToMapCoordinates(steering.Coordinates));
            Assert.That(destination.X, Is.LessThan(0.5f));
            Assert.That(em.GetComponent<TransformComponent>(dragon).Coordinates.X, Is.EqualTo(0.5f), "Choosing a destination must not teleport the dragon.");
        });
        await pair.RunSeconds(1.2f);
        await server.WaitAssertion(() =>
        {
            config.SetCVar(StarlightCCVars.DisablePathfinding, disabledPathfinding);
            Assert.That(em.GetComponent<TransformComponent>(dragon).Coordinates.X, Is.LessThan(0.3f), "Existing steering should carry out the retreat.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LosLossUsesLastKnownPositionThenForgetsTarget()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var arena = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var map = server.System<SharedMapSystem>();
            for (var x = -2; x <= 10; x++)
            for (var y = -4; y <= 4; y++)
                map.SetTile(arena.Grid, new Vector2i(x, y), arena.Tile.Tile);
            var dragon = em.SpawnEntity("DragonWesternDefault", new EntityCoordinates(arena.Grid, 0.5f, 0.5f));
            var human = em.SpawnEntity("MobHuman", new EntityCoordinates(arena.Grid, 6.5f, 0.5f));
            foreach (var action in server.System<SharedActionsSystem>().GetActions(dragon).ToArray())
                server.System<SharedActionsSystem>().SetEnabled(action, false);
            var ai = server.System<WesternDragonBossSystem>();
            var boss = em.GetComponent<WesternDragonBossComponent>(dragon);
            var board = em.GetComponent<HTNComponent>(dragon).Blackboard;
            ai.Tick(dragon, board);
            var remembered = boss.LastKnownPosition;
            for (var y = -4; y <= 4; y++)
                em.SpawnEntity("WallSolid", new EntityCoordinates(arena.Grid, 3.5f, y + 0.5f));
            server.System<SharedTransformSystem>().SetCoordinates(human, new EntityCoordinates(arena.Grid, 8.5f, 2.5f));
            boss.NextThink = TimeSpan.Zero;
            ai.Tick(dragon, board);
            Assert.That(boss.Positioning, Is.EqualTo(DragonPositioning.Search));
            Assert.That(boss.LastKnownPosition, Is.EqualTo(remembered));
            Assert.That(em.GetComponent<NPCSteeringComponent>(dragon).DirectMove, Is.False,
                "Investigating behind cover must restore pathfinding instead of walking into the wall.");
            boss.LastSeen -= TimeSpan.FromSeconds(5);
            boss.NextThink = TimeSpan.Zero;
            Assert.That(ai.Tick(dragon, board), Is.False);
            ai.Stop(dragon, board);
            Assert.That(boss.Target, Is.Null);
            Assert.That(em.HasComponent<NPCSteeringComponent>(dragon), Is.False);
        });
        await pair.CleanReturnAsync();
    }
}
