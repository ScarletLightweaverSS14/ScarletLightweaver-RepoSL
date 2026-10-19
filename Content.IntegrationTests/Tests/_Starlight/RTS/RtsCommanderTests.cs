#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._Starlight.RTS;
using Content.Server._Starlight.RTS.Components;
using Content.Server.Administration.Managers;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Shared._Starlight.RTS;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests._Starlight.RTS;

[TestOf(typeof(RtsCommandSystem))]
public sealed class RtsCommanderTests : GameTest
{
    // These tests deliberately create and change player minds, so they use a fresh real-ticker pair.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        DummyTicker = false,
        Fresh = true,
        Destructive = true,
    };

    [Test]
    public async Task JoiningThroughConsoleAndLeavingRestoresTheOriginalBody()
    {
        var map = await Pair.CreateTestMap();
        Entity<RtsBattlefieldComponent> battlefield = default;
        Entity<MindComponent> mind = default;
        EntityUid body = default;
        EntityUid commander = default;
        EntityUid defaultCommander = default;
        NetEntity netCommander = default;
        try
        {
            await Server.WaitAssertion(() =>
            {
                body = SEntMan.SpawnEntity(null, map.GridCoords);
                var minds = Server.System<MindSystem>();
                mind = minds.CreateMind(ServerSession!.UserId);
                minds.TransferTo(mind, body);
                Assert.That(ServerSession.AttachedEntity, Is.EqualTo(body));

                battlefield = Server.System<RtsBattlefieldSystem>().Generate(173, 80);
                var mapId = SEntMan.GetComponent<MapComponent>(battlefield).MapId;
                Server.ResolveDependency<IAdminManager>().PromoteHost(ServerSession);
                Server.ResolveDependency<IConsoleHost>().GetSessionShell(ServerSession)
                    .ExecuteCommand($"rtsjoin {mapId} red");
                Assert.That(ServerSession.AttachedEntity, Is.Not.Null);
                commander = ServerSession.AttachedEntity!.Value;
                Assert.That(SEntMan.HasComponent<RtsCommanderComponent>(commander), Is.True,
                    "The real rtsjoin console command must attach the player to a commander.");
                var actor = SEntMan.GetComponent<RtsCommanderComponent>(commander);
                Assert.That(actor.Battlefield, Is.EqualTo(battlefield.Owner));
                Assert.That(actor.Team, Is.EqualTo(RtsTeam.Red));
                netCommander = SEntMan.GetNetEntity(commander);
                Assert.That(mind.Comp.OwnedEntity, Is.EqualTo(body));
                Assert.That(mind.Comp.VisitingEntity, Is.EqualTo(commander));
                Assert.That(ServerSession!.AttachedEntity, Is.EqualTo(commander));
            });
            await Pair.RunTicksSync(5);
            await Client.WaitAssertion(() =>
            {
                Assert.That(Client.Session!.AttachedEntity, Is.Not.Null);
                var attached = Client.Session.AttachedEntity!.Value;
                Assert.That(CEntMan.GetNetEntity(attached), Is.EqualTo(netCommander),
                    "The client must receive the command camera attachment.");
                Assert.That(CEntMan.GetComponent<RtsCommanderComponent>(attached).Team, Is.EqualTo(RtsTeam.Red));
            });
            await Server.WaitPost(() => Server.System<RtsCommandSystem>().Leave(commander));
            await Pair.RunTicksSync(5);

            await Server.WaitAssertion(() =>
            {
                Assert.That(SEntMan.EntityExists(body), Is.True);
                Assert.That(SEntMan.EntityExists(commander), Is.False);
                Assert.That(mind.Comp!.VisitingEntity, Is.Null);
                Assert.That(mind.Comp.OwnedEntity, Is.EqualTo(body));
                Assert.That(mind.Comp.CurrentEntity, Is.EqualTo(body));
                Assert.That(ServerSession!.AttachedEntity, Is.EqualTo(body),
                    "Leaving an RTS camera must return control to the body the player still owns.");

                Server.ResolveDependency<IConsoleHost>().GetSessionShell(ServerSession)
                    .ExecuteCommand("rtsjoin");
                Assert.That(ServerSession.AttachedEntity, Is.Not.Null);
                defaultCommander = ServerSession.AttachedEntity!.Value;
                Assert.That(SEntMan.HasComponent<RtsCommanderComponent>(defaultCommander), Is.True,
                    "rtsjoin without arguments should find the only RTS battlefield.");
                var actor = SEntMan.GetComponent<RtsCommanderComponent>(defaultCommander);
                Assert.That(actor.Battlefield, Is.EqualTo(battlefield.Owner));
                Assert.That(actor.Team, Is.EqualTo(RtsTeam.Blue));
                Assert.That(mind.Comp.OwnedEntity, Is.EqualTo(body));
                Assert.That(mind.Comp.VisitingEntity, Is.EqualTo(defaultCommander));
            });

            await Pair.RunTicksSync(5);
            await Server.WaitPost(() => Server.System<RtsCommandSystem>().Leave(defaultCommander));
            await Pair.RunTicksSync(5);
            await Server.WaitAssertion(() =>
            {
                Assert.That(SEntMan.EntityExists(defaultCommander), Is.False);
                Assert.That(mind.Comp!.VisitingEntity, Is.Null);
                Assert.That(ServerSession!.AttachedEntity, Is.EqualTo(body));
            });
        }
        finally
        {
            await DeleteBattlefield(battlefield.Owner);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ObserverRemainsCommanderUntilLeavingOrDeletingBattlefield(bool deleteBattlefield)
    {
        var map = await Pair.CreateTestMap();
        var returnCoordinates = map.GridCoords.Offset(new System.Numerics.Vector2(2.5f, 2.5f));
        Entity<RtsBattlefieldComponent> battlefield = default;
        Entity<MindComponent> mind = default;
        EntityUid observer = default;
        EntityUid commander = default;
        NetEntity netCommander = default;
        try
        {
            await Server.WaitAssertion(() =>
            {
                observer = SEntMan.SpawnEntity(GameTicker.ObserverPrototypeName, returnCoordinates);
                var minds = Server.System<MindSystem>();
                mind = minds.CreateMind(ServerSession!.UserId);
                minds.TransferTo(mind, observer);
                Assert.That(ServerSession.AttachedEntity, Is.EqualTo(observer));

                battlefield = Server.System<RtsBattlefieldSystem>().Generate(173, 80);
                var mapId = SEntMan.GetComponent<MapComponent>(battlefield).MapId;
                Server.ResolveDependency<IAdminManager>().PromoteHost(ServerSession);
                Server.ResolveDependency<IConsoleHost>().GetSessionShell(ServerSession)
                    .ExecuteCommand($"rtsjoin {mapId} blue");
                Assert.That(ServerSession.AttachedEntity, Is.Not.Null);
                commander = ServerSession.AttachedEntity!.Value;
                Assert.That(SEntMan.HasComponent<RtsCommanderComponent>(commander), Is.True,
                    "An observer must enter a command camera through the real console command.");
                netCommander = SEntMan.GetNetEntity(commander);
                Assert.That(mind.Comp.CurrentEntity, Is.EqualTo(commander));
                Assert.That(ServerSession!.AttachedEntity, Is.EqualTo(commander));
            });

            // Detaching an ordinary observer queues its deletion. That deletion must not
            // eject the player from their new camera when the mind lifecycle finishes.
            for (var checkpoint = 0; checkpoint < 3; checkpoint++)
            {
                await Pair.RunTicksSync(5);
                await Server.WaitAssertion(() =>
                {
                    Assert.That(SEntMan.EntityExists(commander), Is.True,
                        "The commander must survive deletion of the old observer.");
                    Assert.That(ServerSession!.AttachedEntity, Is.EqualTo(commander),
                        "Joining must keep the player in their camera until they choose to leave.");
                    Assert.That(mind.Comp!.CurrentEntity, Is.EqualTo(commander));
                    Assert.That(SEntMan.GetComponent<RtsCommanderComponent>(commander).Battlefield,
                        Is.EqualTo(battlefield.Owner));
                });
                await Client.WaitAssertion(() =>
                {
                    Assert.That(Client.Session!.AttachedEntity, Is.Not.Null);
                    var attached = Client.Session.AttachedEntity!.Value;
                    Assert.That(CEntMan.GetNetEntity(attached), Is.EqualTo(netCommander),
                        "The client must remain attached to the same camera after observer cleanup.");
                    Assert.That(CEntMan.HasComponent<RtsCommanderComponent>(attached), Is.True);
                    Assert.That(CEntMan.GetComponent<RtsCommanderComponent>(attached).Team, Is.EqualTo(RtsTeam.Blue));
                    Assert.That(CEntMan.HasComponent<GhostComponent>(attached), Is.False);
                });
            }

            var previousCommander = commander;
            await Server.WaitAssertion(() =>
            {
                var mapId = SEntMan.GetComponent<MapComponent>(battlefield).MapId;
                Server.ResolveDependency<IConsoleHost>().GetSessionShell(ServerSession!)
                    .ExecuteCommand($"rtsjoin {mapId} red");
                Assert.That(ServerSession!.AttachedEntity, Is.Not.Null);
                commander = ServerSession.AttachedEntity!.Value;
                Assert.That(commander, Is.Not.EqualTo(previousCommander));
                var actor = SEntMan.GetComponent<RtsCommanderComponent>(commander);
                Assert.That(actor.Battlefield, Is.EqualTo(battlefield.Owner));
                Assert.That(actor.Team, Is.EqualTo(RtsTeam.Red));
                netCommander = SEntMan.GetNetEntity(commander);

                var transform = Server.System<SharedTransformSystem>();
                var expected = transform.ToMapCoordinates(returnCoordinates);
                var stored = transform.ToMapCoordinates(
                    SEntMan.GetComponent<RtsCommanderSessionComponent>(commander).ReturnCoordinates);
                Assert.That(stored.MapId, Is.EqualTo(expected.MapId));
                Assert.That(System.Numerics.Vector2.Distance(stored.Position, expected.Position), Is.LessThan(0.01f),
                    "Switching teams must preserve the original observer return position.");
            });

            await Pair.RunTicksSync(5);
            await Server.WaitAssertion(() =>
            {
                Assert.That(SEntMan.EntityExists(previousCommander), Is.False,
                    "Switching teams must clean up the previous command camera.");
                Assert.That(SEntMan.EntityExists(commander), Is.True);
                Assert.That(ServerSession!.AttachedEntity, Is.EqualTo(commander),
                    "Replacing an owned command camera must not trigger ghost restoration.");
                Assert.That(mind.Comp!.CurrentEntity, Is.EqualTo(commander));
            });
            await Client.WaitAssertion(() =>
            {
                Assert.That(Client.Session!.AttachedEntity, Is.Not.Null);
                var attached = Client.Session.AttachedEntity!.Value;
                Assert.That(CEntMan.GetNetEntity(attached), Is.EqualTo(netCommander));
                Assert.That(CEntMan.GetComponent<RtsCommanderComponent>(attached).Team, Is.EqualTo(RtsTeam.Red));
                Assert.That(CEntMan.HasComponent<GhostComponent>(attached), Is.False);
            });

            await Server.WaitAssertion(() =>
            {
                Assert.That(SEntMan.EntityExists(observer), Is.False,
                    "Ordinary observer ghosts delete themselves when the player detaches.");
                if (deleteBattlefield)
                    SEntMan.DeleteEntity(battlefield.Owner);
                else
                    Server.System<RtsCommandSystem>().Leave(commander);
            });
            await Pair.RunTicksSync(5);

            await Server.WaitAssertion(() =>
            {
                Assert.That(SEntMan.EntityExists(commander), Is.False);
                Assert.That(mind.Comp!.VisitingEntity, Is.Null);
                Assert.That(ServerSession!.AttachedEntity, Is.Not.Null,
                    "Losing the camera must restore an observer instead of leaving the player unattached.");
                var restored = ServerSession.AttachedEntity!.Value;
                Assert.That(restored, Is.Not.EqualTo(observer));
                Assert.That(SEntMan.HasComponent<GhostComponent>(restored), Is.True);
                Assert.That(mind.Comp.CurrentEntity, Is.EqualTo(restored));
                Assert.That(mind.Comp.OwnedEntity, Is.EqualTo(restored));
                var transform = Server.System<SharedTransformSystem>();
                var expected = transform.ToMapCoordinates(returnCoordinates);
                var actual = transform.ToMapCoordinates(SEntMan.GetComponent<TransformComponent>(restored).Coordinates);
                Assert.That(actual.MapId, Is.EqualTo(expected.MapId));
                Assert.That(System.Numerics.Vector2.Distance(actual.Position, expected.Position), Is.LessThan(0.01f),
                    "The restored observer should return to the location used before entering RTS mode.");
            });
        }
        finally
        {
            await DeleteBattlefield(battlefield.Owner);
        }
    }

    private async Task DeleteBattlefield(EntityUid battlefield)
    {
        await Server.WaitPost(() =>
        {
            if (SEntMan.EntityExists(battlefield))
                SEntMan.DeleteEntity(battlefield);
        });
    }
}
