using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Starlight.Voidwalker;
using Content.Shared._Starlight.Voidwalker;
using Content.Shared.Actions;
using Content.Shared.Damage.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Projectiles;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Starlight.Voidwalker;

[TestFixture]
public sealed class CrystalBurstTest
{
    [TestCase(0)]
    [TestCase(90)]
    public async Task WarningExpandsThenLaunchesFixedRadialShots(int rotation)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var actions = server.System<SharedActionsSystem>();
        var transform = server.System<SharedTransformSystem>();
        var physics = server.System<SharedPhysicsSystem>();
        var map = await pair.CreateTestMap();
        EntityUid caster = default;
        CrystalBurstComponent burst = null;
        EntityUid[] warning = [];
        Dictionary<EntityUid, Vector2> velocities = new();

        await server.WaitAssertion(() =>
        {
            transform.SetLocalRotation(map.Grid.Owner, Angle.FromDegrees(rotation));
            caster = entities.SpawnEntity("MobHuman", map.GridCoords);
            burst = entities.EnsureComponent<CrystalBurstComponent>(caster);
            var ev = new CrystalBurstEvent();
            actions.PerformAction(caster, actions.GetAction(burst.ActionEntity).Value, ev);
            Assert.That(ev.Handled, Is.True);
            Assert.That(burst.Crystals.Count, Is.EqualTo(24));
            warning = burst.Crystals.ToArray();
            Assert.That(Projectiles(entities), Is.Empty, "The warning ring must not deal damage.");
            Assert.That(actions.ValidAction(actions.GetAction(burst.ActionEntity).Value), Is.False);
            Assert.That(entities.EnsureComponent<CrystalBurstComponent>(caster), Is.SameAs(burst));
        });

        await pair.RunSeconds(0.15f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Projectiles(entities), Is.Empty);
            foreach (var crystal in warning)
            {
                var offset = transform.GetMapCoordinates(crystal).Position - transform.GetMapCoordinates(caster).Position;
                Assert.That(offset.Length(), Is.GreaterThan(burst.InitialRadius).And.LessThan(burst.FormationRadius));
                Assert.That(Vector2.Dot(Vector2.Normalize(offset), transform.GetWorldRotation(crystal).ToVec()),
                    Is.EqualTo(1).Within(0.001), "Every warning shard must point away from the caster.");
            }
            // The formation follows movement during its warning, including on a rotated grid.
            transform.SetCoordinates(caster, map.GridCoords.Offset(new Vector2(0, 4)));
        });

        // Leave a full tick beyond the warning, but inspect before the short projectile lifetime ends.
        await pair.RunSeconds(0.35f);
        await server.WaitAssertion(() =>
        {
            var shots = Projectiles(entities);
            Assert.That(shots.Count, Is.EqualTo(24));
            Assert.That(burst.Crystals, Is.Empty);
            Assert.That(warning.All(uid => !entities.EntityExists(uid)), Is.True);
            var origin = transform.GetMapCoordinates(caster).Position;
            var velocitySum = Vector2.Zero;
            foreach (var shot in shots)
            {
                var velocity = physics.GetMapLinearVelocity(shot);
                var offset = transform.GetMapCoordinates(shot).Position - origin;
                Assert.That(velocity.Length(), Is.EqualTo(burst.ProjectileSpeed).Within(0.001));
                Assert.That(Vector2.Dot(Vector2.Normalize(velocity), Vector2.Normalize(offset)), Is.EqualTo(1).Within(0.001));
                Assert.That(entities.GetComponent<ProjectileComponent>(shot).Shooter, Is.EqualTo(caster));
                velocities.Add(shot, velocity);
                velocitySum += velocity;
            }
            Assert.That(velocitySum.Length(), Is.LessThan(0.001), "The burst must cover a complete circle.");
            transform.SetCoordinates(caster, map.GridCoords.Offset(new Vector2(8, 8)));
            transform.SetLocalRotation(caster, Angle.FromDegrees(73));
        });

        await pair.RunTicksSync(1);
        await server.WaitAssertion(() =>
        {
            foreach (var (shot, velocity) in velocities)
                Assert.That(physics.GetMapLinearVelocity(shot), Is.EqualTo(velocity));
        });
        await pair.RunSeconds(0.3f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Projectiles(entities), Is.Empty, "Missed burst shards must expire at short range.");
            Assert.That(actions.ValidAction(actions.GetAction(burst.ActionEntity).Value), Is.False,
                "Firing the burst must not reset its cooldown.");
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("open")]
    [TestCase("wall")]
    [TestCase("close")]
    public async Task BurstHitsSurroundingTargetsButCannotSkipWallsOrTravelFar(string scenario)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var actions = server.System<SharedActionsSystem>();
        var map = await pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid east = default;
        EntityUid north = default;
        EntityUid south = default;
        EntityUid far = default;
        float shardDamage = 0;

        await server.WaitPost(() =>
        {
            caster = entities.SpawnEntity("MobHuman", map.GridCoords);
            east = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(scenario == "close" ? 0.7f : 2, 0)));
            north = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(0, 2)));
            south = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(0, -2)));
            // Beyond the formation + flight distance + both colliders, but close enough to
            // catch an extra physics step while a shard's queued deletion is pending.
            far = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(-7.5f, 0)));
            if (scenario == "wall")
                entities.SpawnEntity("WallSolid", map.GridCoords.Offset(Vector2.UnitX));
            var burst = entities.EnsureComponent<CrystalBurstComponent>(caster);
            // Exercise collision behavior using the prototype's current balance, not a stale duplicate value.
            var prototype = server.ProtoMan.Index(burst.ProjectilePrototype);
            Assert.That(prototype.TryGetComponent<ProjectileComponent>(out var projectile, entities.ComponentFactory), Is.True);
            shardDamage = projectile.Damage.DamageDict["Piercing"].Float();
            // One crystal per direction makes each impact independently measurable.
            burst.CrystalCount = 4;
            // Place warning visuals beyond the wall/close target to exercise the safe launch origin.
            burst.FormationRadius = 1.7f;
            actions.PerformAction(caster, actions.GetAction(burst.ActionEntity).Value, new CrystalBurstEvent());
        });

        await pair.RunSeconds(0.2f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Piercing(entities, east), Is.Zero);
            Assert.That(Piercing(entities, north), Is.Zero);
            Assert.That(Piercing(entities, south), Is.Zero);
        });
        await pair.RunSeconds(0.6f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Piercing(entities, east), Is.EqualTo(scenario == "wall" ? 0 : shardDamage));
            Assert.That(Piercing(entities, north), Is.EqualTo(shardDamage));
            Assert.That(Piercing(entities, south), Is.EqualTo(shardDamage));
            Assert.That(Piercing(entities, caster), Is.Zero);
            Assert.That(Piercing(entities, far), Is.Zero);
            Assert.That(Projectiles(entities), Is.Empty);
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DenseBurstCoversTheGapsBetweenTheOriginalTwelveDirections()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var actions = server.System<SharedActionsSystem>();
        EntityUid caster = default;
        var targets = new List<EntityUid>();

        await server.WaitAssertion(() =>
        {
            caster = entities.SpawnEntity("MobHuman", map.GridCoords);
            foreach (var degrees in new[] { 15, 135, 255 })
            {
                var offset = Angle.FromDegrees(degrees).ToVec() * 3f;
                var target = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(offset));
                entities.RemoveComponent<PassiveDamageComponent>(target);
                targets.Add(target);
            }
            var burst = entities.EnsureComponent<CrystalBurstComponent>(caster);
            actions.PerformAction(caster, actions.GetAction(burst.ActionEntity).Value, new CrystalBurstEvent());
        });

        await pair.RunSeconds(0.8f);
        await server.WaitAssertion(() =>
        {
            foreach (var target in targets)
                Assert.That(Piercing(entities, target), Is.EqualTo(60), "The denser ring must hit targets between the old shot directions.");
            Assert.That(Piercing(entities, caster), Is.Zero);
            Assert.That(Projectiles(entities), Is.Empty);
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("remove")]
    [TestCase("death")]
    [TestCase("delete")]
    [TestCase("container")]
    public async Task InterruptedBurstRemovesWarningWithoutFiring(string reason)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var actions = server.System<SharedActionsSystem>();
        var map = await pair.CreateTestMap();
        EntityUid[] warning = [];
        EntityUid action = default;

        await server.WaitAssertion(() =>
        {
            var caster = entities.SpawnEntity("MobHuman", map.GridCoords);
            var burst = entities.EnsureComponent<CrystalBurstComponent>(caster);
            actions.PerformAction(caster, actions.GetAction(burst.ActionEntity).Value, new CrystalBurstEvent());
            warning = burst.Crystals.ToArray();
            action = burst.ActionEntity.Value;
            Assert.That(warning.Length, Is.EqualTo(24));
            switch (reason)
            {
                case "remove": entities.RemoveComponent<CrystalBurstComponent>(caster); break;
                case "death": server.System<MobStateSystem>().ChangeMobState(caster, MobState.Dead); break;
                case "delete": entities.DeleteEntity(caster); break;
                case "container":
                    var box = entities.SpawnEntity(null, map.GridCoords);
                    var containers = server.System<SharedContainerSystem>();
                    Assert.That(containers.Insert(caster, containers.EnsureContainer<Container>(box, "test")), Is.True);
                    break;
            }
        });

        // Inspect after release would occur, before an incorrectly fired projectile could expire.
        await pair.RunSeconds(0.5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(warning.All(uid => !entities.EntityExists(uid)), Is.True);
            Assert.That(Projectiles(entities), Is.Empty);
            if (reason is "remove" or "delete")
                Assert.That(entities.EntityExists(action), Is.False);
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    private static float Piercing(IEntityManager entities, EntityUid target)
        => entities.GetComponent<DamageableComponent>(target).Damage.DamageDict["Piercing"].Float();

    private static List<EntityUid> Projectiles(IEntityManager entities)
    {
        var result = new List<EntityUid>();
        var query = entities.EntityQueryEnumerator<ProjectileComponent>();
        while (query.MoveNext(out var uid, out _))
            result.Add(uid);
        return result;
    }
}
