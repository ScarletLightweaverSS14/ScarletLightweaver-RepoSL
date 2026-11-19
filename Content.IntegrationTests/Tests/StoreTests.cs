using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Starlight.Revolutionary;
using Content.Server.PDA.Ringer;
using Content.Server.Store.Systems;
using Content.Server.Traitor.Uplink;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Roles;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Content.Shared.StoreDiscount.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Random;
using Robust.Server.GameObjects;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class StoreTests : GameTest
{
    [Test]
    [PairConfig(nameof(PsDisconnected))]
    public async Task ArenaVendorAcceptsFactionCurrenciesAndSharesBalance()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var stores = server.System<StoreSystem>();

        await server.WaitAssertion(() =>
        {
            var vendor = entities.SpawnEntity("ArenaFactionVendor", map.GridCoords);
            var buyer = entities.SpawnEntity("HumanUniformDummy", map.GridCoords);
            var otherBuyer = entities.SpawnEntity("HumanUniformDummy", map.GridCoords);
            var store = entities.GetComponent<StoreComponent>(vendor);

            Assert.That(store.Categories, Has.Count.EqualTo(10));
            stores.UpdateUserInterface(buyer, vendor, store);
            var ui = server.System<UserInterfaceSystem>();
            Assert.That(ui.TryGetUiState<StoreUpdateState>(vendor, StoreUiKey.Key, out var state), Is.True);
            Assert.That(state!.CategoryBalance, Is.True);
            Assert.That(state.Listings.Any(listing =>
                listing.ProductEntity is { } product &&
                (product.Id.Contains("OxygenTank") || product.Id.Contains("NitrogenTank"))), Is.False);
            var credits = entities.SpawnEntity("NTCredit", map.GridCoords);
            Assert.That(stores.TryAddCurrency((credits, entities.GetComponent<CurrencyComponent>(credits)), (vendor, store)), Is.False);

            var ntsfToken = entities.SpawnEntity("ArenaTokenNTSF", map.GridCoords);
            Assert.That(stores.TryAddCurrency((ntsfToken, entities.GetComponent<CurrencyComponent>(ntsfToken)), (vendor, store)), Is.True);
            Assert.That(store.Balance["ArenaNTSF"], Is.EqualTo(FixedPoint2.New(20)));

            entities.EventBus.RaiseLocalEvent(vendor, new StoreBuyListingMessage("ArenaBlackstarWeaponRifleL10") { Actor = otherBuyer });
            Assert.That(store.Balance["ArenaNTSF"], Is.EqualTo(FixedPoint2.New(20)));
            Assert.That(store.Balance.ContainsKey("ArenaBlackstar"), Is.False);

            entities.EventBus.RaiseLocalEvent(vendor, new StoreBuyListingMessage("ArenaNTSFWeaponRiflePeacemaker") { Actor = otherBuyer });
            Assert.That(store.Balance["ArenaNTSF"], Is.EqualTo(FixedPoint2.New(13)));

            var tc = entities.SpawnEntity("Telecrystal5", map.GridCoords);
            var bonds = entities.SpawnEntity("Telebond5", map.GridCoords);
            var crystal = entities.SpawnEntity("ArenaBloodredCrystal", map.GridCoords);
            Assert.That(stores.TryAddCurrency((tc, entities.GetComponent<CurrencyComponent>(tc)), (vendor, store)), Is.True);
            Assert.That(stores.TryAddCurrency((bonds, entities.GetComponent<CurrencyComponent>(bonds)), (vendor, store)), Is.True);
            Assert.That(stores.TryAddCurrency((crystal, entities.GetComponent<CurrencyComponent>(crystal)), (vendor, store)), Is.True);
            Assert.That(store.Balance["Telecrystal"], Is.EqualTo(FixedPoint2.New(5)));
            Assert.That(store.Balance["Telebond"], Is.EqualTo(FixedPoint2.New(5)));
            Assert.That(store.Balance["ArenaNukie"], Is.EqualTo(FixedPoint2.New(40)));

            entities.EventBus.RaiseLocalEvent(vendor, new StoreBuyListingMessage("ArenaSyndicateUplinkPistolViper") { Actor = buyer });
            entities.EventBus.RaiseLocalEvent(vendor, new StoreBuyListingMessage("ArenaUSSPSKBMakarov") { Actor = buyer });
            entities.EventBus.RaiseLocalEvent(vendor, new StoreBuyListingMessage("ArenaNukiesUplinkEsword") { Actor = buyer });
            Assert.That(store.Balance["Telecrystal"], Is.EqualTo(FixedPoint2.New(2)));
            Assert.That(store.Balance["Telebond"], Is.EqualTo(FixedPoint2.New(3)));
            Assert.That(store.Balance["ArenaNukie"], Is.EqualTo(FixedPoint2.New(32)));

            entities.EventBus.RaiseLocalEvent(vendor, new StoreBuyListingMessage("ArenaUSSPSKBSmallFlashConversion") { Actor = buyer });
            Assert.That(store.Balance["Telebond"], Is.EqualTo(FixedPoint2.New(1)));

            var rifts = server.System<RevSupplyRiftSystem>();
            entities.EventBus.RaiseLocalEvent(vendor, new StoreBuyListingMessage("ArenaUSSPRevSupplyRiftListing") { Actor = buyer });
            Assert.That(rifts.IsRiftBeingProcessed(), Is.False);
            for (var i = 0; i < 2; i++)
            {
                var extraBonds = entities.SpawnEntity("Telebond5", map.GridCoords);
                Assert.That(stores.TryAddCurrency((extraBonds, entities.GetComponent<CurrencyComponent>(extraBonds)), (vendor, store)), Is.True);
            }
            entities.EventBus.RaiseLocalEvent(vendor, new StoreBuyListingMessage("ArenaUSSPRevSupplyRiftListing") { Actor = buyer });
            Assert.That(store.Balance["Telebond"], Is.EqualTo(FixedPoint2.New(3)));
            Assert.That(rifts.IsRiftBeingProcessed(), Is.True);
            var moreBonds = entities.SpawnEntity("Telebond5", map.GridCoords);
            Assert.That(stores.TryAddCurrency((moreBonds, entities.GetComponent<CurrencyComponent>(moreBonds)), (vendor, store)), Is.True);
            entities.EventBus.RaiseLocalEvent(vendor, new StoreBuyListingMessage("ArenaUSSPRevSupplyRiftListing") { Actor = buyer });
            Assert.That(store.Balance["Telebond"], Is.EqualTo(FixedPoint2.New(8)));

            var otherFactions = new[]
            {
                (Token: "ArenaTokenERT", Currency: "ArenaERT", Listing: "ArenaERTWeaponRiflePeacemaker"),
                (Token: "ArenaTokenSecurity", Currency: "ArenaSecurity", Listing: "ArenaSecurityWeaponRifleLecter"),
                (Token: "ArenaTokenGorlex", Currency: "ArenaGorlex", Listing: "ArenaGorlexWeaponRifleEstoc"),
                (Token: "ArenaTokenBlackstar", Currency: "ArenaBlackstar", Listing: "ArenaBlackstarWeaponRifleL10"),
                (Token: "ArenaTokenPirate", Currency: "ArenaPirate", Listing: "ArenaPiratesWeaponRevolverGA6Fang"),
                (Token: "ArenaTokenTSF", Currency: "ArenaTSF", Listing: "ArenaTSFWeaponShotgunCycler"),
            };
            foreach (var faction in otherFactions)
            {
                var token = entities.SpawnEntity(faction.Token, map.GridCoords);
                var listing = store.FullListingsCatalog.Single(x => x.ID == faction.Listing);
                Assert.That(stores.ListingHasCategory(listing, store.Categories), Is.True, faction.Listing);
                Assert.That(stores.TryAddCurrency((token, entities.GetComponent<CurrencyComponent>(token)), (vendor, store)), Is.True, faction.Token);
                entities.EventBus.RaiseLocalEvent(vendor, new StoreBuyListingMessage(faction.Listing) { Actor = otherBuyer });
                Assert.That(store.Balance[faction.Currency], Is.EqualTo(FixedPoint2.New(20) - listing.Cost[faction.Currency]), faction.Listing);
            }
        });
    }

    [Test]
    [PairConfig(nameof(PsDisconnected))]
    public async Task ArenaVendorCatalogIncludesUplinksAndSupplementalGear()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var stores = server.System<StoreSystem>();

        await server.WaitAssertion(() =>
        {
            var vendor = entities.SpawnEntity("ArenaFactionVendor", map.GridCoords);
            var buyer = entities.SpawnEntity("HumanUniformDummy", map.GridCoords);
            var store = entities.GetComponent<StoreComponent>(vendor);
            var available = stores.GetAvailableListings(buyer, vendor, store).ToDictionary(listing => listing.ID);

            var sourceCategories = new[]
            {
                "UplinkWeaponry", "UplinkAmmo", "UplinkExplosives", "UplinkChemicals",
                "UplinkDeception", "UplinkDisruption", "UplinkImplants", "UplinkAllies",
                "UplinkWearables", "UplinkJob", "UplinkCybernetics", "UplinkPointless",
                "UplinkObjective", "UplinkDAGD",
            };
            var usspCategories = new[]
            {
                "RevUplinkWeaponry", "RevUplinkConversionRewards", "RevUplinkWearables",
                "RevUplinkImplants", "RevUplinkShuttle", "RevUplinkDeception",
                "RevUplinkPointless", "RevUplinkWar",
            };
            foreach (var source in server.ProtoMan.EnumeratePrototypes<ListingPrototype>())
            {
                var syndicate = source.Categories.Any(category => sourceCategories.Contains(category.Id));
                var ussp = source.Categories.Any(category => usspCategories.Contains(category.Id));
                if (!syndicate && !ussp)
                    continue;

                var factions = syndicate ? new[] { "Syndicate", "Nukies" } : new[] { "USSP" };
                foreach (var faction in factions)
                {
                    var id = $"Arena{faction}{source.ID}";
                    Assert.That(available.TryGetValue(id, out var copy), Is.True, id);
                    Assert.That(copy!.ProductEntity, Is.EqualTo(source.ProductEntity), id);
                    Assert.That(copy.Conditions, Is.Null.Or.Empty, id);
                    var currency = faction == "Syndicate" ? "Telecrystal" : faction == "USSP" ? "Telebond" : "ArenaNukie";
                    Assert.That(copy.Cost.Keys.Select(key => key.Id), Is.EquivalentTo(new[] { currency }), id);
                    var originalCurrency = syndicate ? "Telecrystal" : "Telebond";
                    if (source.Cost.TryGetValue(originalCurrency, out var originalCost))
                        Assert.That(copy.Cost[currency], Is.EqualTo(originalCost), id);
                }
            }

            foreach (var faction in new[] { "NTSF", "ERT" })
            {
                foreach (var item in new[] { "WeaponRiflePeacemaker", "GrenadeShrapnel", "GrenadeIncendiary" })
                    Assert.That(available.ContainsKey($"Arena{faction}{item}"), Is.True, $"{faction}: {item}");
            }
            Assert.That(available.ContainsKey("ArenaNTSFEnergyShield"), Is.True);
            Assert.That(available.ContainsKey("ArenaNTSFEnergyShieldUnbreakable"), Is.True);
            Assert.That(available.ContainsKey("ArenaNTSFWeaponPulseCarbine"), Is.False);
            Assert.That(available.ContainsKey("ArenaERTClothingOuterHardsuitERTSecurity"), Is.False);
            Assert.That(available.ContainsKey("ArenaBlackstarWeaponRiflePrometheus"), Is.False);
            Assert.That(available.ContainsKey("ArenaTSFWeaponRifleAk"), Is.False);
            Assert.That(available.ContainsKey("ArenaPiratesEnergyCutlass"), Is.False);

            var gearGroups = new[]
            {
                (Faction: "NTSF", Prefixes: new[] { "BaseNanoTrasenSpecialForces", "NanoTrasenSpecialForces", "DecimusGear" }),
                (Faction: "ERT", Prefixes: new[] { "ERT" }),
                (Faction: "Security", Prefixes: new[] { "SecurityOfficerGear", "SecurityCadetGear", "NanotrasenParamilitaryGear" }),
                (Faction: "Gorlex", Prefixes: new[] { "SyndicateFootsoldier" }),
                (Faction: "Blackstar", Prefixes: new[] { "Blackstar" }),
                (Faction: "Pirates", Prefixes: new[] { "Pirate" }),
                (Faction: "TSF", Prefixes: new[] { "TSF", "SolGovOfficer" }),
                (Faction: "Nukies", Prefixes: new[] { "SyndicateOperative", "SyndicateCommander", "SyndicateLoneOperative" }),
            };
            foreach (var group in gearGroups)
            {
                var issued = server.ProtoMan.EnumeratePrototypes<StartingGearPrototype>()
                    .Where(gear => group.Prefixes.Any(prefix => gear.ID.StartsWith(prefix)))
                    .SelectMany(gear => gear.Equipment.Values.Concat(gear.Inhand).Concat(gear.Storage.Values.SelectMany(items => items)))
                    .Select(entity => entity.Id)
                    .ToHashSet();
                foreach (var listing in available.Values.Where(listing =>
                             listing.ProductEntity != null &&
                             listing.ID == $"Arena{group.Faction}{listing.ProductEntity.Value.Id}"))
                {
                    var product = listing.ProductEntity!.Value.Id;
                    var requestedException =
                        (group.Faction is "NTSF" or "ERT" &&
                         product is "WeaponRiflePeacemaker" or "GrenadeShrapnel" or "GrenadeIncendiary") ||
                        (group.Faction == "NTSF" && product is "EnergyShield" or "EnergyShieldUnbreakable");
                    Assert.That(requestedException || !issued.Contains(product), Is.True, listing.ID);
                }
            }
        });
    }

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  name: InventoryPdaDummy
  id: InventoryPdaDummy
  parent: BasePDA
  components:
  - type: Clothing
    QuickEquip: false
    slots:
    - idcard
  - type: Pda
";

    [Test]
    [Ignore("""
        This currently causes the client to crash, failing the test.
        When this is fixed, this test should be removed and StoreDiscountAndRefund
        should just use the default pair config.
    """)]
    public async Task StoreDiscountAndRefundWithClient()
    {
        await StoreDiscountAndRefund();
    }

    [Test]
    [PairConfig(nameof(PsDisconnected))]
    public async Task StoreDiscountAndRefund()
    {
        var pair = Pair;
        var server = pair.Server;

        var testMap = await pair.CreateTestMap();
        await server.WaitIdleAsync();

        var serverRandom = server.ResolveDependency<IRobustRandom>();
        serverRandom.SetSeed(534);

        var entManager = server.ResolveDependency<IEntityManager>();

        var mapSystem = server.System<SharedMapSystem>();
        var prototypeManager = server.ProtoMan;

        Assert.That(mapSystem.IsInitialized(testMap.MapId));


        EntityUid human = default;
        EntityUid uniform = default;
        EntityUid pda = default;

        var uplinkSystem = entManager.System<UplinkSystem>();
        var ringerSystem = entManager.System<RingerSystem>();

        var listingPrototypes = prototypeManager.EnumeratePrototypes<ListingPrototype>()
                                                .ToArray();

        var coordinates = testMap.GridCoords;
        await server.WaitAssertion(() =>
        {
            var invSystem = entManager.System<InventorySystem>();
            var mindSystem = entManager.System<SharedMindSystem>();

            human = entManager.SpawnEntity("HumanUniformDummy", coordinates);
            uniform = entManager.SpawnEntity("UniformDummy", coordinates);
            pda = entManager.SpawnEntity("InventoryPdaDummy", coordinates);

            Assert.That(invSystem.TryEquip(human, uniform, "jumpsuit"));
            Assert.That(invSystem.TryEquip(human, pda, "id"));

            var mind = mindSystem.CreateMind(null);
            mindSystem.TransferTo(mind, human, mind: mind);

            FixedPoint2 originalBalance = 20;
            uplinkSystem.AddUplink(human, originalBalance, out var notes, pda, true);

            Assert.That(notes, Is.Not.Null);
            Assert.That(ringerSystem.TryMatchRingtoneToStore(notes!, out var storeEnt), Is.True);
            Assert.That(storeEnt.HasValue);
            var storeComponent = entManager.GetComponent<StoreComponent>(storeEnt!.Value);
            var discountComponent = entManager.GetComponent<StoreDiscountComponent>(storeEnt.Value);
            Assert.That(
                discountComponent.Discounts,
                Has.Exactly(6).Items,
                $"After applying discount total discounted items count was expected to be '6' "
                + $"but was actually {discountComponent.Discounts.Count}- this can be due to discount "
                + $"categories settings (maxItems, weight) not being realistically set, or default "
                + $"discounted count being changed from '6' in StoreDiscountSystem.InitializeDiscounts."
            );
            var discountedListingItems = storeComponent.FullListingsCatalog
                                                       .Where(x => x.IsCostModified)
                                                       .OrderBy(x => x.ID)
                                                       .ToArray();
            Assert.That(discountComponent.Discounts
                                         .Select(x => x.ListingId.Id),
                Is.EquivalentTo(discountedListingItems.Select(x => x.ID)),
                $"{nameof(StoreComponent)}.{nameof(StoreComponent.FullListingsCatalog)} does not contain all "
                + $"items that are marked as discounted, or they don't have flag '{nameof(ListingDataWithCostModifiers.IsCostModified)}'"
                + $"flag as 'true'. This marks the fact that cost modifier of discount is not applied properly!"
            );

            // The storeComponent returns discounted items with conditions randomly, so we remove these to sanitize the data.
            foreach (var discountedItem in discountedListingItems)
            {
                discountedItem.Conditions = null;
            }

            // Refund action requests re-generation of listing items so we will be re-acquiring items from component a lot of times.
            var itemIds = discountedListingItems.Select(x => x.ID);
            foreach (var itemId in itemIds)
            {
                Assert.Multiple(() =>
                {
                    storeComponent.RefundAllowed = true;

                    var discountedListingItem = storeComponent.FullListingsCatalog.First(x => x.ID == itemId);
                    var plainDiscountedCost = discountedListingItem.Cost[UplinkSystem.TelecrystalCurrencyPrototype];

                    var prototype = listingPrototypes.First(x => x.ID == discountedListingItem.ID);

                    var prototypeCost = prototype.Cost[UplinkSystem.TelecrystalCurrencyPrototype];
                    var discountDownTo = prototype.DiscountDownTo[UplinkSystem.TelecrystalCurrencyPrototype];
                    Assert.That(plainDiscountedCost.Value, Is.GreaterThanOrEqualTo(discountDownTo.Value), "Expected discounted cost to be greater then DiscountDownTo value.");
                    Assert.That(plainDiscountedCost.Value, Is.LessThan(prototypeCost.Value), "Expected discounted cost to be lower then prototype cost.");


                    var buyMsg = new StoreBuyListingMessage(discountedListingItem.ID){Actor = human};
                    server.EntMan.EventBus.RaiseLocalEvent(storeEnt!.Value, buyMsg);

                    var newBalance = storeComponent.Balance[UplinkSystem.TelecrystalCurrencyPrototype];
                    Assert.That(newBalance.Value, Is.EqualTo((originalBalance - plainDiscountedCost).Value), "Expected to have balance reduced by discounted cost");
                    Assert.That(
                        discountedListingItem.IsCostModified,
                        Is.False,
                        $"Expected item cost to not be modified after Buying discounted item."
                    );
                    var costAfterBuy = discountedListingItem.Cost[UplinkSystem.TelecrystalCurrencyPrototype];
                    Assert.That(costAfterBuy.Value, Is.EqualTo(prototypeCost.Value), "Expected cost after discount refund to be equal to prototype cost.");

                    var refundMsg = new StoreRequestRefundMessage { Actor = human };
                    server.EntMan.EventBus.RaiseLocalEvent(storeEnt!.Value, refundMsg);

                    // get refreshed item after refund re-generated items
                    discountedListingItem = storeComponent.FullListingsCatalog.First(x => x.ID == itemId);

                    // The storeComponent can give a discounted item a condition at random, so we remove it to sanitize the data.
                    discountedListingItem.Conditions = null;

                    var afterRefundBalance = storeComponent.Balance[UplinkSystem.TelecrystalCurrencyPrototype];
                    Assert.That(afterRefundBalance.Value, Is.EqualTo(originalBalance.Value), "Expected refund to return all discounted cost value.");
                    Assert.That(
                        discountComponent.Discounts.First(x => x.ListingId == discountedListingItem.ID).Count,
                        Is.EqualTo(0),
                        "Discounted count should still be zero even after refund."
                    );

                    Assert.That(
                        discountedListingItem.IsCostModified,
                        Is.False,
                        $"Expected item cost to not be modified after Buying discounted item (even after refund was done)."
                    );
                    var costAfterRefund = discountedListingItem.Cost[UplinkSystem.TelecrystalCurrencyPrototype];
                    Assert.That(costAfterRefund.Value, Is.EqualTo(prototypeCost.Value), "Expected cost after discount refund to be equal to prototype cost.");
                });
            }

        });
    }
}
