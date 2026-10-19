#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Starlight.RTS;
using Content.Server._Starlight.RTS.Components;
using Content.Server.NPC.Components;
using Content.Shared._Starlight.RTS;
using Content.Shared._Starlight.CCVar;
using Content.Shared.Movement.Components;
using Content.Shared.NPC;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Starlight.RTS;

[TestOf(typeof(RtsCommandSystem))]
public sealed class RtsCommandTests : GameTest
{
    [SetUp]
    public async Task ConfigureSimulation()
    {
        await OverrideCVar(Side.Server, CVars.NetTickrate, 30);
        await OverrideCVar(Side.Server, StarlightCCVars.DisablePathfinding, false);
    }

    [Test]
    public async Task OrdersValidateCommanderOwnershipBattlefieldAndCoordinates()
    {
        EntityUid battlefield = default;
        EntityUid otherMap = default;
        try
        {
            await Server.WaitAssertion(() =>
            {
                var generated = Server.System<RtsBattlefieldSystem>().Generate(173, 80);
                battlefield = generated.Owner;
                otherMap = Server.System<SharedMapSystem>().CreateMap();
                var blue = SEntMan.SpawnEntity("RtsInfantryBlue", new EntityCoordinates(battlefield, 10.5f, 35.5f));
                var red = SEntMan.SpawnEntity("RtsInfantryRed", new EntityCoordinates(battlefield, 14.5f, 35.5f));
                var foreign = SEntMan.SpawnEntity("RtsInfantryBlue", new EntityCoordinates(otherMap, Vector2.Zero));
                var commander = SpawnCommander(battlefield);
                var plain = SEntMan.SpawnEntity(null, new EntityCoordinates(battlefield, 12.5f, 35.5f));
                var commands = Server.System<RtsCommandSystem>();
                var destination = new Vector2(10.5f, 33.5f);

                Assert.That(commands.IssueOrder(plain, RtsOrderType.Move, [blue], destination), Is.Zero,
                    "An ordinary entity must not gain commander privileges.");
                Assert.That(commands.IssueOrder(commander, RtsOrderType.Move, [red], destination), Is.Zero,
                    "A commander must not move the enemy army.");
                Assert.That(commands.IssueOrder(commander, RtsOrderType.Move, [foreign], destination), Is.Zero,
                    "Team ownership must not authorize units on a different battlefield.");
                Assert.That(commands.IssueOrder(commander, RtsOrderType.Move,
                    [generated.Comp.BlueHeadquarters], destination), Is.Zero,
                    "Headquarters are owned structures, not movable units.");
                Assert.That(commands.IssueOrder(commander, (RtsOrderType) 255, [blue], destination), Is.Zero);
                Assert.That(commands.IssueOrder(commander, RtsOrderType.Move,
                    Enumerable.Repeat(blue, 33).ToArray(), destination), Is.Zero,
                    "Oversized requests must be rejected before processing their units.");

                Vector2[] invalidTargets =
                [
                    new(float.NaN, 10), new(10, float.NaN),
                    new(float.PositiveInfinity, 10), new(10, float.NegativeInfinity),
                    new(-1, 10), new(10, -1), new(80, 10), new(10, 80),
                ];
                foreach (var target in invalidTargets)
                {
                    Assert.That(commands.IssueOrder(commander, RtsOrderType.Move, [blue], target), Is.Zero,
                        $"Invalid destination {target} must not become a pathfinding request.");
                }

                var transform = Server.System<SharedTransformSystem>();
                transform.SetCoordinates(commander, new EntityCoordinates(otherMap, Vector2.Zero));
                Assert.That(commands.IssueOrder(commander, RtsOrderType.Move, [blue], destination), Is.Zero,
                    "Moving a commander away must revoke access to the previous battlefield.");
                transform.SetCoordinates(commander, new EntityCoordinates(battlefield, 12.5f, 35.5f));
                Assert.That(SEntMan.HasComponent<NPCSteeringComponent>(blue), Is.False,
                    "Rejected requests must not start movement.");
                Assert.That(SEntMan.GetComponent<RtsUnitComponent>(blue).State, Is.EqualTo(RtsUnitState.Idle));

                Assert.That(commands.IssueOrder(commander, RtsOrderType.Move, [blue, red, blue], destination), Is.EqualTo(1),
                    "Process eligible units once while ignoring enemy units and duplicates.");
                Assert.That(SEntMan.GetComponent<RtsUnitComponent>(blue).State, Is.EqualTo(RtsUnitState.Moving));
                Assert.That(SEntMan.HasComponent<NPCSteeringComponent>(red), Is.False);
                var steering = SEntMan.GetComponent<NPCSteeringComponent>(blue);
                Assert.That(steering.Coordinates.EntityId, Is.EqualTo(battlefield));
                Assert.That(steering.Coordinates.Position, Is.EqualTo(destination));

                // Invalid replacement commands must leave an existing valid order intact.
                Assert.That(commands.IssueOrder(commander, RtsOrderType.Move, [blue], new Vector2(float.NaN)), Is.Zero);
                Assert.That(SEntMan.GetComponent<NPCSteeringComponent>(blue), Is.SameAs(steering));
                Assert.That(commands.IssueOrder(commander, RtsOrderType.Stop, [blue], Vector2.Zero), Is.EqualTo(1));
                AssertIdle(blue);
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                if (SEntMan.EntityExists(battlefield))
                    SEntMan.DeleteEntity(battlefield);
                if (SEntMan.EntityExists(otherMap))
                    SEntMan.DeleteEntity(otherMap);
            });
        }
    }

    [Test]
    public async Task NetworkOrdersUseTheSessionsAttachedCommander()
    {
        EntityUid battlefield = default;
        EntityUid commander = default;
        EntityUid unit = default;
        NetEntity netUnit = default;
        var previous = ServerSession!.AttachedEntity;
        try
        {
            await Server.WaitPost(() =>
            {
                battlefield = Server.System<RtsBattlefieldSystem>().Generate(173, 80).Owner;
                unit = SEntMan.SpawnEntity("RtsInfantryBlue", new EntityCoordinates(battlefield, 10.5f, 35.5f));
                netUnit = SEntMan.GetNetEntity(unit);
                commander = SpawnCommander(battlefield);
            });

            var destination = new Vector2(6.5f, 35.5f);
            await SendOrder(RtsOrderType.Move, netUnit, destination);
            await Server.WaitAssertion(() =>
                Assert.That(SEntMan.HasComponent<NPCSteeringComponent>(unit), Is.False,
                    "Knowing a unit's network ID does not authorize an ordinary player to command it."));

            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(ServerSession!, commander));
            await Pair.RunTicksSync(15);
            await SendOrder(RtsOrderType.Move, netUnit, destination);
            await Server.WaitAssertion(() =>
            {
                Assert.That(SEntMan.GetComponent<RtsUnitComponent>(unit).State, Is.EqualTo(RtsUnitState.Moving));
                Assert.That(SEntMan.GetComponent<NPCSteeringComponent>(unit).Coordinates.Position, Is.EqualTo(destination));
            });

            await SendOrder(RtsOrderType.Stop, netUnit, Vector2.Zero);
            await Server.WaitAssertion(() => AssertIdle(unit));
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                Server.PlayerMan.SetAttachedEntity(ServerSession!, previous);
                if (SEntMan.EntityExists(battlefield))
                    SEntMan.DeleteEntity(battlefield);
            });
            await Pair.RunTicksSync(5);
        }
    }

    private EntityUid SpawnCommander(EntityUid battlefield)
    {
        var uid = SEntMan.SpawnEntity("RtsCommander", new EntityCoordinates(battlefield, 12.5f, 35.5f));
        var commander = SEntMan.GetComponent<RtsCommanderComponent>(uid);
        commander.Battlefield = battlefield;
        commander.Team = RtsTeam.Blue;
        commander.BattlefieldSize = 80;
        return uid;
    }

    private async Task SendOrder(RtsOrderType order, NetEntity unit, Vector2 target)
    {
        await Client.WaitPost(() => CEntMan.EntityNetManager!.SendSystemNetworkMessage(
            new RtsOrderRequestEvent(order, [unit], target)));
        // Allow the server command cooldown and the network round trip to finish.
        await Pair.RunTicksSync(15);
    }

    private void AssertIdle(EntityUid uid)
    {
        Assert.That(SEntMan.GetComponent<RtsUnitComponent>(uid).State, Is.EqualTo(RtsUnitState.Idle));
        Assert.That(SEntMan.HasComponent<NPCSteeringComponent>(uid), Is.False);
        Assert.That(SEntMan.HasComponent<RtsUnitOrderComponent>(uid), Is.False);
        Assert.That(SEntMan.HasComponent<ActiveNPCComponent>(uid), Is.False);
        if (SEntMan.TryGetComponent<InputMoverComponent>(uid, out var mover))
            Assert.That(mover.CurTickSprintMovement, Is.EqualTo(Vector2.Zero));
    }
}
