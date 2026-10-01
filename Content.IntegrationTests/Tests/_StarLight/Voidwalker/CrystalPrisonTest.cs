using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Starlight.Voidwalker;
using Content.Server.Atmos.Components;
using Content.Shared._Starlight.Voidwalker;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._Starlight.Voidwalker;

[TestFixture]
public sealed class CrystalPrisonTest
{
    // A human-sized body without a movement controller, so the test can drive its velocity directly.
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: VoidwalkerPrisonTestBody
          components:
          - type: Physics
            bodyType: Dynamic
          - type: Fixtures
            fixtures:
              body:
                shape: !type:PhysShapeCircle
                  radius: 0.35
                layer: [MobLayer]
                mask: [MobMask]
        """;

    [TestCase(0)]
    [TestCase(90)]
    public async Task CageBlocksMovementUntilBrokenThenExpiresWithoutCaster(int gridAngle)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var actions = server.System<SharedActionsSystem>();
        var transform = server.System<SharedTransformSystem>();
        var physics = server.System<SharedPhysicsSystem>();
        var damage = server.System<DamageableSystem>();
        var interaction = server.System<SharedInteractionSystem>();
        EntityUid caster = default;
        EntityUid victim = default;
        EntityUid body = default;
        EntityUid eastCrystal = default;
        EntityUid action = default;
        var direction = Angle.FromDegrees(gridAngle).RotateVec(Vector2.UnitX);
        var center = At(map, 4.5f, 0.5f);

        async Task PushBody(float seconds)
        {
            // Apply continuous movement instead of a single impulse that tile friction stops.
            for (var i = 0; i < pair.SecondsToTicks(seconds); i++)
            {
                await server.WaitPost(() => physics.SetLinearVelocity(body, direction * 3));
                await pair.RunTicksSync(1);
            }
        }

        await server.WaitAssertion(() =>
        {
            MakeFloor(server.System<SharedMapSystem>(), map);
            transform.SetLocalRotation(map.Grid.Owner, Angle.FromDegrees(gridAngle));
            caster = entities.SpawnEntity("MobHuman", At(map, 0.5f, 0.5f));
            victim = entities.SpawnEntity("MobHuman", center);
            // The empty test map has no atmosphere; isolate the cage from vacuum barotrauma.
            entities.EnsureComponent<PressureImmunityComponent>(victim);
            body = entities.SpawnEntity("VoidwalkerPrisonTestBody", center);
            var aim = entities.SpawnEntity(null, At(map, 4.7f, 0.6f));
            var prison = entities.EnsureComponent<CrystalPrisonComponent>(caster);
            action = prison.ActionEntity.Value;
            var ev = new CrystalPrisonEvent { Target = new EntityCoordinates(aim, Vector2.Zero) };
            actions.PerformAction(caster, actions.GetAction(action).Value, ev);
            Assert.That(ev.Handled, Is.True);
            var crystals = Crystals(entities);
            Assert.That(crystals.Count, Is.EqualTo(8));
            var expected = prison.Offsets.Select(offset => center.Position + (Vector2) offset).ToHashSet();
            var actual = crystals.Select(uid => transform.WithEntityId(entities.GetComponent<TransformComponent>(uid).Coordinates,
                map.Grid.Owner).Position).ToHashSet();
            Assert.That(actual, Is.EquivalentTo(expected), "The cage must surround the tile and leave its center empty.");
            eastCrystal = crystals.Single(uid => transform.WithEntityId(entities.GetComponent<TransformComponent>(uid).Coordinates,
                map.Grid.Owner).Position == center.Position + Vector2.UnitX);
            Assert.That(actions.ValidAction(actions.GetAction(action).Value), Is.False);
            Assert.That(entities.EnsureComponent<CrystalPrisonComponent>(caster), Is.SameAs(prison));
            foreach (var offset in prison.Offsets)
            {
                Assert.That(interaction.InRangeUnobstructed(transform.ToMapCoordinates(center),
                    transform.ToMapCoordinates(center.Offset((Vector2) offset * 2)), range: 0,
                    collisionMask: CollisionGroup.Impassable), Is.False, "The perimeter must have no movement gaps.");
            }
            transform.SetCoordinates(aim, At(map, 10.5f, 3.5f));
        });

        await PushBody(0.5f);
        await server.WaitAssertion(() =>
        {
            var position = transform.WithEntityId(entities.GetComponent<TransformComponent>(body).Coordinates, map.Grid.Owner).Position;
            Assert.That(position.X, Is.GreaterThan(4.5f).And.LessThan(4.8f), "A moving body must collide with the cage.");
            Assert.That(entities.GetComponent<DamageableComponent>(victim).TotalDamage, Is.EqualTo(FixedPoint2.Zero));
            damage.TryChangeDamage(eastCrystal, new DamageSpecifier { DamageDict = { ["Blunt"] = 119 } }, ignoreResistances: true);
        });
        await pair.RunTicksSync(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(eastCrystal), Is.True, "The prison crystal has a 120 damage threshold.");
            damage.TryChangeDamage(eastCrystal, new DamageSpecifier { DamageDict = { ["Blunt"] = 1 } }, ignoreResistances: true);
        });
        await pair.RunSeconds(0.1f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(eastCrystal), Is.False);
            Assert.That(Crystals(entities).Count, Is.EqualTo(7));
            Assert.That(interaction.InRangeUnobstructed(transform.ToMapCoordinates(center),
                transform.ToMapCoordinates(center.Offset(Vector2.UnitX * 2)), range: 0,
                collisionMask: CollisionGroup.Impassable), Is.True, "Breaking a crystal must open an escape route.");
        });
        await PushBody(0.6f);
        await server.WaitAssertion(() =>
        {
            var position = transform.WithEntityId(entities.GetComponent<TransformComponent>(body).Coordinates, map.Grid.Owner).Position;
            Assert.That(position.X, Is.GreaterThan(5.5f), "The same body must pass through the broken crystal's tile.");
            entities.DeleteEntity(caster);
        });
        await pair.RunSeconds(10.5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Crystals(entities), Is.Empty);
            Assert.That(entities.EntityExists(action), Is.False);
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("WallSolid")]
    [TestCase("MobHuman")]
    public async Task OccupiedEdgeIsNotOverwritten(string occupantPrototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var actions = server.System<SharedActionsSystem>();
        await server.WaitAssertion(() =>
        {
            MakeFloor(server.System<SharedMapSystem>(), map);
            var caster = entities.SpawnEntity("MobHuman", At(map, 0.5f, 0.5f));
            var occupied = At(map, 5.5f, 0.5f);
            var occupant = entities.SpawnEntity(occupantPrototype, occupied);
            var prison = entities.EnsureComponent<CrystalPrisonComponent>(caster);
            var ev = new CrystalPrisonEvent { Target = At(map, 4.5f, 0.5f) };
            actions.PerformAction(caster, actions.GetAction(prison.ActionEntity).Value, ev);
            Assert.That(ev.Handled, Is.True);
            var crystals = Crystals(entities);
            Assert.That(crystals.Count, Is.EqualTo(7));
            Assert.That(crystals.Any(uid => entities.GetComponent<TransformComponent>(uid).Coordinates == occupied), Is.False);
            Assert.That(entities.EntityExists(occupant), Is.True);
            Assert.That(entities.GetComponent<DamageableComponent>(occupant).TotalDamage, Is.EqualTo(FixedPoint2.Zero));
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("space")]
    [TestCase("gap")]
    [TestCase("range")]
    [TestCase("wall")]
    [TestCase("container")]
    [TestCase("empty")]
    public async Task RejectedCastCreatesNoCrystalsAndSpendsNoCooldown(string reason)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var actions = server.System<SharedActionsSystem>();
        await server.WaitAssertion(() =>
        {
            var maps = server.System<SharedMapSystem>();
            MakeFloor(maps, map);
            var caster = entities.SpawnEntity("MobHuman", At(map, 0.5f, 0.5f));
            var prison = entities.EnsureComponent<CrystalPrisonComponent>(caster);
            var target = At(map, 4.5f, 0.5f);
            switch (reason)
            {
                case "space": maps.SetTile(map.Grid, new Vector2i(4, 0), Tile.Empty); break;
                case "gap": maps.SetTile(map.Grid, new Vector2i(5, 1), Tile.Empty); break;
                case "range": target = At(map, 10.5f, 0.5f); break;
                case "wall": entities.SpawnEntity("WallSolid", At(map, 2.5f, 0.5f)); break;
                case "empty": prison.Offsets.Clear(); break;
                case "container":
                    var box = entities.SpawnEntity(null, At(map, 0.5f, 0.5f));
                    var containers = server.System<SharedContainerSystem>();
                    Assert.That(containers.Insert(caster, containers.EnsureContainer<Container>(box, "test")), Is.True);
                    break;
            }
            var action = actions.GetAction(prison.ActionEntity).Value;
            var ev = new CrystalPrisonEvent { Target = target };
            actions.PerformAction(caster, action, ev);
            Assert.That(ev.Handled, Is.False);
            Assert.That(action.Comp.Cooldown, Is.Null);
            Assert.That(Crystals(entities), Is.Empty);
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    private static EntityCoordinates At(TestMapData map, float x, float y) => new(map.Grid.Owner, x, y);

    private static void MakeFloor(SharedMapSystem system, TestMapData map)
    {
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = -2; x <= 12; x++)
        for (var y = -3; y <= 4; y++)
            tiles.Add((new Vector2i(x, y), map.Tile.Tile));
        system.SetTiles(map.Grid, tiles);
    }

    private static List<EntityUid> Crystals(IEntityManager entities)
    {
        var result = new List<EntityUid>();
        var query = entities.EntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var uid, out var meta))
        {
            if (meta.EntityPrototype?.ID == "VoidwalkerPrisonCrystal")
                result.Add(uid);
        }
        return result;
    }
}
