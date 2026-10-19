#nullable enable
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

[TestOf(typeof(RtsUnitSystem))]
public sealed class RtsUnitMovementTests : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [SetUp]
    public async Task ConfigureSimulation()
    {
        await OverrideCVar(Side.Server, CVars.NetTickrate, 30);
        await OverrideCVar(Side.Server, StarlightCCVars.DisablePathfinding, false);
    }

    [Test]
    public async Task InfantryPathsAroundHeadquartersArrivesAndCanBeStopped()
    {
        EntityUid battlefield = default;
        EntityUid unit = default;
        Vector2 headquarters = default;
        Vector2 destination = default;
        var sawPath = false;
        var sawDetour = false;
        var arrived = false;
        try
        {
            await Server.WaitPost(() =>
            {
                var generated = Server.System<RtsBattlefieldSystem>().Generate(173, 80);
                battlefield = generated.Owner;
                headquarters = SEntMan.GetComponent<TransformComponent>(generated.Comp.BlueHeadquarters).LocalPosition;
                // A new unit starts to the west of the headquarters and must reach its east side.
                // The existing static headquarters blocks the complete direct line between them.
                unit = SEntMan.SpawnEntity("RtsInfantryBlue",
                    new EntityCoordinates(battlefield, headquarters - new Vector2(4, 0)));
                destination = headquarters + new Vector2(4, 0);
            });

            await Pair.RunSeconds(1);
            await Server.WaitAssertion(() =>
                Assert.That(Server.System<RtsUnitSystem>().IssueMove(unit,
                    new EntityCoordinates(battlefield, destination)), Is.True));

            // Exercise native pathfinding, context steering, movement input, and physics together.
            // Stop early on arrival; allow extra time for asynchronous navmesh/path jobs on slow runners.
            for (var step = 0; step < 80 && !arrived; step++)
            {
                await Pair.RunSeconds(0.25f);
                await Server.WaitAssertion(() =>
                {
                    var position = SEntMan.GetComponent<TransformComponent>(unit).LocalPosition;
                    var relative = position - headquarters;
                    sawDetour |= MathF.Abs(relative.Y) > 1.1f;
                    Assert.That(MathF.Abs(relative.X) < 0.9f && MathF.Abs(relative.Y) < 0.9f, Is.False,
                        "The unit must go around the headquarters' collidable footprint.");

                    if (SEntMan.TryGetComponent<NPCSteeringComponent>(unit, out var steering))
                        sawPath |= steering.CurrentPath.Count > 0;

                    var state = SEntMan.GetComponent<RtsUnitComponent>(unit).State;
                    Assert.That(state, Is.Not.EqualTo(RtsUnitState.Blocked),
                        $"An accessible route around headquarters should be found; last position {position}.");
                    arrived = state == RtsUnitState.Idle;
                });
            }

            await Server.WaitAssertion(() =>
            {
                var position = SEntMan.GetComponent<TransformComponent>(unit).LocalPosition;
                Assert.That(arrived, Is.True, $"Unit did not complete its move; last position {position}.");
                Assert.That(sawPath, Is.True, "The move should use the existing NPC pathfinder.");
                Assert.That(sawDetour, Is.True, "The direct path must be obstructed enough to require a detour.");
                Assert.That(Vector2.Distance(position, destination), Is.LessThanOrEqualTo(0.5f));
                AssertIdle(unit);
            });

            // Idle optimization removes the mover after arrival. A later order must restore it.
            await Pair.RunSeconds(1);
            await Server.WaitAssertion(() =>
            {
                AssertIdle(unit);
                Assert.That(Server.System<RtsUnitSystem>().IssueMove(unit,
                    new EntityCoordinates(battlefield, destination + new Vector2(0, 4))), Is.True);
            });

            await Pair.RunSeconds(0.5f);
            Vector2 stoppedAt = default;
            await Server.WaitAssertion(() =>
            {
                stoppedAt = SEntMan.GetComponent<TransformComponent>(unit).LocalPosition;
                Assert.That(Vector2.Distance(stoppedAt, destination), Is.GreaterThan(0.25f),
                    "The replacement order should begin moving before cancellation.");
                Server.System<RtsUnitSystem>().Stop(unit);
                AssertIdle(unit);
            });

            await Pair.RunSeconds(1);
            await Server.WaitAssertion(() =>
            {
                AssertIdle(unit);
                var position = SEntMan.GetComponent<TransformComponent>(unit).LocalPosition;
                Assert.That(Vector2.Distance(position, stoppedAt), Is.LessThanOrEqualTo(0.5f),
                    "Stop must clear the movement input instead of continuing to the old destination.");

                Assert.That(Server.System<RtsUnitSystem>().IssueMove(unit,
                    new EntityCoordinates(battlefield, destination + new Vector2(0, -3))), Is.True,
                    "A unit must accept a later move after a stop has removed its mover.");
            });

            await Pair.RunSeconds(0.5f);
            Vector2 restartedAt = default;
            await Server.WaitAssertion(() =>
            {
                restartedAt = SEntMan.GetComponent<TransformComponent>(unit).LocalPosition;
                Assert.That(Vector2.Distance(restartedAt, stoppedAt), Is.GreaterThan(0.25f));
                var units = Server.System<RtsUnitSystem>();
                units.Stop(unit);
                Assert.That(units.IssueMove(unit,
                    new EntityCoordinates(battlefield, destination + new Vector2(0, -4))), Is.True,
                    "An immediate move after stop must replace a mover pending deferred removal.");
                Assert.That(SEntMan.GetComponent<RtsUnitComponent>(unit).State, Is.EqualTo(RtsUnitState.Moving));
            });

            await Pair.RunSeconds(0.5f);
            await Server.WaitAssertion(() =>
            {
                var position = SEntMan.GetComponent<TransformComponent>(unit).LocalPosition;
                Assert.That(Vector2.Distance(position, restartedAt), Is.GreaterThan(0.25f),
                    "An immediate replacement order must keep its movement components after ticking.");
                Assert.That(SEntMan.GetComponent<RtsUnitComponent>(unit).State, Is.Not.EqualTo(RtsUnitState.Blocked));
                Server.System<RtsUnitSystem>().Stop(unit);
                AssertIdle(unit);
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                if (SEntMan.EntityExists(battlefield))
                    SEntMan.DeleteEntity(battlefield);
                Assert.That(SEntMan.EntityExists(unit), Is.False,
                    "Battlefield deletion must clean up units and outstanding steering work.");
            });
        }
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
