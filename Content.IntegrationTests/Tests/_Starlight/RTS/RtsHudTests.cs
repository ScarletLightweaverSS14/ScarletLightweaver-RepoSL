#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._Starlight.RTS;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Client.UserInterface.Systems.Viewport;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._Starlight.RTS;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Starlight.RTS;

[TestOf(typeof(RtsCommandPanel))]
public sealed class RtsHudTests : GameTest
{
    [Test]
    public async Task CommanderHudStaysAboveGameplayAndFitsScaledViewports()
    {
        EntityUid map = default;
        NetEntity idleUnit = default;
        var previous = ServerSession!.AttachedEntity;
        var originalDefault = false;
        var originalSeparated = false;
        IViewportControl? originalViewport = null;
        var originalUiScale = 0f;
        var originalAutoScale = false;
        RtsCommandPanel? originalPanel = null;
        var stage = "replicate commander";
        try
        {
            await Client.WaitPost(() =>
            {
                var ui = Client.ResolveDependency<IUserInterfaceManager>();
                var cfg = Client.ResolveDependency<IConfigurationManager>();
                originalUiScale = cfg.GetCVar(CVars.DisplayUIScale);
                originalAutoScale = cfg.GetCVar(CVars.ResAutoScaleEnabled);
                cfg.SetCVar(CVars.ResAutoScaleEnabled, false);
                originalDefault = ui.ActiveScreen is DefaultGameScreen;
                originalSeparated = ui.ActiveScreen is SeparatedChatGameScreen;
                originalViewport = Client.ResolveDependency<IEyeManager>().MainViewport;
            });
            await Server.WaitPost(() =>
            {
                map = Server.System<SharedMapSystem>().CreateMap();
                var camera = SEntMan.SpawnEntity("RtsCommander", new EntityCoordinates(map, Vector2.Zero));
                var commander = SEntMan.GetComponent<RtsCommanderComponent>(camera);
                commander.Battlefield = map;
                commander.BattlefieldSize = 80;
                commander.Team = RtsTeam.Blue;
                SEntMan.Dirty(camera, commander);
                for (var index = 0; index < 3; index++)
                {
                    // ClydeHeadless.LocalToWorld returns the origin rather than projecting
                    // a rendered scene. Keep the squad at that point to exercise the real
                    // button handler and idle-state filter without replacing the eye API.
                    var unit = SEntMan.SpawnEntity("RtsInfantryBlue", new EntityCoordinates(map, Vector2.Zero));
                    var marker = SEntMan.GetComponent<RtsUnitComponent>(unit);
                    marker.State = (RtsUnitState) index;
                    SEntMan.Dirty(unit, marker);
                    if (index == 0)
                        idleUnit = SEntMan.GetNetEntity(unit);
                }
                Server.PlayerMan.SetAttachedEntity(ServerSession!, camera);
            });
            await Pair.RunTicksSync(10);

            foreach (var separated in new[] { false, true })
            {
                await Client.WaitPost(() =>
                {
                    var ui = Client.ResolveDependency<IUserInterfaceManager>();
                    if (ui.ActiveScreen != null)
                        ui.GetUIController<GameplayStateLoadController>().UnloadScreen();
                    ui.UnloadScreen();
                    if (separated)
                        ui.LoadScreen<SeparatedChatGameScreen>();
                    else
                        ui.LoadScreen<DefaultGameScreen>();
                    ui.GetUIController<GameplayStateLoadController>().LoadScreen();
                });

                foreach (var (width, height, scale) in new[] { (1280, 720, 1f), (1024, 600, 1.5f) })
                {
                    stage = $"{(separated ? "separated" : "default")} HUD at {width}x{height}, scale {scale}";
                    await Client.WaitPost(() => Client.ResolveDependency<IConfigurationManager>()
                        .SetCVar(CVars.DisplayUIScale, scale));
                    RtsCommandPanel panel = null!;
                    await Client.WaitAssertion(() =>
                    {
                        var ui = Client.ResolveDependency<IUserInterfaceManager>();
                        var system = Client.System<RtsCommanderSystem>();
                        Client.System<EyeSystem>().FrameUpdate(0f);
                        Layout(ui, system, width, height);
                        var viewport = ui.ActiveScreen!.FindControl<LayoutContainer>("ViewportContainer");
                        panel = viewport.Children.OfType<RtsCommandPanel>().Single();
                        originalPanel ??= panel;
                        Assert.That(panel, Is.SameAs(originalPanel), "Screen changes must reparent the existing HUD.");
                        Assert.That(panel.Parent, Is.SameAs(viewport));
                        Assert.That(panel.GetPositionInParent(), Is.EqualTo(viewport.ChildCount - 1),
                            "The HUD must draw after the gameplay viewport and its other controls.");
                        Assert.That(panel.VisibleInTree, Is.True);
                        Assert.That(panel.SelectIdle.Disabled, Is.False, "The active viewport must contain the test squad.");
                        AssertBounds(ui, viewport, panel);
                        AssertButtonsReachable(ui, panel);
                    });

                    // Exercise the real local button handler instead of invoking the controller directly.
                    await ClickButton(panel.SelectIdle);
                    await Client.WaitAssertion(() =>
                    {
                        Assert.That(Client.System<RtsCommanderSystem>().Selected,
                            Is.EquivalentTo(new[] { CEntMan.GetEntity(idleUnit) }),
                            "Select idle must omit visible moving and blocked units.");
                    });

                    var richTextBefore = 0;
                    await Client.WaitPost(() =>
                        richTextBefore = Descendants(panel).OfType<RichTextLabel>().Count(label => label.Visible));
                    await ClickButton(panel.Help);
                    await Client.WaitAssertion(() =>
                    {
                        var ui = Client.ResolveDependency<IUserInterfaceManager>();
                        Layout(ui, Client.System<RtsCommanderSystem>(), width, height);
                        var viewport = ui.ActiveScreen!.FindControl<LayoutContainer>("ViewportContainer");
                        Assert.That(panel.Help.Pressed, Is.True);
                        Assert.That(Descendants(panel).OfType<RichTextLabel>().Count(label => label.Visible),
                            Is.EqualTo(richTextBefore + 1));
                        AssertBounds(ui, viewport, panel);
                        AssertButtonsReachable(ui, panel);
                        var scroll = panel.Children.OfType<ScrollContainer>().Single();
                        Assert.That(scroll.HScrollEnabled, Is.False, "Text should wrap inside the narrow viewport.");
                    });
                    await ClickButton(panel.Help);
                    await Client.WaitPost(() => panel.Children.OfType<ScrollContainer>().Single().VScroll = 0);
                }
            }
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"RTS HUD test failed during {stage}: {exception}");
            throw;
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                Server.PlayerMan.SetAttachedEntity(ServerSession!, previous);
                if (SEntMan.EntityExists(map))
                    SEntMan.DeleteEntity(map);
            });
            await Pair.RunTicksSync(5);
            await Client.WaitPost(() =>
            {
                var ui = Client.ResolveDependency<IUserInterfaceManager>();
                Client.System<RtsCommanderSystem>().FrameUpdate(0f);
                if (ui.ActiveScreen != null)
                    ui.GetUIController<GameplayStateLoadController>().UnloadScreen();
                ui.UnloadScreen();
                if (originalDefault)
                    ui.LoadScreen<DefaultGameScreen>();
                else if (originalSeparated)
                    ui.LoadScreen<SeparatedChatGameScreen>();
                if (ui.ActiveScreen != null)
                    ui.GetUIController<GameplayStateLoadController>().LoadScreen();
                if (originalViewport != null)
                    Client.ResolveDependency<IEyeManager>().MainViewport = originalViewport;
                var cfg = Client.ResolveDependency<IConfigurationManager>();
                cfg.SetCVar(CVars.DisplayUIScale, originalUiScale);
                cfg.SetCVar(CVars.ResAutoScaleEnabled, originalAutoScale);
                var logicalSize = new Vector2(1280, 720) / ui.RootControl.UIScale;
                ui.RootControl.Measure(logicalSize);
                ui.RootControl.Arrange(UIBox2.FromDimensions(Vector2.Zero, logicalSize));
            });
        }
    }

    private static void Layout(IUserInterfaceManager ui, RtsCommanderSystem system, int width, int height)
    {
        // ClydeHeadless has a fixed render target. Explicit root layout supplies the requested
        // window bounds while retaining the real scale, styles, screens, and control layout code.
        var size = new Vector2(width, height) / ui.RootControl.UIScale;
        for (var pass = 0; pass < 3; pass++)
        {
            InvalidateLayout(ui.RootControl);
            ui.RootControl.Measure(size);
            ui.RootControl.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
            // The headless loop does not render the UI-controller frame that assigns the
            // camera eye to a newly loaded gameplay viewport.
            ui.GetUIController<ViewportUIController>().FrameUpdate(new FrameEventArgs(0f));
            system.FrameUpdate(0.2f);
        }
        InvalidateLayout(ui.RootControl);
        ui.RootControl.Measure(size);
        ui.RootControl.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
    }

    private static void InvalidateLayout(Control control)
    {
        // Invalidations normally run through the render-frame layout queue. This test
        // supplies explicit window bounds, so flush each control's cached layout here.
        control.InvalidateMeasure();
        control.InvalidateArrange();
        foreach (var child in control.Children)
            InvalidateLayout(child);
    }

    private static void AssertBounds(IUserInterfaceManager ui, Control viewport, RtsCommandPanel panel)
    {
        AssertInside(panel.GlobalPixelRect, viewport.GlobalPixelRect, "The panel must fit within the gameplay viewport.");
        AssertInside(panel.GlobalPixelRect, ui.RootControl.GlobalPixelRect, "The panel must remain on screen.");
        Assert.That(panel.PixelWidth, Is.GreaterThan(0));
        Assert.That(panel.PixelHeight, Is.GreaterThan(0));
        foreach (var control in Descendants(panel).Where(control => control.VisibleInTree && control.ChildCount == 0))
        {
            // Vertical overflow is intentional inside the panel's native scroll container.
            Assert.That(control.GlobalPixelRect.Left, Is.GreaterThanOrEqualTo(panel.GlobalPixelRect.Left - 1));
            Assert.That(control.GlobalPixelRect.Right, Is.LessThanOrEqualTo(panel.GlobalPixelRect.Right + 1),
                $"{control.GetType().Name} must not overflow horizontally.");
        }
    }

    private static void AssertButtonsReachable(IUserInterfaceManager ui, RtsCommandPanel panel)
    {
        var scroll = panel.Children.OfType<ScrollContainer>().Single();
        foreach (var button in new[] { panel.Stop, panel.SelectVisible, panel.SelectIdle, panel.Clear, panel.Help, panel.Exit })
        {
            if (button.GlobalRect.Bottom > scroll.GlobalRect.Bottom)
                scroll.VScroll += button.GlobalRect.Bottom - scroll.GlobalRect.Bottom + 1;
            if (button.GlobalRect.Top < scroll.GlobalRect.Top)
                scroll.VScroll -= scroll.GlobalRect.Top - button.GlobalRect.Top + 1;
            scroll.InvalidateArrange();
            scroll.Arrange(scroll.Rect);
            AssertInside(button.GlobalPixelRect, scroll.GlobalPixelRect, $"{button.Text} must be reachable inside the panel.");
            var point = button.GlobalPixelPosition + (Vector2) button.PixelSize / 2;
            var hit = ui.MouseGetControl(new ScreenCoordinates(point, button.Window!.Id));
            Assert.That(hit == button || hit != null && Ancestors(hit).Contains(button), Is.True,
                $"{button.Text} must receive the pointer; hit {hit} at {hit?.GlobalPixelRect}. " +
                $"Button {button.GlobalPixelRect}, panel {panel.GlobalPixelRect}, scale {button.UIScale}, " +
                $"chat {(ui.ActiveScreen as InGameScreen)?.ChatBox.GlobalPixelRect}, root {ui.RootControl.GlobalPixelRect}, " +
                $"set/max/desired {panel.SetWidth}/{panel.MaxWidth}/{panel.DesiredSize}, " +
                $"chat visible {(ui.ActiveScreen as InGameScreen)?.ChatBox.VisibleInTree}.");
        }
    }

    private async Task ClickButton(Button button)
    {
        var point = button.GlobalPixelPosition + (Vector2) button.PixelSize / 2;
        foreach (var state in new[] { BoundKeyState.Down, BoundKeyState.Up })
        {
            await Client.DoGuiEvent(button, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state,
                new ScreenCoordinates(point, button.Window!.Id), true, button.Size / 2, (Vector2) button.PixelSize / 2));
        }
    }

    private static void AssertInside(UIBox2i inner, UIBox2i outer, string message)
    {
        Assert.That(inner.Left, Is.GreaterThanOrEqualTo(outer.Left - 1), message);
        Assert.That(inner.Top, Is.GreaterThanOrEqualTo(outer.Top - 1), message);
        Assert.That(inner.Right, Is.LessThanOrEqualTo(outer.Right + 1), message);
        Assert.That(inner.Bottom, Is.LessThanOrEqualTo(outer.Bottom + 1), message);
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

    private static IEnumerable<Control> Ancestors(Control control)
    {
        for (var parent = control.Parent; parent != null; parent = parent.Parent)
            yield return parent;
    }
}
