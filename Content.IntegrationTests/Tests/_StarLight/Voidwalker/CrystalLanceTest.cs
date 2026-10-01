using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Starlight.Voidwalker;
using Content.Server.Atmos.Components;
using Content.Shared._Starlight.Voidwalker;
using Content.Shared.Actions;
using Content.Shared.Damage.Components;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Sprite;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._Starlight.Voidwalker;

[TestFixture]
public sealed class CrystalLanceTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          parent: ClothingOuterArmorBasic
          id: VoidwalkerLanceTestArmor
          components:
          - type: Armor
            modifiers:
              coefficients:
                Piercing: 0.5

        - type: entity
          parent: VoidwalkerLanceTestArmor
          id: VoidwalkerLanceTestArmorFlat
          components:
          - type: Armor
            modifiers:
              coefficients:
                Piercing: 0.5
              flatReductions:
                Piercing: 10

        - type: entity
          id: VoidwalkerLanceTestBlocker
          components:
          - type: Physics
            bodyType: Static
          - type: Fixtures
            fixtures:
              wall:
                shape: !type:PhysShapeAabb
                  bounds: "-0.49,-0.49,0.49,0.49"
                layer: [WallLayer]

        - type: entity
          parent: MobHuman
          id: VoidwalkerLanceTestMultiFixtureTarget
          components:
          - type: Fixtures
            fixtures:
              extra:
                shape: !type:PhysShapeCircle
                  radius: 0.35
                layer: [MobLayer]
                mask: [MobMask]
        """;

    [TestCase(0)]
    [TestCase(90)]
    public async Task ChargeGrowsAndUsesTheClickedPointThenFliesStraight(int gridAngle)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var actions = server.System<SharedActionsSystem>();
        var transform = server.System<SharedTransformSystem>();
        var physics = server.System<SharedPhysicsSystem>();
        var scales = server.System<SharedScaleVisualsSystem>();
        EntityUid caster = default;
        EntityUid charge = default;
        EntityUid shot = default;
        CrystalLanceComponent lance = null;
        Vector2 velocity = default;

        await server.WaitAssertion(() =>
        {
            transform.SetLocalRotation(map.Grid.Owner, Angle.FromDegrees(gridAngle));
            caster = entities.SpawnEntity("MobHuman", map.GridCoords);
            lance = entities.EnsureComponent<CrystalLanceComponent>(caster);
            var action = actions.GetAction(lance.ActionEntity).Value;
            var selfClick = new CrystalLanceEvent { Target = map.GridCoords };
            actions.PerformAction(caster, action, selfClick);
            Assert.That(selfClick.Handled, Is.False);
            Assert.That(action.Comp.Cooldown, Is.Null);
            var cursor = entities.SpawnEntity(null, map.GridCoords.Offset(new Vector2(10, 0)));
            var ev = new CrystalLanceEvent { Target = new EntityCoordinates(cursor, Vector2.Zero) };
            actions.PerformAction(caster, action, ev);
            Assert.That(ev.Handled, Is.True);
            charge = lance.ChargingLance.Value;
            Assert.That(Projectiles(entities), Is.Empty);
            Assert.That(scales.GetSpriteScale(charge), Is.EqualTo(lance.InitialScale));
            Assert.That(actions.ValidAction(action), Is.False);
            Assert.That(entities.EnsureComponent<CrystalLanceComponent>(caster).ChargingLance, Is.EqualTo(charge));
            transform.SetCoordinates(cursor, map.GridCoords.Offset(new Vector2(0, 10)));
        });

        await pair.RunSeconds(0.3f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Projectiles(entities), Is.Empty, "The lance must charge before becoming a damaging projectile.");
            Assert.That(scales.GetSpriteScale(charge).X, Is.GreaterThan(lance.InitialScale.X).And.LessThan(lance.FullScale.X));
            // Moving during charge changes the launch origin, but not the selected world point.
            transform.SetCoordinates(caster, map.GridCoords.Offset(new Vector2(0, 2)));
        });
        await pair.RunSeconds(0.6f);
        await server.WaitAssertion(() =>
        {
            var shots = Projectiles(entities);
            Assert.That(shots.Count, Is.EqualTo(1));
            shot = shots.Single();
            Assert.That(entities.EntityExists(charge), Is.False);
            Assert.That(lance.ChargingLance, Is.Null);
            velocity = physics.GetMapLinearVelocity(shot);
            var expected = Vector2.Normalize(lance.Target.Position - transform.GetMapCoordinates(caster).Position) * lance.ProjectileSpeed;
            Assert.That(Vector2.Distance(velocity, expected), Is.LessThan(0.001));
            Assert.That(velocity.Length(), Is.EqualTo(60).Within(0.001));
            Assert.That(entities.GetComponent<ProjectileComponent>(shot).Shooter, Is.EqualTo(caster));
            Assert.That(scales.GetSpriteScale(shot), Is.EqualTo(lance.FullScale));
            transform.SetCoordinates(caster, map.GridCoords.Offset(new Vector2(0, 8)));
            transform.SetLocalRotation(caster, Angle.FromDegrees(73));
        });
        await pair.RunTicksSync(1);
        await server.WaitAssertion(() => Assert.That(physics.GetMapLinearVelocity(shot), Is.EqualTo(velocity)));
        await pair.RunSeconds(0.5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(shot), Is.False);
            Assert.That(actions.ValidAction(actions.GetAction(lance.ActionEntity).Value), Is.False);
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("three", 3)]
    [TestCase("multi", 3)]
    [TestCase("wall", 1)]
    [TestCase("inert", 1)]
    [TestCase("charge-cover", 0)]
    public async Task LancePiercesDistinctMobsButStopsAtCover(string scenario, int expectedHits)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var actions = server.System<SharedActionsSystem>();
        EntityUid caster = default;
        var victims = new List<EntityUid>();

        await server.WaitAssertion(() =>
        {
            caster = entities.SpawnEntity("MobHuman", map.GridCoords);
            for (var i = 1; i <= 4; i++)
            {
                var prototype = scenario == "multi" && i == 1 ? "VoidwalkerLanceTestMultiFixtureTarget" : "MobHuman";
                var victim = entities.SpawnEntity(prototype, map.GridCoords.Offset(new Vector2(i * 3, 0)));
                // Measure projectile damage independently of human regeneration and vacuum damage.
                entities.RemoveComponent<PassiveDamageComponent>(victim);
                entities.EnsureComponent<PressureImmunityComponent>(victim);
                victims.Add(victim);
            }
            if (scenario is "wall" or "inert")
                entities.SpawnEntity(scenario == "wall" ? "WallSolid" : "VoidwalkerLanceTestBlocker",
                    map.GridCoords.Offset(new Vector2(4.5f, 0)));
            var lance = entities.EnsureComponent<CrystalLanceComponent>(caster);
            if (scenario == "charge-cover")
            {
                lance.ChargeOffset = 2;
                entities.SpawnEntity("WallSolid", map.GridCoords.Offset(Vector2.UnitX));
            }
            var ev = new CrystalLanceEvent { Target = map.GridCoords.Offset(new Vector2(15, 0)) };
            actions.PerformAction(caster, actions.GetAction(lance.ActionEntity).Value, ev);
            Assert.That(ev.Handled, Is.True);
        });
        await pair.RunSeconds(1.4f);
        await server.WaitAssertion(() =>
        {
            for (var i = 0; i < victims.Count; i++)
                Assert.That(Piercing(entities, victims[i]), Is.EqualTo(i < expectedHits ? 120 : 0), $"Target {i + 1}");
            Assert.That(Piercing(entities, caster), Is.Zero);
            Assert.That(Projectiles(entities), Is.Empty);
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("VoidwalkerLanceProjectile", null, 120f)]
    [TestCase("VoidwalkerLanceProjectile", "VoidwalkerLanceTestArmor", 99f)]
    [TestCase("VoidwalkerLanceProjectile", "VoidwalkerLanceTestArmorFlat", 96.11f)]
    [TestCase("VoidwalkerCrystalProjectile", "VoidwalkerLanceTestArmor", 12.5f)]
    public async Task ArmorPenetrationBypassesTheConfiguredFractionOfWornArmor(string projectile, string armorPrototype, float expected)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid target = default;

        await server.WaitAssertion(() =>
        {
            var caster = entities.SpawnEntity("MobHuman", map.GridCoords);
            target = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(2, 0)));
            if (armorPrototype != null)
            {
                var armor = entities.SpawnEntity(armorPrototype, map.GridCoords);
                Assert.That(server.System<InventorySystem>().TryEquip(target, armor, "outerClothing", force: true), Is.True);
            }
            var shot = entities.SpawnEntity(projectile, map.GridCoords);
            server.System<SharedGunSystem>().ShootProjectile(shot, Vector2.UnitX, Vector2.Zero, caster, caster, 20);
        });
        await pair.RunSeconds(0.4f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Piercing(entities, target), Is.EqualTo(expected).Within(0.01));
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("remove")]
    [TestCase("death")]
    [TestCase("delete")]
    [TestCase("container")]
    public async Task InterruptedChargeCleansUpWithoutFiring(string reason)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var actions = server.System<SharedActionsSystem>();
        EntityUid charge = default;
        EntityUid action = default;

        await server.WaitAssertion(() =>
        {
            var caster = entities.SpawnEntity("MobHuman", map.GridCoords);
            var lance = entities.EnsureComponent<CrystalLanceComponent>(caster);
            action = lance.ActionEntity.Value;
            actions.PerformAction(caster, actions.GetAction(action).Value,
                new CrystalLanceEvent { Target = map.GridCoords.Offset(new Vector2(15, 0)) });
            charge = lance.ChargingLance.Value;
            switch (reason)
            {
                case "remove": entities.RemoveComponent<CrystalLanceComponent>(caster); break;
                case "death": server.System<MobStateSystem>().ChangeMobState(caster, MobState.Dead); break;
                case "delete": entities.DeleteEntity(caster); break;
                case "container":
                    var box = entities.SpawnEntity(null, map.GridCoords);
                    var containers = server.System<SharedContainerSystem>();
                    Assert.That(containers.Insert(caster, containers.EnsureContainer<Container>(box, "test")), Is.True);
                    break;
            }
        });
        // A mistakenly fired lance would still be in flight at this point.
        await pair.RunSeconds(0.9f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(charge), Is.False);
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
