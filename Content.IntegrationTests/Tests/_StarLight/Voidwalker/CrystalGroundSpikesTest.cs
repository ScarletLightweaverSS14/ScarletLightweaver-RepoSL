using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Starlight.Voidwalker;
using Content.Shared._Starlight.Voidwalker;
using Content.Shared.Actions;
using Content.Shared.Damage.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._Starlight.Voidwalker;

[TestFixture]
public sealed class CrystalGroundSpikesTest
{
    [TestCase(0)]
    [TestCase(90)]
    public async Task EruptionsTravelAlongTheOriginalGridDirection(int gridAngle)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var actions = server.System<SharedActionsSystem>();
        var transform = server.System<SharedTransformSystem>();
        EntityUid caster = default;
        EntityUid wave = default;

        await server.WaitAssertion(() =>
        {
            MakeFloor(server.System<SharedMapSystem>(), map);
            transform.SetLocalRotation(map.Grid.Owner, Angle.FromDegrees(gridAngle));
            caster = entities.SpawnEntity("MobHuman", At(map, 0.5f, 0.5f));
            var ability = entities.EnsureComponent<CrystalGroundSpikesComponent>(caster);
            var aim = entities.SpawnEntity(null, At(map, 10.5f, 0.5f));
            var ev = new CrystalGroundSpikesEvent { Target = new EntityCoordinates(aim, Vector2.Zero) };
            actions.PerformAction(caster, actions.GetAction(ability.ActionEntity).Value, ev);
            Assert.That(ev.Handled, Is.True);
            wave = ability.ActiveFaultLine.Value;
            Assert.That(Spikes(entities), Is.Empty, "The stomp should precede the first eruption.");
            Assert.That(actions.ValidAction(actions.GetAction(ability.ActionEntity).Value), Is.False);
            Assert.That(entities.EnsureComponent<CrystalGroundSpikesComponent>(caster).ActiveFaultLine, Is.EqualTo(wave));
            transform.SetCoordinates(caster, At(map, 0.5f, 2.5f));
            transform.SetCoordinates(aim, At(map, 0.5f, 10.5f));
        });

        // Tick callbacks run before CurTick advances. Leave a full tick beyond the initial delay.
        await pair.RunSeconds(0.3f);
        await server.WaitAssertion(() => Assert.That(Spikes(entities).Count, Is.EqualTo(1)));
        await pair.RunSeconds(0.16f);
        await server.WaitAssertion(() => Assert.That(Spikes(entities).Count, Is.EqualTo(2)));
        await pair.RunSeconds(1.1f);
        await server.WaitAssertion(() =>
        {
            var spikes = Spikes(entities);
            Assert.That(spikes.Count, Is.EqualTo(8));
            var positions = spikes.Select(uid => transform.WithEntityId(entities.GetComponent<TransformComponent>(uid).Coordinates,
                map.Grid.Owner).Position).OrderBy(pos => pos.X).ToArray();
            for (var i = 0; i < positions.Length; i++)
            {
                Assert.That(positions[i].X, Is.EqualTo(1.5f + i).Within(0.01));
                Assert.That(positions[i].Y, Is.EqualTo(0.5f).Within(0.01));
            }
            Assert.That(entities.EntityExists(wave), Is.False);
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DirectHitsDealDamageAndStunWithoutStackingOrContactDamage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var actions = server.System<SharedActionsSystem>();
        EntityUid caster = default;
        EntityUid victim = default;
        EntityUid highStaminaVictim = default;

        await server.WaitPost(() =>
        {
            MakeFloor(server.System<SharedMapSystem>(), map);
            caster = entities.SpawnEntity("MobHuman", At(map, 0.5f, 0.5f));
            // Both adjacent eruptions overlap this victim, but should damage it only once.
            victim = entities.SpawnEntity("MobHuman", At(map, 2, 0.5f));
            highStaminaVictim = entities.SpawnEntity("MobHuman", At(map, 4.5f, 0.5f));
            // Humans regenerate brute damage every second. Isolate eruption damage from that healing.
            entities.RemoveComponent<PassiveDamageComponent>(victim);
            entities.RemoveComponent<PassiveDamageComponent>(highStaminaVictim);
            var stamina = entities.GetComponent<StaminaComponent>(highStaminaVictim);
            stamina.BaseCritThreshold = 500;
            stamina.CritThreshold = 500;
            var ability = entities.EnsureComponent<CrystalGroundSpikesComponent>(caster);
            actions.PerformAction(caster, actions.GetAction(ability.ActionEntity).Value,
                new CrystalGroundSpikesEvent { Target = At(map, 10.5f, 0.5f) });
        });

        await pair.RunSeconds(1.2f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Piercing(entities, victim), Is.EqualTo(45));
            Assert.That(Piercing(entities, highStaminaVictim), Is.EqualTo(45));
            Assert.That(Piercing(entities, caster), Is.Zero);
            Assert.That(entities.GetComponent<StaminaComponent>(victim).Critical, Is.True);
            Assert.That(entities.GetComponent<StaminaComponent>(highStaminaVictim).StaminaDamage, Is.EqualTo(150));
            // Walk the caster into a lingering crystal: the eruption must not leave a damage aura.
            server.System<SharedTransformSystem>().SetCoordinates(caster, At(map, 1.5f, 0.5f));
        });
        await pair.RunSeconds(0.4f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Piercing(entities, caster), Is.Zero);
            Assert.That(Piercing(entities, victim), Is.EqualTo(45));
        });
        await pair.RunSeconds(4);
        await server.WaitAssertion(() =>
        {
            Assert.That(Spikes(entities), Is.Empty);
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FaultLineStopsAtWallsAndFloorGaps(bool gap)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var actions = server.System<SharedActionsSystem>();
        EntityUid victim = default;

        await server.WaitPost(() =>
        {
            var mapSystem = server.System<SharedMapSystem>();
            MakeFloor(mapSystem, map);
            if (gap)
                mapSystem.SetTile(map.Grid, new Vector2i(3, 0), Tile.Empty);
            else
                entities.SpawnEntity("WallSolid", At(map, 3.5f, 0.5f));

            var caster = entities.SpawnEntity("MobHuman", At(map, 0.5f, 0.5f));
            victim = entities.SpawnEntity("MobHuman", At(map, 4.5f, 0.5f));
            var ability = entities.EnsureComponent<CrystalGroundSpikesComponent>(caster);
            actions.PerformAction(caster, actions.GetAction(ability.ActionEntity).Value,
                new CrystalGroundSpikesEvent { Target = At(map, 10.5f, 0.5f) });
        });

        await pair.RunSeconds(1.2f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Spikes(entities).Count, Is.EqualTo(2));
            Assert.That(Piercing(entities, victim), Is.Zero);
            Assert.That(entities.GetComponent<StaminaComponent>(victim).StaminaDamage, Is.Zero);
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("space")]
    [TestCase("wall")]
    [TestCase("self")]
    public async Task InvalidCastDoesNotSpendCooldown(string reason)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var actions = server.System<SharedActionsSystem>();
        await server.WaitAssertion(() =>
        {
            MakeFloor(server.System<SharedMapSystem>(), map);
            if (reason == "wall")
                entities.SpawnEntity("WallSolid", At(map, 1.5f, 0.5f));
            var origin = reason == "space" ? new EntityCoordinates(map.MapUid, new Vector2(50, 50)) : At(map, 0.5f, 0.5f);
            var caster = entities.SpawnEntity("MobHuman", origin);
            var ability = entities.EnsureComponent<CrystalGroundSpikesComponent>(caster);
            var action = actions.GetAction(ability.ActionEntity).Value;
            var ev = new CrystalGroundSpikesEvent { Target = reason == "self" ? origin : origin.Offset(new Vector2(10, 0)) };
            actions.PerformAction(caster, action, ev);
            Assert.That(ev.Handled, Is.False);
            Assert.That(action.Comp.Cooldown, Is.Null);
            Assert.That(ability.ActiveFaultLine, Is.Null);
            Assert.That(Spikes(entities), Is.Empty);
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("remove")]
    [TestCase("death")]
    [TestCase("delete")]
    public async Task InterruptedCasterStopsFutureEruptions(string reason)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var actions = server.System<SharedActionsSystem>();
        EntityUid caster = default;
        EntityUid wave = default;
        await server.WaitPost(() =>
        {
            MakeFloor(server.System<SharedMapSystem>(), map);
            caster = entities.SpawnEntity("MobHuman", At(map, 0.5f, 0.5f));
            var ability = entities.EnsureComponent<CrystalGroundSpikesComponent>(caster);
            actions.PerformAction(caster, actions.GetAction(ability.ActionEntity).Value,
                new CrystalGroundSpikesEvent { Target = At(map, 10.5f, 0.5f) });
            wave = ability.ActiveFaultLine.Value;
        });
        await pair.RunSeconds(0.3f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Spikes(entities).Count, Is.EqualTo(1));
            switch (reason)
            {
                case "remove": entities.RemoveComponent<CrystalGroundSpikesComponent>(caster); break;
                case "death": server.System<MobStateSystem>().ChangeMobState(caster, MobState.Dead); break;
                case "delete": entities.DeleteEntity(caster); break;
            }
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(wave), Is.False);
            Assert.That(Spikes(entities).Count, Is.EqualTo(1), "Existing spikes remain until their own lifetime expires.");
        });
        await pair.RunSeconds(4);
        await server.WaitAssertion(() =>
        {
            Assert.That(Spikes(entities), Is.Empty);
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    private static EntityCoordinates At(TestMapData map, float x, float y) => new(map.Grid.Owner, x, y);

    private static void MakeFloor(SharedMapSystem system, TestMapData map)
    {
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = -1; x <= 12; x++)
        for (var y = -1; y <= 3; y++)
            tiles.Add((new Vector2i(x, y), map.Tile.Tile));
        system.SetTiles(map.Grid, tiles);
    }

    private static float Piercing(IEntityManager entities, EntityUid target)
        => entities.GetComponent<DamageableComponent>(target).Damage.DamageDict["Piercing"].Float();

    private static List<EntityUid> Spikes(IEntityManager entities)
    {
        var result = new List<EntityUid>();
        var query = entities.EntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var uid, out var meta))
        {
            if (meta.EntityPrototype?.ID == "VoidwalkerGroundSpike")
                result.Add(uid);
        }
        return result;
    }
}
