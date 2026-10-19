#nullable enable
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using Content.Client._Starlight.RTS;
using Content.Client.Interaction;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Starlight.RTS;
using Content.Server.NPC.Components;
using Content.Shared._Starlight.RTS;
using Content.Shared._Starlight.CCVar;
using Content.Shared.Input;
using Content.Shared.Interaction;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Starlight.RTS;

[TestOf(typeof(RtsCommanderSystem))]
public sealed class RtsSelectionTests : GameTest
{
    [SetUp]
    public async Task ConfigureSimulation()
    {
        await OverrideCVar(Side.Server, CVars.NetTickrate, 30);
        await OverrideCVar(Side.Server, StarlightCCVars.DisablePathfinding, false);
    }

    [Test]
    public async Task CommanderPointerInputSelectsFriendlyUnitsAndOrdersTheirMovement()
    {
        EntityUid battlefield = default;
        EntityUid ordinary = default;
        EntityUid camera = default;
        EntityUid firstUnit = default;
        EntityUid secondUnit = default;
        NetEntity netBattlefield = default;
        NetEntity first = default;
        NetEntity second = default;
        NetEntity enemy = default;
        NetEntity headquarters = default;
        var previous = ServerSession!.AttachedEntity;
        var center = Vector2.Zero;
        var ordinaryInput = false;
        var cameraMoveHeld = false;
        var cameraStart = Vector2.Zero;
        var stage = "spawn and replicate commander";

        try
        {
            await Server.WaitPost(() =>
            {
                var generated = Server.System<RtsBattlefieldSystem>().Generate(173, 80);
                battlefield = generated.Owner;
                netBattlefield = SEntMan.GetNetEntity(battlefield);
                headquarters = SEntMan.GetNetEntity(generated.Comp.BlueHeadquarters);
                center = SEntMan.GetComponent<TransformComponent>(generated.Comp.BlueHeadquarters).LocalPosition;
                firstUnit = SEntMan.SpawnEntity("RtsInfantryBlue", new EntityCoordinates(battlefield, center + new Vector2(-4, -2)));
                secondUnit = SEntMan.SpawnEntity("RtsInfantryBlue", new EntityCoordinates(battlefield, center + new Vector2(-2, -2)));
                first = SEntMan.GetNetEntity(firstUnit);
                second = SEntMan.GetNetEntity(secondUnit);
                enemy = SEntMan.GetNetEntity(SEntMan.SpawnEntity("RtsInfantryRed",
                    new EntityCoordinates(battlefield, center + new Vector2(-3, 0))));
                ordinary = SEntMan.SpawnEntity(null, new EntityCoordinates(battlefield, center + new Vector2(-2, 2)));

                // Exercise the real camera prototype, input context, and networked commander component.
                cameraStart = center + new Vector2(-2, 2);
                camera = SEntMan.SpawnEntity("RtsCommander", new EntityCoordinates(battlefield, cameraStart));
                var commander = SEntMan.GetComponent<RtsCommanderComponent>(camera);
                commander.Battlefield = battlefield;
                commander.BattlefieldSize = 80;
                commander.Team = RtsTeam.Blue;
                SEntMan.Dirty(camera, commander);
                Server.PlayerMan.SetAttachedEntity(ServerSession!, camera);
            });
            await Pair.RunTicksSync(20);

            await Client.WaitAssertion(() =>
            {
                stage = "activate commander HUD";
                var system = Client.System<RtsCommanderSystem>();
                var ui = Client.ResolveDependency<IUserInterfaceManager>();
                var input = Client.ResolveDependency<IInputManager>();
                // The headless test loop ticks simulation but does not render frames.
                system.FrameUpdate(1f);
                Assert.That(system.Active, Is.True);
                Assert.That(input.Contexts.ActiveContext, Is.SameAs(input.Contexts.GetContext("rts")));
                var panel = Descendants(ui.RootControl).OfType<RtsCommandPanel>().Single();
                Assert.That(panel.Stop.Disabled, Is.True);

                var map = CEntMan.GetEntity(netBattlefield);
                var firstUid = CEntMan.GetEntity(first);
                var secondUid = CEntMan.GetEntity(second);
                var firstPosition = center + new Vector2(-4, -2);
                var secondPosition = center + new Vector2(-2, -2);

                stage = "click and Shift selection";
                Click(EngineKeyFunctions.Use, new EntityCoordinates(map, firstPosition), firstUid);
                Assert.That(system.Selected, Is.EquivalentTo(new[] { firstUid }));
                Assert.That(panel.Stop.Disabled, Is.False);

                // Standard SS14 Shift + left click dispatches ExamineEntity rather than Use.
                Click(ContentKeyFunctions.ExamineEntity, new EntityCoordinates(map, secondPosition), secondUid);
                Assert.That(system.Selected, Is.EquivalentTo(new[] { firstUid, secondUid }));
                Click(ContentKeyFunctions.ExamineEntity, new EntityCoordinates(map, firstPosition), firstUid);
                Assert.That(system.Selected, Is.EquivalentTo(new[] { secondUid }), "Shift click toggles a selected unit.");

                Click(EngineKeyFunctions.Use, new EntityCoordinates(map, center + new Vector2(-3, 0)), CEntMan.GetEntity(enemy));
                Assert.That(system.Selected, Is.Empty, "Enemy units must not be selected.");
                Click(EngineKeyFunctions.Use, new EntityCoordinates(map, center), CEntMan.GetEntity(headquarters));
                Assert.That(system.Selected, Is.Empty, "Owned headquarters must not be treated as movable units.");

                var start = new EntityCoordinates(map, center + new Vector2(-5, -3));
                var end = new EntityCoordinates(map, center + new Vector2(-1, 1));
                stage = "box selection";
                Dispatch(EngineKeyFunctions.Use, BoundKeyState.Down, start, EntityUid.Invalid, new Vector2(100, 100));
                Dispatch(EngineKeyFunctions.Use, BoundKeyState.Up, end, EntityUid.Invalid, new Vector2(220, 220));
                Assert.That(system.Selected, Is.EquivalentTo(new[] { firstUid, secondUid }),
                    "Box selection must include friendly units and omit enemies inside the same rectangle.");

                var target = new EntityCoordinates(map, center + new Vector2(-3, 3));
                stage = "right click movement request";
                Assert.That(Dispatch(EngineKeyFunctions.UseSecondary, BoundKeyState.Down, target, EntityUid.Invalid, Vector2.Zero), Is.True);
                Dispatch(EngineKeyFunctions.UseSecondary, BoundKeyState.Up, target, EntityUid.Invalid, Vector2.Zero);
                Assert.That(system.OrderMarker, Is.Not.Null);

                // An observer after RTS input provides evidence that normal input remains reachable after detaching.
                CommandBinds.Builder.Bind(new CommandBind(EngineKeyFunctions.Use,
                    InputCmdHandler.FromDelegate(_ => ordinaryInput = true),
                    before: [typeof(DragDropSystem), typeof(SharedInteractionSystem)],
                    after: [typeof(RtsCommanderSystem)])).Register<RtsSelectionTests>();
                Click(EngineKeyFunctions.Use, new EntityCoordinates(map, firstPosition), firstUid);
                Assert.That(ordinaryInput, Is.False, "Commander input must be consumed before ordinary interaction handlers.");
            });

            await Pair.RunTicksSync(15);
            stage = "server movement acknowledgement";
            await Server.WaitAssertion(() =>
            {
                Assert.That(SEntMan.GetComponent<RtsUnitComponent>(firstUnit).State, Is.EqualTo(RtsUnitState.Moving));
                Assert.That(SEntMan.GetComponent<RtsUnitComponent>(secondUnit).State, Is.EqualTo(RtsUnitState.Moving));
                Assert.That(SEntMan.HasComponent<NPCSteeringComponent>(firstUnit), Is.True);
                Assert.That(SEntMan.HasComponent<NPCSteeringComponent>(secondUnit), Is.True);
            });

            stage = "WASD commander camera movement";
            await Client.WaitPost(() =>
            {
                Dispatch(EngineKeyFunctions.MoveRight, BoundKeyState.Down,
                    new EntityCoordinates(CEntMan.GetEntity(netBattlefield), center), EntityUid.Invalid, Vector2.Zero);
                cameraMoveHeld = true;
            });
            await Pair.RunSeconds(0.25f);
            await Client.WaitPost(() =>
            {
                Dispatch(EngineKeyFunctions.MoveRight, BoundKeyState.Up,
                    new EntityCoordinates(CEntMan.GetEntity(netBattlefield), center), EntityUid.Invalid, Vector2.Zero);
                cameraMoveHeld = false;
            });
            await Pair.RunTicksSync(3);
            await Server.WaitAssertion(() =>
            {
                var transform = SEntMan.GetComponent<TransformComponent>(camera);
                Assert.That(transform.LocalPosition.X, Is.GreaterThan(cameraStart.X + 0.1f),
                    "The commander camera must respond to the ordinary rightward movement input.");
                Assert.That(transform.MapUid, Is.EqualTo(battlefield));
            });
            await Client.WaitAssertion(() =>
            {
                // Rendering normally advances the eye; do that explicitly in the headless test.
                Client.System<EyeSystem>().FrameUpdate(0f);
                var eye = Client.ResolveDependency<IEyeManager>().CurrentEye;
                var localCamera = Client.Session!.AttachedEntity!.Value;
                Assert.That(eye, Is.SameAs(CEntMan.GetComponent<EyeComponent>(localCamera).Eye));
                Assert.That(eye.Position.Position.X, Is.GreaterThan(cameraStart.X + 0.1f),
                    "The active eye must follow the moving commander rather than the previous body.");
                Assert.That(eye.Position.MapId, Is.EqualTo(CEntMan.GetComponent<TransformComponent>(localCamera).MapID));
            });

            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(ServerSession!, ordinary));
            await Pair.RunTicksSync(10);
            await Client.WaitAssertion(() =>
            {
                stage = "restore ordinary input and remove HUD";
                var system = Client.System<RtsCommanderSystem>();
                system.FrameUpdate(1f);
                Assert.That(system.Active, Is.False);
                Assert.That(system.Selected, Is.Empty);
                Assert.That(Descendants(Client.ResolveDependency<IUserInterfaceManager>().RootControl).OfType<RtsCommandPanel>(), Is.Empty);
                var map = CEntMan.GetEntity(netBattlefield);
                Click(EngineKeyFunctions.Use, new EntityCoordinates(map, center), EntityUid.Invalid);
                Assert.That(ordinaryInput, Is.True, "Normal input handlers must become reachable after leaving the commander.");
            });
        }
        catch (Exception exception)
        {
            // The NUnit/MTP bridge can otherwise replace the original exception with the pool's teardown warning.
            TestContext.Out.WriteLine($"RTS pointer test failed during {stage}: {exception}");
            throw;
        }
        finally
        {
            await Client.WaitPost(() =>
            {
                if (cameraMoveHeld)
                {
                    Dispatch(EngineKeyFunctions.MoveRight, BoundKeyState.Up,
                        new EntityCoordinates(CEntMan.GetEntity(netBattlefield), center), EntityUid.Invalid, Vector2.Zero);
                }
                CommandBinds.Unregister<RtsSelectionTests>();
            });
            await Server.WaitPost(() =>
            {
                Server.PlayerMan.SetAttachedEntity(ServerSession!, previous);
                if (SEntMan.EntityExists(battlefield))
                    SEntMan.DeleteEntity(battlefield);
            });
            await Pair.RunTicksSync(5);
            await Client.WaitPost(() => Client.System<RtsCommanderSystem>().FrameUpdate(1f));
        }
    }

    private void Click(BoundKeyFunction function, EntityCoordinates coordinates, EntityUid entity)
    {
        Dispatch(function, BoundKeyState.Down, coordinates, entity, new Vector2(100, 100));
        Dispatch(function, BoundKeyState.Up, coordinates, entity, new Vector2(100, 100));
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (var child in parent.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private bool Dispatch(BoundKeyFunction function, BoundKeyState state, EntityCoordinates coordinates, EntityUid entity, Vector2 screen)
    {
        var input = Client.ResolveDependency<IInputManager>();
        var message = new ClientFullInputCmdMessage(CGameTiming.CurTick, CGameTiming.TickFraction,
            input.NetworkBindMap.KeyFunctionID(function))
        {
            State = state,
            Coordinates = coordinates,
            Uid = entity,
            ScreenCoordinates = new ScreenCoordinates(screen, default),
        };
        return Client.System<InputSystem>().HandleInputCommand(Client.Session, function, message);
    }
}
