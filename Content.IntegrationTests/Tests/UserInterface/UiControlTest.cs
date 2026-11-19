using System.Collections.Generic;
using System.Linq;
using Content.Client.Lobby.UI;
using Content.Client.Store.Ui;
using Content.IntegrationTests.Fixtures;
using Content.Shared.FixedPoint;
using Content.Shared.Store;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.ContentPack;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Reflection;

#region Starlight
using Content.Client._Starlight.Lobby.UI;
#endregion

namespace Content.IntegrationTests.Tests.UserInterface;

[TestFixture]
public sealed class UiControlTest : GameTest
{
    [Test]
    public async Task ArenaVendorCategoryBalanceTracksSelection()
    {
        var client = Pair.Client;
        await client.WaitAssertion(() =>
        {
            using var menu = new StoreMenu();
            var listings = new HashSet<ListingDataWithCostModifiers>
            {
                new(client.ProtoMan.Index(new ProtoId<ListingPrototype>("ArenaNTSFWeaponRiflePeacemaker"))),
                new(client.ProtoMan.Index(new ProtoId<ListingPrototype>("ArenaERTWeaponRiflePeacemaker"))),
            };
            menu.SetCategoryBalance(true);
            menu.UpdateBalance(new Dictionary<ProtoId<CurrencyPrototype>, FixedPoint2>
            {
                ["ArenaNTSF"] = 20,
                ["ArenaERT"] = 0,
            });
            menu.PopulateStoreCategoryButtons(listings);
            menu.UpdateListing(listings.ToList());

            Assert.That(menu.BalanceInfo.Text, Does.Contain("NTSF credits: 20"));
            Assert.That(menu.BalanceInfo.Text, Does.Not.Contain("ERT credits"));
            menu.CurrentCategory = "ArenaERT";
            menu.UpdateListing();
            Assert.That(menu.BalanceInfo.Text, Does.Contain("ERT credits: 0"));
            Assert.That(menu.BalanceInfo.Text, Does.Not.Contain("NTSF credits"));

            menu.UpdateListing(new List<ListingDataWithCostModifiers>());
            Assert.That(menu.CurrentCategory, Is.EqualTo("ArenaERT"));
            Assert.That(menu.BalanceInfo.Text, Does.Contain("ERT credits: 0"));

            menu.SetCategoryBalance(false);
            Assert.That(menu.BalanceInfo.Text, Does.Contain("NTSF credits: 20"));
            Assert.That(menu.BalanceInfo.Text, Does.Contain("ERT credits: 0"));

            var uplinkListings = client.ProtoMan.EnumeratePrototypes<ListingPrototype>()
                .Where(listing => listing.Categories.Any(category =>
                    category.Id is "ArenaSyndicate" or "ArenaNukies" or "ArenaUSSP"))
                .Select(listing => new ListingDataWithCostModifiers(listing))
                .ToHashSet();
            menu.SetCategoryBalance(true);
            menu.PopulateStoreCategoryButtons(uplinkListings);
            foreach (var category in new[] { "ArenaSyndicate", "ArenaNukies", "ArenaUSSP" })
            {
                menu.CurrentCategory = category;
                menu.UpdateListing(uplinkListings.ToList());
            }
        });
    }

    // You should not be adding to this.
    private Type[] _ignored = new Type[]
    {
        typeof(LateJoinGui),
    };

    /// <summary>
    /// Tests that all windows can be instantiated successfully.
    /// </summary>
    [Test]
    public async Task TestWindows()
    {
        var pair = Pair;
        var activator = pair.Client.ResolveDependency<IDynamicTypeFactory>();
        var refManager = pair.Client.ResolveDependency<IReflectionManager>();
        var loader = pair.Client.ResolveDependency<IModLoader>();

        await pair.Client.WaitAssertion(() =>
        {
            foreach (var type in refManager.GetAllChildren(typeof(BaseWindow)))
            {
                if (type.IsAbstract || _ignored.Contains(type))
                    continue;

                if (!loader.IsContentType(type))
                    continue;

                // If it has no empty ctor then skip it instead of figuring out what args it needs.
                var ctor = type.GetConstructor(Type.EmptyTypes);

                if (ctor == null)
                    continue;

                // Don't inject because the control themselves have to do it.
                activator.CreateInstance(type, oneOff: true, inject: false);
            }
        });
    }
}
