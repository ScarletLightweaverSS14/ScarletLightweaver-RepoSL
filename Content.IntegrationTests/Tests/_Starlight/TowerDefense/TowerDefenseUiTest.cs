using Content.Client._Starlight.TowerDefense;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._Starlight.TowerDefense;
using Content.Shared._Starlight.TowerDefense;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Starlight.TowerDefense;

public sealed class TowerDefenseUiTest : InteractionTest
{
    [Test]
    public async Task SetupPurchasesUpgradesAndCanReopenAfterClosing()
    {
        EntityUid arenaMap = default;
        await Server.WaitPost(() =>
        {
            var console = Server.System<TowerDefenseSystem>().CreateArena(SPlayer, out var mapId);
            Target = SEntMan.GetNetEntity(console);
            arenaMap = MapSystem.GetMapOrInvalid(mapId);
        });

        try
        {
            // Exercise the actual connected BUI lifecycle, including startup's repeated Open calls.
            await RunTicks(15);
            Assert.That(TryGetBui(TowerDefenseUiKey.Key, out var first), Is.True);
            Assert.That(first, Is.TypeOf<TowerDefenseBoundUserInterface>());
            Assert.That(first.IsOpened, Is.True);
            var window = ((TowerDefenseBoundUserInterface)first).Window!;
            Button Button(string name) => GetControlFromChildren<Button>(control => control.Name == name, window);
            async Task Press(string name)
            {
                await ClickControl(Button(name));
                await RunTicks(15);
            }

            Assert.That(Button("TowerTile_0_7").Disabled, Is.True, "The entrance road cannot accept a tower.");
            await Press("TowerShop_TowerDefenseTider");
            await Press("TowerTile_5_5");
            await Server.WaitAssertion(() =>
            {
                var arena = SEntMan.GetComponent<TowerDefenseArenaComponent>(STarget!.Value);
                Assert.That(arena.Towers.Count, Is.EqualTo(1));
                Assert.That(arena.Cash, Is.EqualTo(350));
            });

            await Press("TowerTile_5_5");
            Assert.That(Button("TowerUpgrade").Disabled, Is.False);
            await Press("TowerUpgrade");
            await Press("TowerUpgrade");
            Assert.That(Button("TowerUpgrade").Disabled, Is.True, "The maximum tier is shown as unavailable.");
            Assert.That(Button("TowerTile_5_5").Text, Is.EqualTo("T3"));
            await Press("TowerShop_TowerDefenseSecurity");
            await Press("TowerTile_8_5");
            await Server.WaitAssertion(() =>
            {
                var arena = SEntMan.GetComponent<TowerDefenseArenaComponent>(STarget!.Value);
                Assert.That(arena.Cash, Is.EqualTo(50));
                Assert.That(arena.Towers.Count, Is.EqualTo(2));
                var tider = SEntMan.GetComponent<TowerDefenseTowerComponent>(arena.Towers[new Vector2i(5, 5)]);
                var security = SEntMan.GetComponent<TowerDefenseTowerComponent>(arena.Towers[new Vector2i(8, 5)]);
                Assert.That(tider.Level, Is.EqualTo(2));
                Assert.That(security.Definition.Id, Is.EqualTo("TowerDefenseSecurity"));
            });

            // Server rejection travels back through the real BUI and appears in the window.
            await SendBui(TowerDefenseUiKey.Key,
                new TowerDefenseMessage(TowerDefenseAction.Place, new(0, 7), "TowerDefenseTider"));
            var feedback = GetControlFromChildren<Label>(control => control.Name == "TowerFeedback", window);
            Assert.That(feedback.Visible, Is.True);
            Assert.That(feedback.Text, Is.Not.Null.And.Not.Empty);

            await Press("TowerStart");
            Assert.That(Button("TowerStart").Disabled, Is.True);
            await CloseBui(TowerDefenseUiKey.Key);
            Assert.That(IsUiOpen(TowerDefenseUiKey.Key), Is.False);

            await Server.WaitPost(() => SUiSys.TryOpenUi(STarget!.Value, TowerDefenseUiKey.Key, SPlayer));
            await RunTicks(15);
            Assert.That(TryGetBui(TowerDefenseUiKey.Key, out var reopened), Is.True);
            Assert.That(reopened.IsOpened, Is.True);
            Assert.That(reopened, Is.Not.SameAs(first));
            var reopenedWindow = ((TowerDefenseBoundUserInterface)reopened).Window!;
            Assert.That(GetControlFromChildren<Button>(control => control.Name == "TowerTile_5_5", reopenedWindow).Text,
                Is.EqualTo("T3"));
            await CloseBui(TowerDefenseUiKey.Key);
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                Transform.SetCoordinates(SPlayer, SEntMan.GetCoordinates(PlayerCoords));
                if (SEntMan.EntityExists(arenaMap))
                    SEntMan.DeleteEntity(arenaMap);
            });
            await RunTicks(5);
        }
    }
}
