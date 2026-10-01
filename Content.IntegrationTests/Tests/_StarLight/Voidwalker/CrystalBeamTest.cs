using System;
using System.Numerics;
using Content.Server._Starlight.Voidwalker;
using Content.Server.Atmos.Components;
using Content.Shared._Starlight.Voidwalker;
using Content.Shared.Actions;
using Content.Shared.Damage.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Starlight.Voidwalker;

[TestFixture]
public sealed class CrystalBeamTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          parent: MobHuman
          id: VoidwalkerBeamTestTarget
          components:
          - type: Fixtures
            fixtures:
              extra:
                shape: !type:PhysShapeCircle
                  radius: 0.35
                layer: [MobLayer]
                mask: [MobMask]

        - type: entity
          id: VoidwalkerBeamTestBlocker
          components:
          - type: Physics
            bodyType: Static
          - type: Fixtures
            fixtures:
              wall:
                shape: !type:PhysShapeAabb
                  bounds: "-0.49,-0.49,0.49,0.49"
                layer: [WallLayer]
        """;

    [TestCase(0, false)]
    [TestCase(90, false)]
    [TestCase(0, true)]
    public async Task ChargeThenTwelvePulsesPierceMobsButStopAtCover(int gridRotation, bool cover)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var transform = server.System<SharedTransformSystem>();
        var actions = server.System<SharedActionsSystem>();
        EntityUid caster = default, first = default, second = default, channel = default;
        CrystalBeamAbilityComponent ability = null;
        await server.WaitAssertion(() =>
        {
            transform.SetLocalRotation(map.Grid.Owner, Angle.FromDegrees(gridRotation));
            caster = entities.SpawnEntity("MobHuman", map.GridCoords);
            first = entities.SpawnEntity("VoidwalkerBeamTestTarget", map.GridCoords.Offset(new Vector2(3, 0)));
            second = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(6, 0)));
            foreach (var mob in new[] { caster, first, second })
            {
                entities.RemoveComponent<PassiveDamageComponent>(mob);
                entities.EnsureComponent<PressureImmunityComponent>(mob);
            }
            if (cover)
                entities.SpawnEntity("VoidwalkerBeamTestBlocker", map.GridCoords.Offset(new Vector2(4.5f, 0)));
            ability = entities.EnsureComponent<CrystalBeamAbilityComponent>(caster);
            var ev = new CrystalBeamEvent { Target = map.GridCoords.Offset(new Vector2(10, 0)) };
            actions.PerformAction(caster, actions.GetAction(ability.ActionEntity).Value, ev);
            Assert.That(ev.Handled, Is.True);
            channel = ability.Channel.Value;
            Assert.That(entities.EnsureComponent<CrystalBeamAbilityComponent>(caster).Channel, Is.EqualTo(channel));
            Assert.That(actions.ValidAction(actions.GetAction(ability.ActionEntity).Value), Is.False);
        });
        await pair.RunSeconds(0.8f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<CrystalBeamComponent>(channel).Firing, Is.False);
            Assert.That(Damage(entities, first, "Piercing"), Is.Zero);
        });
        await pair.RunSeconds(0.4f);
        await server.WaitAssertion(() =>
        {
            var beam = entities.GetComponent<CrystalBeamComponent>(channel);
            Assert.That(beam.Firing, Is.True);
            Assert.That(Damage(entities, first, "Piercing"), Is.EqualTo(6));
            Assert.That(Damage(entities, first, "Heat"), Is.EqualTo(12));
            Assert.That(beam.Length, cover ? Is.LessThan(4.5f) : Is.EqualTo(12));
        });
        await pair.RunSeconds(3.1f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(entities, first, "Piercing"), Is.EqualTo(72), "Twelve pulses, not one hit per fixture or frame.");
            Assert.That(Damage(entities, first, "Heat"), Is.EqualTo(144));
            Assert.That(Damage(entities, second, "Piercing"), Is.EqualTo(cover ? 0 : 72));
            Assert.That(Damage(entities, caster, "Piercing"), Is.Zero);
            Assert.That(entities.EntityExists(channel), Is.False);
            Assert.That(ability.Channel, Is.Null);
            Assert.That(actions.ValidAction(actions.GetAction(ability.ActionEntity).Value), Is.False);
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AimIsOwnedRateLimitedAndTurnsGraduallyWhileFollowingTheCaster()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var actions = server.System<SharedActionsSystem>();
        var system = server.System<CrystalBeamSystem>();
        var transform = server.System<SharedTransformSystem>();
        EntityUid caster = default, channel = default;
        CrystalBeamComponent beam = null;
        await server.WaitAssertion(() =>
        {
            caster = entities.SpawnEntity("MobHuman", map.GridCoords);
            var stranger = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(0, 3)));
            var ability = entities.EnsureComponent<CrystalBeamAbilityComponent>(caster);
            actions.PerformAction(caster, actions.GetAction(ability.ActionEntity).Value,
                new CrystalBeamEvent { Target = map.GridCoords.Offset(new Vector2(10, 0)) });
            channel = ability.Channel.Value;
            beam = entities.GetComponent<CrystalBeamComponent>(channel);
            var target = transform.ToMapCoordinates(map.GridCoords.Offset(new Vector2(0, 10)));
            Assert.That(system.TrySetAim(stranger, channel, target), Is.False);
            Assert.That(system.TrySetAim(caster, channel, new MapCoordinates(float.NaN, 0, target.MapId)), Is.False);
            Assert.That(system.TrySetAim(caster, channel, new MapCoordinates(1, 1, MapId.Nullspace)), Is.False);
            Assert.That(system.TrySetAim(caster, channel, target), Is.True);
            Assert.That(system.TrySetAim(caster, channel, target), Is.False, "Input floods must be rate-limited.");
            Assert.That(beam.Direction.Degrees, Is.EqualTo(0).Within(0.01), "Receiving input must not snap the beam.");
        });
        await pair.RunSeconds(0.5f);
        await server.WaitAssertion(() => Assert.That(beam.Direction.Degrees, Is.InRange(35, 50)));
        await pair.RunSeconds(0.65f);
        await server.WaitAssertion(() =>
        {
            Assert.That(beam.Direction.Degrees, Is.EqualTo(90).Within(0.1));
            var newPosition = map.GridCoords.Offset(new Vector2(2, 1));
            transform.SetCoordinates(caster, newPosition);
            Assert.That(transform.GetMapCoordinates(channel), Is.EqualTo(transform.ToMapCoordinates(newPosition)));
            var target = transform.ToMapCoordinates(newPosition.Offset(new Vector2(0, -10)));
            Assert.That(system.TrySetAim(caster, channel, target), Is.True);
        });
        await pair.RunSeconds(0.25f);
        await server.WaitAssertion(() =>
        {
            var turn = Math.Abs(Angle.ShortestDistance(Angle.FromDegrees(90), beam.Direction).Degrees);
            Assert.That(turn, Is.InRange(15, 27));
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("death", false)]
    [TestCase("remove", false)]
    [TestCase("delete", false)]
    [TestCase("container", true)]
    [TestCase("map", true)]
    public async Task InterruptionRemovesTheChannel(string reason, bool duringFire)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var actions = server.System<SharedActionsSystem>();
        EntityUid caster = default, channel = default;
        await server.WaitPost(() =>
        {
            caster = entities.SpawnEntity("MobHuman", map.GridCoords);
            var ability = entities.EnsureComponent<CrystalBeamAbilityComponent>(caster);
            actions.PerformAction(caster, actions.GetAction(ability.ActionEntity).Value,
                new CrystalBeamEvent { Target = map.GridCoords.Offset(new Vector2(10, 0)) });
            channel = ability.Channel.Value;
        });
        await pair.RunSeconds(duringFire ? 1.2f : 0.2f);
        await server.WaitPost(() =>
        {
            switch (reason)
            {
                case "death": server.System<MobStateSystem>().ChangeMobState(caster, MobState.Dead); break;
                case "remove": entities.RemoveComponent<CrystalBeamAbilityComponent>(caster); break;
                case "delete": entities.DeleteEntity(caster); break;
                case "container":
                    var container = entities.SpawnEntity(null, map.GridCoords);
                    var containers = server.System<SharedContainerSystem>();
                    containers.Insert(caster, containers.EnsureContainer<Container>(container, "beam-test"));
                    break;
                case "map": server.System<SharedTransformSystem>().DetachEntity(caster, entities.GetComponent<TransformComponent>(caster)); break;
            }
        });
        await pair.RunSeconds(0.2f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(channel), Is.False);
            if (reason == "map")
                entities.DeleteEntity(caster);
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    private static float Damage(IEntityManager entities, EntityUid target, string kind)
        => entities.GetComponent<DamageableComponent>(target).Damage.DamageDict[kind].Float();

    [Test]
    public async Task BeamWidthAndMovingCoverAreRecalculatedBetweenPulses()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var actions = server.System<SharedActionsSystem>();
        var transform = server.System<SharedTransformSystem>();
        EntityUid target = default, wall = default, channel = default;
        await server.WaitPost(() =>
        {
            var caster = entities.SpawnEntity("MobHuman", map.GridCoords);
            // The center ray misses this target; the edge of the beam must still damage it.
            target = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(4, 0.42f)));
            entities.RemoveComponent<PassiveDamageComponent>(target);
            entities.EnsureComponent<PressureImmunityComponent>(target);
            wall = entities.SpawnEntity("VoidwalkerBeamTestBlocker", map.GridCoords.Offset(new Vector2(2, 3)));
            var ability = entities.EnsureComponent<CrystalBeamAbilityComponent>(caster);
            actions.PerformAction(caster, actions.GetAction(ability.ActionEntity).Value,
                new CrystalBeamEvent { Target = map.GridCoords.Offset(new Vector2(10, 0)) });
            channel = ability.Channel.Value;
        });
        await pair.RunSeconds(1.15f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(entities, target, "Piercing"), Is.EqualTo(6));
            transform.SetCoordinates(wall, map.GridCoords.Offset(new Vector2(2, 0)));
        });
        await pair.RunSeconds(0.5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(entities, target, "Piercing"), Is.EqualTo(6));
            Assert.That(entities.GetComponent<CrystalBeamComponent>(channel).Length, Is.LessThan(2));
            entities.DeleteEntity(wall);
        });
        await pair.RunSeconds(0.25f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(entities, target, "Piercing"), Is.EqualTo(12));
            Assert.That(entities.GetComponent<CrystalBeamComponent>(channel).Length, Is.EqualTo(12));
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PausedChannelDoesNotFireCatchUpPulses()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var actions = server.System<SharedActionsSystem>();
        var meta = server.System<MetaDataSystem>();
        EntityUid channel = default, target = default;
        await server.WaitPost(() =>
        {
            var caster = entities.SpawnEntity("MobHuman", map.GridCoords);
            target = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(3, 0)));
            entities.RemoveComponent<PassiveDamageComponent>(target);
            entities.EnsureComponent<PressureImmunityComponent>(target);
            var ability = entities.EnsureComponent<CrystalBeamAbilityComponent>(caster);
            actions.PerformAction(caster, actions.GetAction(ability.ActionEntity).Value,
                new CrystalBeamEvent { Target = map.GridCoords.Offset(new Vector2(10, 0)) });
            channel = ability.Channel.Value;
        });
        await pair.RunSeconds(1.15f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(entities, target, "Piercing"), Is.EqualTo(6));
            meta.SetEntityPaused(channel, true);
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(entities, target, "Piercing"), Is.EqualTo(6));
            meta.SetEntityPaused(channel, false);
        });
        await pair.RunSeconds(0.25f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(entities, target, "Piercing"), Is.EqualTo(12));
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NetworkStateReplicatesAndDetachingThePlayerStopsTheBeam()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var actions = server.System<SharedActionsSystem>();
        EntityUid caster = default, channel = default;
        NetEntity netChannel = default;
        EntityUid? oldBody = null;
        await server.WaitPost(() =>
        {
            oldBody = pair.Player.AttachedEntity;
            caster = entities.SpawnEntity("MobHuman", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player, caster);
            var ability = entities.EnsureComponent<CrystalBeamAbilityComponent>(caster);
            actions.PerformAction(caster, actions.GetAction(ability.ActionEntity).Value,
                new CrystalBeamEvent { Target = map.GridCoords.Offset(new Vector2(10, 0)) });
            channel = ability.Channel.Value;
            netChannel = entities.GetNetEntity(channel);
        });
        await pair.RunTicksSync(20);
        await client.WaitAssertion(() =>
        {
            var local = client.EntMan.GetEntity(netChannel);
            var beam = client.EntMan.GetComponent<CrystalBeamComponent>(local);
            Assert.That(beam.Caster, Is.Not.Null);
            Assert.That(beam.Firing, Is.False);
            Assert.That(beam.EndAt - beam.FireAt, Is.EqualTo(TimeSpan.FromSeconds(3)));
        });
        await pair.RunSeconds(1.1f);
        await client.WaitAssertion(() =>
        {
            var beam = client.EntMan.GetComponent<CrystalBeamComponent>(client.EntMan.GetEntity(netChannel));
            Assert.That(beam.Firing, Is.True);
            Assert.That(beam.Length, Is.EqualTo(12));
        });
        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player, oldBody));
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() => Assert.That(entities.EntityExists(channel), Is.False));
        await client.WaitAssertion(() => Assert.That(client.EntMan.TryGetEntity(netChannel, out _), Is.False));
        await server.WaitPost(() => entities.DeleteEntity(map.MapUid));
        await pair.CleanReturnAsync();
    }
}
