using System;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._Starlight.Dragon;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Starlight;

[TestFixture]
public sealed class WesternDragonCounterplayTest
{
    [Test]
    public async Task SustainedAttackerGetsWarningThenFireballWithoutBypassingCooldowns()
    {
        await using var pair = await PoolManager.GetServerClient();
        var (dragon, target, _) = await CreateEncounter(pair);
        var server = pair.Server;
        var em = server.EntMan;
        await server.WaitAssertion(() =>
        {
            BeginWarning(pair, dragon, target);
            Assert.That(em.HasComponent<NPCSteeringComponent>(dragon), Is.False,
                "The warning should give the attacker a clear windup before the counterattack.");
        });

        // Keep the real warning and HTN/action timers; only the preceding observation window is shortened.
        await pair.RunSeconds(1f);
        await server.WaitAssertion(() => Assert.That(em.GetComponent<WesternDragonBossComponent>(dragon).AbilitiesUsed, Is.Zero));
        await pair.RunSeconds(0.8f);
        await server.WaitAssertion(() =>
        {
            var boss = em.GetComponent<WesternDragonBossComponent>(dragon);
            Assert.That(boss.LowestHealth, Is.GreaterThan(boss.FireballHealth), "Small hits must not unlock Ancient Flame normally.");
            Assert.That(boss.LastAbility, Is.EqualTo(DragonAbility.Fireball));
            Assert.That(boss.AbilitiesUsed, Is.EqualTo(1));
            var action = em.GetComponent<ActionGunComponent>(dragon).ActionEntity;
            var actions = server.System<SharedActionsSystem>();
            Assert.That(actions.ValidAction(actions.GetAction(action)!.Value), Is.False,
                "The counterattack must consume the ordinary action cooldown.");
            Assert.That(boss.NextAbility, Is.GreaterThan(server.ResolveDependency<IGameTiming>().CurTime));
            boss.NextAbility = TimeSpan.Zero;
            boss.RecoverUntil = TimeSpan.Zero;
            Think(pair, dragon);
            Assert.That(boss.AbilitiesUsed, Is.EqualTo(1), "Counterplay must not bypass the action's cooldown even after shared recovery.");
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("none")]
    [TestCase("environment")]
    [TestCase("other-attacker")]
    [TestCase("expired")]
    [TestCase("brief")]
    public async Task CounterplayRequiresSustainedDamageFromPursuedTarget(string damage)
    {
        await using var pair = await PoolManager.GetServerClient();
        var (dragon, target, grid) = await CreateEncounter(pair);
        await pair.Server.WaitAssertion(() =>
        {
            var boss = pair.Server.EntMan.GetComponent<WesternDragonBossComponent>(dragon);
            if (damage != "none")
            {
                EntityUid? origin = damage switch
                {
                    "environment" => null,
                    "other-attacker" => pair.Server.EntMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 11.5f, 0.5f)),
                    _ => target,
                };
                Hit(pair, dragon, origin);
            }
            Think(pair, dragon);
            boss.PursuitSampleAt -= TimeSpan.FromSeconds(boss.CounterplayDelay + (damage == "brief" ? -0.1 : 0.1));
            if (damage == "expired")
            {
                boss.PursuitAttackedUntil = pair.Server.ResolveDependency<IGameTiming>().CurTime - TimeSpan.FromSeconds(1);
                boss.RecentAttackUntil = boss.PursuitAttackedUntil;
            }
            Think(pair, dragon);
            Assert.That(boss.Target, Is.EqualTo(target));
            Assert.That(boss.CounterplayTarget, Is.Null);
            Assert.That(boss.AbilitiesUsed, Is.Zero);
            Assert.That(boss.Positioning, Is.EqualTo(DragonPositioning.Approach));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClosingDistanceStartsANewObservationWindow()
    {
        await using var pair = await PoolManager.GetServerClient();
        var (dragon, target, grid) = await CreateEncounter(pair);
        await pair.Server.WaitAssertion(() =>
        {
            var boss = pair.Server.EntMan.GetComponent<WesternDragonBossComponent>(dragon);
            Hit(pair, dragon, target);
            Think(pair, dragon);
            boss.PursuitSampleAt -= TimeSpan.FromSeconds(boss.CounterplayDelay + 0.1f);
            pair.Server.System<SharedTransformSystem>().SetCoordinates(target, new EntityCoordinates(grid, 6.5f, 0.5f));
            Hit(pair, dragon, target);
            Think(pair, dragon);
            Assert.That(boss.CounterplayTarget, Is.Null, "Gaining two tiles is ordinary pursuit, even after the old observation window expired.");
            Assert.That(boss.PursuitSampleAt, Is.EqualTo(pair.Server.ResolveDependency<IGameTiming>().CurTime));
            Assert.That(boss.PursuitDistance, Is.EqualTo(6f).Within(0.01f));
            Think(pair, dragon);
            Assert.That(boss.CounterplayTarget, Is.Null);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("target")]
    [TestCase("los")]
    [TestCase("melee")]
    public async Task CounterplayEndsWhenItsCombatSituationChanges(string change)
    {
        await using var pair = await PoolManager.GetServerClient();
        var (dragon, target, grid) = await CreateEncounter(pair);
        await pair.Server.WaitAssertion(() =>
        {
            var server = pair.Server;
            var em = server.EntMan;
            var boss = em.GetComponent<WesternDragonBossComponent>(dragon);
            BeginWarning(pair, dragon, target);
            var nextCounterplay = boss.NextCounterplay;
            switch (change)
            {
                case "target":
                    var other = em.SpawnEntity("MobHuman", new EntityCoordinates(grid, 6.5f, 0.5f));
                    Hit(pair, dragon, other, 80);
                    break;
                case "los":
                    // Incoming shots briefly preserve their last firing position even behind cover.
                    boss.RecentAttackUntil = server.ResolveDependency<IGameTiming>().CurTime;
                    for (var y = -4; y <= 4; y++)
                        em.SpawnEntity("WallSolid", new EntityCoordinates(grid, 4.5f, y + 0.5f));
                    break;
                case "melee":
                    server.System<SharedTransformSystem>().SetCoordinates(target, new EntityCoordinates(grid, 2.5f, 0.5f));
                    break;
            }
            Think(pair, dragon);
            Assert.That(boss.CounterplayTarget, Is.Null);
            Assert.That(boss.PursuitTarget, Is.Null);
            Assert.That(boss.NextCounterplay, Is.EqualTo(nextCounterplay), "Switching targets or losing LOS must not reset the response cooldown.");
            Assert.That(boss.AbilitiesUsed, Is.Zero);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SleepingTheRealCombatPlanClearsCounterplay()
    {
        await using var pair = await PoolManager.GetServerClient();
        var (dragon, target, _) = await CreateEncounter(pair);
        await pair.Server.WaitAssertion(() => BeginWarning(pair, dragon, target));
        // Allow the HTN to own an actual running operator so SleepNPC exercises its shutdown hooks.
        await pair.RunSeconds(0.6f);
        await pair.Server.WaitAssertion(() =>
        {
            var boss = pair.Server.EntMan.GetComponent<WesternDragonBossComponent>(dragon);
            var board = pair.Server.EntMan.GetComponent<HTNComponent>(dragon).Blackboard;
            var nextCounterplay = boss.NextCounterplay;
            Assert.That(pair.Server.EntMan.GetComponent<HTNComponent>(dragon).Plan, Is.Not.Null);
            pair.Server.System<NPCSystem>().SleepNPC(dragon);
            Assert.That(boss.CounterplayTarget, Is.Null);
            Assert.That(boss.PursuitTarget, Is.Null);
            Assert.That(boss.NextCounterplay, Is.EqualTo(nextCounterplay));
            Assert.That(pair.Server.System<WesternDragonBossSystem>().Tick(dragon, board), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("disabled")]
    [TestCase("action")]
    [TestCase("shared")]
    [TestCase("gun")]
    public async Task UnavailableFireballDoesNotStartWarning(string cooldown)
    {
        await using var pair = await PoolManager.GetServerClient();
        var (dragon, target, _) = await CreateEncounter(pair);
        await pair.Server.WaitAssertion(() =>
        {
            var boss = pair.Server.EntMan.GetComponent<WesternDragonBossComponent>(dragon);
            var actions = pair.Server.System<SharedActionsSystem>();
            var actionGun = pair.Server.EntMan.GetComponent<ActionGunComponent>(dragon);
            var now = pair.Server.ResolveDependency<IGameTiming>().CurTime;
            switch (cooldown)
            {
                case "disabled":
                    actions.SetEnabled(actionGun.ActionEntity, false);
                    break;
                case "action":
                    actions.SetCooldown(actionGun.ActionEntity, TimeSpan.FromSeconds(30));
                    break;
                case "shared":
                    boss.NextAbility = now + TimeSpan.FromSeconds(30);
                    break;
                case "gun":
                    pair.Server.System<SharedGunSystem>().DelayFire(actionGun.Gun!.Value, TimeSpan.FromSeconds(30));
                    break;
            }
            Hit(pair, dragon, target);
            Think(pair, dragon);
            boss.PursuitSampleAt -= TimeSpan.FromSeconds(boss.CounterplayDelay + 0.1f);
            Hit(pair, dragon, target);
            Think(pair, dragon);
            Assert.That(boss.CounterplayTarget, Is.Null);
            Assert.That(boss.CounterplayReadyAt, Is.EqualTo(TimeSpan.Zero));
            Assert.That(boss.AbilitiesUsed, Is.Zero);
            Assert.That(pair.Server.EntMan.HasComponent<NPCSteeringComponent>(dragon), Is.True,
                "An unavailable counterattack must not interrupt ordinary pursuit.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FailedPathRetriesAreThrottledWithoutTriggeringCounterplay()
    {
        await using var pair = await PoolManager.GetServerClient();
        var (dragon, _, _) = await CreateEncounter(pair);
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var boss = em.GetComponent<WesternDragonBossComponent>(dragon);
            var failed = em.GetComponent<NPCSteeringComponent>(dragon);
            failed.Status = SteeringStatus.NoPath;
            Think(pair, dragon);
            var retry = em.GetComponent<NPCSteeringComponent>(dragon);
            Assert.That(retry, Is.Not.SameAs(failed));
            retry.Status = SteeringStatus.NoPath;
            Think(pair, dragon);
            Think(pair, dragon);
            Assert.That(em.GetComponent<NPCSteeringComponent>(dragon), Is.SameAs(retry),
                "Repeated failed routing must not start a fresh path job on every decision.");
            Assert.That(boss.CounterplayTarget, Is.Null, "Path failure alone is not evidence of an attacking player exploiting it.");
            boss.NextPathRetry = pair.Server.ResolveDependency<IGameTiming>().CurTime;
            Think(pair, dragon);
            Assert.That(em.GetComponent<NPCSteeringComponent>(dragon), Is.Not.SameAs(retry));
            Assert.That(boss.NextPathRetry, Is.GreaterThan(pair.Server.ResolveDependency<IGameTiming>().CurTime));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CounterShotLeadsFastTargetsByAtMostThreeTiles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var (dragon, target, _) = await CreateEncounter(pair);
        await pair.Server.WaitAssertion(() =>
        {
            var server = pair.Server;
            BeginWarning(pair, dragon, target);
            var boss = server.EntMan.GetComponent<WesternDragonBossComponent>(dragon);
            boss.CounterplayReadyAt = server.ResolveDependency<IGameTiming>().CurTime;
            server.System<SharedPhysicsSystem>().SetLinearVelocity(target, new Vector2(0, 100));
            Think(pair, dragon);
            Assert.That(boss.LastAbility, Is.EqualTo(DragonAbility.Fireball));
            var action = server.EntMan.GetComponent<ActionGunComponent>(dragon).ActionEntity;
            var shot = (ActionGunShootEvent) server.System<SharedActionsSystem>().GetEvent(action!.Value)!;
            var transform = server.System<SharedTransformSystem>();
            var lead = transform.ToMapCoordinates(shot.Target).Position - transform.GetMapCoordinates(target).Position;
            Assert.That(lead.Y, Is.GreaterThan(0), "Aim should lead the visible target's movement.");
            Assert.That(lead.Length(), Is.LessThanOrEqualTo(3.01f), "Speed boosts must not create arbitrarily distant aim points.");
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ImmuneLaserHitsThroughNearbyGlassStillProvokeCounterattack(bool hitscan)
    {
        await using var pair = await PoolManager.GetServerClient();
        var (dragon, target, grid) = await CreateEncounter(pair);
        EntityUid window = default;
        await pair.Server.WaitAssertion(() =>
        {
            var server = pair.Server;
            var em = server.EntMan;
            var boss = em.GetComponent<WesternDragonBossComponent>(dragon);
            server.System<SharedTransformSystem>().SetCoordinates(target, new EntityCoordinates(grid, 2.5f, 0.5f));
            window = em.SpawnEntity("Window", new EntityCoordinates(grid, 1.5f, 0.5f));
            Assert.That(server.System<SharedInteractionSystem>().InRangeUnobstructed(dragon,
                em.GetComponent<TransformComponent>(target).Coordinates, range: 0,
                collisionMask: CollisionGroup.Impassable | CollisionGroup.InteractImpassable), Is.False);

            EntityUid origin;
            if (hitscan)
            {
                origin = em.SpawnEntity("WeaponLr30", em.GetComponent<TransformComponent>(target).Coordinates);
                Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(target, origin), Is.True);
            }
            else
            {
                origin = em.SpawnEntity(null, new EntityCoordinates(grid, 0.5f, 0.5f));
                em.AddComponent<ProjectileComponent>(origin).Shooter = target;
            }
            var damage = new DamageSpecifier { DamageDict = { ["Heat"] = FixedPoint2.New(20) } };
            server.System<DamageableSystem>().ChangeDamage(dragon, damage, origin: origin);
            Think(pair, dragon);
            Assert.That(boss.RecentAttacker, Is.EqualTo(target), "Immune laser hits must identify the projectile shooter or held hitscan gun's wielder.");
            Assert.That(boss.PursuitTarget, Is.EqualTo(target), "Nearby glass must not be mistaken for successful melee pursuit.");
            boss.PursuitSampleAt -= TimeSpan.FromSeconds(boss.CounterplayDelay + 0.1f);
            server.System<DamageableSystem>().ChangeDamage(dragon, damage, origin: origin);
            Think(pair, dragon);
            Assert.That(em.GetComponent<DamageableComponent>(dragon).TotalDamage, Is.EqualTo(FixedPoint2.Zero));
            Assert.That(boss.CounterplayTarget, Is.EqualTo(target));
            Assert.That(boss.LowestHealth, Is.EqualTo(1f));
            var actions = server.System<SharedActionsSystem>();
            foreach (var action in actions.GetActions(dragon))
                actions.SetEnabled(action, true);
            boss.LastAbility = DragonAbility.Fireball;
            boss.CounterplayReadyAt = server.ResolveDependency<IGameTiming>().CurTime;
            Think(pair, dragon);
            Assert.That(boss.LastAbility, Is.EqualTo(DragonAbility.Fireball),
                "A full-health dragon should answer sustained laser fire through glass with its normal projectile.");
            Assert.That(em.EntityQuery<WesternDragonFireTrailComponent>().Count(), Is.EqualTo(1),
                "The counterattack must emit the normal Ancient Flame projectile.");
            // Let the emitted shot resolve without later AI attacks supplying the window damage.
            server.System<HTNSystem>().SetHTNEnabled((dragon, em.GetComponent<HTNComponent>(dragon)), false);
        });
        await pair.RunSeconds(0.6f);
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            Assert.That(em.Deleted(window) || em.GetComponent<DamageableComponent>(window).TotalDamage > FixedPoint2.Zero,
                Is.True, "The actual Ancient Flame impact must damage or destroy the glass blocking its attacker.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RealHtnRespondsBeyondVisualQueryWithoutTrackingHiddenMovement()
    {
        await using var pair = await PoolManager.GetServerClient();
        var (dragon, target, grid) = await CreateEncounter(pair);
        EntityCoordinates firingPosition = default;
        await pair.Server.WaitAssertion(() =>
        {
            var server = pair.Server;
            var em = server.EntMan;
            var boss = em.GetComponent<WesternDragonBossComponent>(dragon);
            var board = em.GetComponent<HTNComponent>(dragon).Blackboard;
            server.System<WesternDragonBossSystem>().Stop(dragon, board);
            server.System<SharedTransformSystem>().SetCoordinates(target, new EntityCoordinates(grid, 18.5f, 0.5f));
            var gun = em.SpawnEntity("WeaponLr30", em.GetComponent<TransformComponent>(target).Coordinates);
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(target, gun), Is.True);
            server.System<DamageableSystem>().ChangeDamage(dragon,
                new DamageSpecifier { DamageDict = { ["Heat"] = FixedPoint2.New(20) } }, origin: gun);
            firingPosition = boss.RecentAttackPosition!.Value;
            for (var y = -5; y <= 5; y++)
                em.SpawnEntity("WallSolid", new EntityCoordinates(grid, 4.5f, y + 0.5f));
            server.System<SharedTransformSystem>().SetCoordinates(target, new EntityCoordinates(grid, 20.5f, 2.5f));
            Assert.That(server.System<NPCUtilitySystem>().GetEntities(board, "WesternDragonTargets", bestOnly: false).Entities,
                Is.Empty, "The normal visual branch must be unavailable in this regression.");
            Assert.That(boss.Target, Is.Null);
            boss.NextThink = TimeSpan.Zero;
        });

        // No manual AI tick: only the attack-memory HTN branch can establish this combat plan.
        await pair.RunSeconds(0.8f);
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var boss = em.GetComponent<WesternDragonBossComponent>(dragon);
            Assert.That(em.GetComponent<HTNComponent>(dragon).Plan, Is.Not.Null);
            Assert.That(boss.Target, Is.EqualTo(target));
            Assert.That(boss.LastKnownPosition, Is.EqualTo(firingPosition), "Remember the actual shot location, not subsequent movement behind walls.");
            Assert.That(boss.PursuitTarget, Is.EqualTo(target));
            Assert.That(em.GetComponent<DamageableComponent>(dragon).TotalDamage, Is.EqualTo(FixedPoint2.Zero));
        });
        await pair.CleanReturnAsync();
    }

    private static async Task<(EntityUid Dragon, EntityUid Target, EntityUid Grid)> CreateEncounter(TestPair pair)
    {
        var arena = await pair.CreateTestMap();
        EntityUid dragon = default;
        EntityUid target = default;
        await pair.Server.WaitAssertion(() =>
        {
            var server = pair.Server;
            var em = server.EntMan;
            var map = server.System<SharedMapSystem>();
            for (var x = -3; x <= 23; x++)
            for (var y = -5; y <= 5; y++)
                map.SetTile(arena.Grid, new Vector2i(x, y), arena.Tile.Tile);
            dragon = em.SpawnEntity("DragonWesternDefault", new EntityCoordinates(arena.Grid, 0.5f, 0.5f));
            target = em.SpawnEntity("MobHuman", new EntityCoordinates(arena.Grid, 8.5f, 0.5f));
            var actions = server.System<SharedActionsSystem>();
            foreach (var action in actions.GetActions(dragon).ToArray())
                actions.SetEnabled(action, em.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == "ActionWesternDragonsBreath");
            Think(pair, dragon);
            Assert.That(em.GetComponent<WesternDragonBossComponent>(dragon).Target, Is.EqualTo(target));
            Assert.That(em.GetComponent<WesternDragonBossComponent>(dragon).AbilitiesUsed, Is.Zero);
        });
        return (dragon, target, arena.Grid.Owner);
    }

    private static void Hit(TestPair pair, EntityUid dragon, EntityUid? attacker, int amount = 5)
        => pair.Server.System<DamageableSystem>().ChangeDamage(dragon,
            new DamageSpecifier { DamageDict = { ["Slash"] = FixedPoint2.New(amount) } }, ignoreResistances: true, origin: attacker);

    private static void Think(TestPair pair, EntityUid dragon)
    {
        pair.Server.EntMan.GetComponent<WesternDragonBossComponent>(dragon).NextThink = TimeSpan.Zero;
        var board = pair.Server.EntMan.GetComponent<HTNComponent>(dragon).Blackboard;
        Assert.That(pair.Server.System<WesternDragonBossSystem>().Tick(dragon, board), Is.True);
    }

    private static void BeginWarning(TestPair pair, EntityUid dragon, EntityUid target)
    {
        var boss = pair.Server.EntMan.GetComponent<WesternDragonBossComponent>(dragon);
        Hit(pair, dragon, target);
        Think(pair, dragon);
        Assert.That(boss.PursuitTarget, Is.EqualTo(target));
        // Advance only accumulated pursuit evidence; real damage attribution and ability timing remain in use.
        boss.PursuitSampleAt -= TimeSpan.FromSeconds(boss.CounterplayDelay + 0.1f);
        Hit(pair, dragon, target);
        Think(pair, dragon);
        Assert.That(boss.CounterplayTarget, Is.EqualTo(target));
        Assert.That(boss.CounterplayReadyAt, Is.GreaterThan(pair.Server.ResolveDependency<IGameTiming>().CurTime));
        Assert.That(boss.AbilitiesUsed, Is.Zero);
    }
}
