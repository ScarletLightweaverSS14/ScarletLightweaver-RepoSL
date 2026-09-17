using System;
using System.Linq;
using Content.Server._Starlight.Dragon;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Devour.Components;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Starlight;

[TestFixture]
public sealed class WesternDragonRefinementTest
{
    [Test]
    public async Task LairTauntDoesNotRepeatAfterDisengaging()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var arena = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var dragon = em.SpawnEntity("DragonWesternDefault", arena.GridCoords);
            em.SpawnEntity("MobHuman", new EntityCoordinates(arena.Grid, 5, 0));
            var actions = server.System<SharedActionsSystem>();
            foreach (var action in actions.GetActions(dragon).ToArray())
                actions.SetEnabled(action, false);
            var ai = server.System<WesternDragonBossSystem>();
            var boss = em.GetComponent<WesternDragonBossComponent>(dragon);
            var board = em.GetComponent<HTNComponent>(dragon).Blackboard;
            ai.Tick(dragon, board);
            Assert.That(boss.LairTauntSpoken, Is.True);
            Assert.That(boss.TauntsSpoken, Is.EqualTo(1));
            ai.Stop(dragon, board);
            boss.NextThink = TimeSpan.Zero;
            boss.NextTaunt = TimeSpan.Zero;
            ai.Tick(dragon, board);
            Assert.That(boss.TauntsSpoken, Is.EqualTo(1), "Re-aggro must not repeat the once-per-dragon introduction.");
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("complete")]
    [TestCase("damage")]
    [TestCase("revived")]
    [TestCase("sleep")]
    [TestCase("deleted")]
    public async Task DevourHonorsQuietTimeAndCanBeInterrupted(string outcome)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var arena = await pair.CreateTestMap();
        EntityUid dragon = default;
        EntityUid victim = default;
        DoAfterId? started = null;
        await server.WaitAssertion(() =>
        {
            var map = server.System<SharedMapSystem>();
            for (var x = -3; x <= 3; x++)
            for (var y = -3; y <= 3; y++)
                map.SetTile(arena.Grid, new Vector2i(x, y), arena.Tile.Tile);
            dragon = em.SpawnEntity("DragonWesternDefault", new EntityCoordinates(arena.Grid, 0.5f, 0.5f));
            victim = em.SpawnEntity("MobHuman", new EntityCoordinates(arena.Grid, 1.5f, 0.5f));
            server.System<DamageableSystem>().ChangeDamage(victim,
                new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(220) } });
            var ai = server.System<WesternDragonBossSystem>();
            var boss = em.GetComponent<WesternDragonBossComponent>(dragon);
            var board = em.GetComponent<HTNComponent>(dragon).Blackboard;
            var now = server.ResolveDependency<IGameTiming>().CurTime;
            Assert.That(boss.DevourAvailableAt, Is.EqualTo(now + TimeSpan.FromSeconds(30)));
            boss.NextPosition = now + TimeSpan.FromSeconds(2);
            server.System<DamageableSystem>().ChangeDamage(dragon,
                new DamageSpecifier { DamageDict = { ["Slash"] = FixedPoint2.New(1) } });
            Assert.That(boss.NextPosition, Is.EqualTo(now + TimeSpan.FromSeconds(2)),
                "Small hits outside feeding must not continually cancel committed movement.");
            ai.Tick(dragon, board);
            Assert.That(boss.DevourDoAfter, Is.Null, "Fresh spawns must wait 30 damage-free seconds too.");
            boss.DevourAvailableAt = now;
            boss.NextThink = TimeSpan.Zero;
        });

        // Exercise the actual food HTN branch with no living combat targets present.
        await pair.RunSeconds(0.7f);
        await server.WaitAssertion(() =>
        {
            var boss = em.GetComponent<WesternDragonBossComponent>(dragon);
            started = boss.DevourDoAfter;
            Assert.That(server.System<SharedDoAfterSystem>().IsRunning(started), Is.True, "The feeding branch must start normal Devour.");
            switch (outcome)
            {
                case "damage":
                    server.System<DamageableSystem>().ChangeDamage(dragon,
                        new DamageSpecifier { DamageDict = { ["Slash"] = FixedPoint2.New(1) } });
                    var now = server.ResolveDependency<IGameTiming>().CurTime;
                    Assert.That(boss.DevourAvailableAt, Is.EqualTo(now + TimeSpan.FromSeconds(30)));
                    Assert.That(server.System<SharedDoAfterSystem>().IsRunning(started), Is.False);
                    server.System<WesternDragonBossSystem>().Stop(dragon, em.GetComponent<HTNComponent>(dragon).Blackboard);
                    Assert.That(boss.DevourAvailableAt, Is.EqualTo(now + TimeSpan.FromSeconds(30)), "Replanning cannot erase the damage timer.");
                    break;
                case "revived":
                    server.System<DamageableSystem>().ClearAllDamage(victim);
                    server.System<MobStateSystem>().ChangeMobState(victim, MobState.Alive);
                    break;
                case "sleep":
                    server.System<NPCSystem>().SleepNPC(dragon);
                    break;
                case "deleted":
                    em.DeleteEntity(victim);
                    break;
            }
        });
        await pair.RunSeconds(3.6f);
        await server.WaitAssertion(() =>
        {
            var stomach = em.GetComponent<DevourerComponent>(dragon).Stomach;
            Assert.That(stomach.Contains(victim), Is.EqualTo(outcome == "complete"));
            Assert.That(server.System<SharedDoAfterSystem>().IsRunning(started), Is.False);
            if (outcome == "complete")
            {
                var boss = em.GetComponent<WesternDragonBossComponent>(dragon);
                boss.NextThink = TimeSpan.Zero;
                boss.NextDevourAttempt = TimeSpan.Zero;
                server.System<WesternDragonBossSystem>().Tick(dragon, em.GetComponent<HTNComponent>(dragon).Blackboard);
                Assert.That(boss.DevourDoAfter, Is.Null, "Already swallowed targets must not provide repeated healing.");
                Assert.That(em.GetComponent<DevourerComponent>(dragon).Devoured, Is.EqualTo(1));
            }
        });
        await pair.CleanReturnAsync();
    }
}
