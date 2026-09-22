using System.Linq;
using System.IO;
using System.Numerics;
using Content.IntegrationTests.Tests.Helpers;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._Starlight.Weapons.Ranged;
using Content.Shared._Starlight.Weapons.Ranged.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Input;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Client.Input;
using Robust.Client.UserInterface.Controls;
using Robust.Client.ViewVariables;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.ViewVariables;

namespace Content.IntegrationTests.Tests._Starlight.Weapons;

public sealed class AltWeaponryTest : InteractionTest
{
    public sealed class ShotListenerSystem : TestListenerSystem<AmmoShotEvent>;

    // A different secondary weapon and ammo container, configured entirely through YAML.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: AltWeaponryTestRifle
  parent: WeaponRifleM90GrenadeLauncher
  components:
  - type: AltWeaponry
    gunPrototype: AltWeaponryTestShotgun
    container: secondary_test_gun
    ammoContainer: shell_chamber
    ammoSlot:
      name: Shell
      whitelist:
        tags:
        - ShellShotgun

- type: entity
  id: AltWeaponryTestShotgun
  categories: [ HideSpawnMenu ]
  components:
  - type: Gun
    fireRate: 1
    selectedMode: SemiAuto
    availableModes: [SemiAuto]
    soundGunshot:
      path: /Audio/Weapons/Guns/Gunshots/shotgun.ogg
  - type: ContainerAmmoProvider
    container: shell_chamber

- type: entity
  id: AltWeaponryTestGunCollision
  parent: WeaponRifleM90GrenadeLauncher
  components:
  - type: AltWeaponry
    container: gun_magazine

- type: entity
  id: AltWeaponryTestAmmoCollision
  parent: WeaponRifleM90GrenadeLauncher
  components:
  - type: AltWeaponry
    ammoContainer: gun_chamber

- type: entity
  id: AltWeaponryTestSameContainer
  parent: WeaponRifleM90GrenadeLauncher
  components:
  - type: AltWeaponry
    ammoContainer: gun_magazine
    container: gun_magazine
";

    [TestCase("WeaponRifleM90GrenadeLauncher", "grenade_chamber", "Grenade40mmFrag", "ShellShotgun", "BulletGrenade40mmFrag", 1)]
    [TestCase("WeaponRifleM90GrenadeLauncher", "grenade_chamber", "Grenade40mmBlast", "ShellShotgun", "BulletGrenade40mmBlast", 1)]
    [TestCase("AltWeaponryTestRifle", "shell_chamber", "ShellShotgun", "Grenade40mmFrag", "PelletShotgunSpreadTrace", 12)]
    [TestCase("AltWeaponryTestRifle", "shell_chamber", "ShellShotgunSlug", "Grenade40mmBlast", "PelletShotgunSlugTrace", 1)]
    public async Task SingleShotAndIndependentAmmo(string weaponPrototype, string ammoContainer, string ammoPrototype,
        string wrongCaliber, string projectilePrototype, int projectileCount)
    {
        var weapon = SEntMan.GetEntity(await PlaceInHands(weaponPrototype));
        await SetCombatMode(true);
        await RunTicks(60);
        await Client.WaitAssertion(() =>
        {
            Assert.That(InputManager.TryGetKeyBinding(ContentKeyFunctions.SecondaryWeaponAction, out var binding), Is.True);
            Assert.That(binding!.BaseKey, Is.EqualTo(Keyboard.Key.RightAlt));
        });

        await Server.WaitAssertion(() =>
        {
            var guns = SEntMan.System<GunSystem>();
            var slots = SEntMan.System<ItemSlotsSystem>();
            var containers = SEntMan.System<SharedContainerSystem>();
            var coordinates = SEntMan.GetCoordinates(PlayerCoords);
            var aim = coordinates.Offset(new Vector2(20, 0));
            var chamber = containers.GetContainer(weapon, ammoContainer);
            guns.SetBoltClosed(weapon, SEntMan.GetComponent<ChamberMagazineAmmoProviderComponent>(weapon), true);
            var rifleAmmo = guns.GetAmmoCount(weapon);
            Assert.That(rifleAmmo, Is.GreaterThan(0));

            var alternate = SEntMan.GetComponent<AltWeaponryComponent>(weapon);
            var secondary = containers.GetContainer(weapon, alternate.Container);
            Assert.That(secondary.ContainedEntities, Has.Count.EqualTo(1));
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(secondary.ContainedEntities[0]).EntityPrototype!.ID,
                Is.EqualTo(alternate.GunPrototype.Id));
            Assert.That(chamber.ContainedEntities, Is.Empty);
            // Full-sized grenades and rifle cartridges do not fit, even in an empty chamber.
            foreach (var invalid in new[] { "GrenadeFrag", "GrenadeBlast", "CartridgeRifleFMJ", wrongCaliber })
            {
                var other = SEntMan.SpawnEntity(invalid, coordinates);
                Assert.That(slots.TryInsert(weapon, ammoContainer, other, null), Is.False);
                SEntMan.DeleteEntity(other);
            }
            var round = SEntMan.SpawnEntity(ammoPrototype, coordinates);
            Assert.That(slots.TryInsert(weapon, ammoContainer, round, null), Is.True);
            var extra = SEntMan.SpawnEntity(ammoPrototype, coordinates);
            Assert.That(slots.TryInsert(weapon, ammoContainer, extra, null), Is.False);
            SEntMan.DeleteEntity(extra);
            Assert.That(slots.TryInsert(weapon, "gun_chamber", round, null), Is.False);

            // Firing the rifle leaves the loaded secondary round alone.
            Assert.That(guns.AttemptShoot(SPlayer, (weapon, SEntMan.GetComponent<GunComponent>(weapon)), aim), Is.True);
            Assert.That(chamber.ContainedEntities, Does.Contain(round));
            Assert.That(guns.GetAmmoCount(weapon), Is.EqualTo(rifleAmmo - 1));
        });

        // The rifle also applies a short shared melee/shooting cooldown.
        await RunTicks(30);
        await Server.WaitAssertion(() =>
        {
            var guns = SEntMan.System<GunSystem>();
            var chamber = SEntMan.System<SharedContainerSystem>().GetContainer(weapon, ammoContainer);
            var round = chamber.ContainedEntities[0];
            var rifleAmmo = guns.GetAmmoCount(weapon);
            var aim = SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(20, 0));
            var cartridge = SEntMan.GetComponent<CartridgeAmmoComponent>(round);
            Assert.That(cartridge.Prototype.Id, Is.EqualTo(projectilePrototype));
            SEntMan.EnsureComponent<TestListenerComponent>(round);
            Assert.That(SEntMan.System<AltWeaponrySystem>().TryFire(SPlayer, aim), Is.True);
            var shot = GetEvents<AmmoShotEvent>(round).Single();
            Assert.That(shot.FiredProjectiles, Has.Count.EqualTo(projectileCount));
            // Hitscan shells delete their trace entities immediately; physical projectiles remain.
            if (!SEntMan.Deleted(shot.FiredProjectiles[0]))
            {
                Assert.That(SEntMan.GetComponent<MetaDataComponent>(shot.FiredProjectiles[0]).EntityPrototype!.ID,
                    Is.EqualTo(projectilePrototype));
            }
            Assert.That(chamber.ContainedEntities, Is.Empty);
            Assert.That(cartridge.Spent, Is.True);
            Assert.That(guns.GetAmmoCount(weapon), Is.EqualTo(rifleAmmo));
            Assert.That(SEntMan.System<AltWeaponrySystem>().TryFire(SPlayer, aim), Is.False);
        });

        await RunTicks(65);
        await Server.WaitAssertion(() =>
        {
            var slots = SEntMan.System<ItemSlotsSystem>();
            var coordinates = SEntMan.GetCoordinates(PlayerCoords);
            var aim = coordinates.Offset(new Vector2(20, 0));
            // Even after the cooldown it stays empty until manually reloaded.
            Assert.That(SEntMan.System<AltWeaponrySystem>().TryFire(SPlayer, aim), Is.False);
            var reload = SEntMan.SpawnEntity(ammoPrototype, coordinates);
            Assert.That(slots.TryInsert(weapon, ammoContainer, reload, null), Is.True);

            // A contained gun must not bypass the held weapon's firing pin.
            var containers = SEntMan.System<SharedContainerSystem>();
            var pins = containers.GetContainer(weapon, "firing_pin");
            var pin = pins.ContainedEntities.Single();
            Assert.That(containers.Remove(pin, pins), Is.True);
            Assert.That(SEntMan.System<AltWeaponrySystem>().TryFire(SPlayer, aim), Is.False);
            Assert.That(containers.GetContainer(weapon, ammoContainer).ContainedEntities, Does.Contain(reload));
            Assert.That(containers.Insert(pin, pins), Is.True);
        });

        await RunTicks(65);
        await SetCombatMode(false);
        await Server.WaitAssertion(() =>
        {
            var aim = SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(20, 0));
            Assert.That(SEntMan.System<AltWeaponrySystem>().TryFire(SPlayer, aim), Is.False);
            Assert.That(SEntMan.System<SharedContainerSystem>().GetContainer(weapon, ammoContainer).ContainedEntities, Has.Count.EqualTo(1));
        });
        await SetCombatMode(true);
        var target = SEntMan.GetNetCoordinates(SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(20, 0)));
        await PressKey(ContentKeyFunctions.SecondaryWeaponAction, coordinates: target);
        await RunTicks(10);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedContainerSystem>().GetContainer(weapon, ammoContainer).ContainedEntities, Is.Empty);
        });
    }

    [Test]
    public async Task AddToExistingDoubleBarrel()
    {
        var netWeapon = await PlaceInHands("WeaponShotgunDoubleBarreled");
        var weapon = SEntMan.GetEntity(netWeapon);
        await SetCombatMode(true);
        await RunTicks(60);
        EntityUid round = default;
        EntityUid secondary = default;
        var primaryAmmo = 0;
        await Server.WaitAssertion(() =>
        {
            var alternate = SEntMan.AddComponent<AltWeaponryComponent>(weapon);
            Assert.That(alternate.Prototype, Is.EqualTo(string.Empty));
        });
        await RunTicks(10);
        await Client.WaitAssertion(() =>
        {
            var value = CEntMan.GetComponent<AltWeaponryComponent>(CEntMan.GetEntity(netWeapon)).Prototype;
            Assert.That(value, Is.EqualTo(string.Empty));
            // Remote VV chooses an editor from the current value's type, not the declared field type.
            var editor = Client.ResolveDependency<IViewVariableControlFactory>().CreateFor(value?.GetType());
            using var control = editor.Initialize(value, false);
            Assert.That(control, Is.InstanceOf<LineEdit>());
            Assert.That(((LineEdit) control).Editable, Is.True);
        });
        await Server.WaitAssertion(() =>
        {
            var alternate = SEntMan.GetComponent<AltWeaponryComponent>(weapon);
            var path = Server.ResolveDependency<IViewVariablesManager>().ResolvePath($"/entity/{weapon}/AltWeaponry/Prototype");
            Assert.That(path, Is.Not.Null);
            path!.Set("ShellShotgun");
            Assert.That(alternate.Prototype, Is.EqualTo("ShellShotgun"));
            var vv = Server.ResolveDependency<IViewVariablesManager>();
            vv.ResolvePath($"/entity/{weapon}/AltWeaponry/Container")!.Set("gun_magazine");
            vv.ResolvePath($"/entity/{weapon}/AltWeaponry/AmmoContainer")!.Set("gun_chamber");
            Assert.That(alternate.Container, Is.EqualTo("alt_weapon"));
            Assert.That(alternate.AmmoContainer, Is.EqualTo("altweapon"));
            var containers = SEntMan.System<SharedContainerSystem>();
            secondary = containers.GetContainer(weapon, alternate.Container).ContainedEntities.Single();
            var slots = SEntMan.System<ItemSlotsSystem>();
            var coordinates = SEntMan.GetCoordinates(PlayerCoords);
            var aim = coordinates.Offset(new Vector2(20, 0));
            primaryAmmo = SEntMan.System<GunSystem>().GetAmmoCount(weapon);
            Assert.That(primaryAmmo, Is.EqualTo(2));
            foreach (var invalid in new[] { "ShellShotgunSlug", "Grenade40mmFrag", "CartridgeRifleFMJ" })
            {
                var ammo = SEntMan.SpawnEntity(invalid, coordinates);
                Assert.That(slots.TryInsert(weapon, "altweapon", ammo, null), Is.False);
                SEntMan.DeleteEntity(ammo);
            }
            round = SEntMan.SpawnEntity("ShellShotgun", coordinates);
            Assert.That(slots.TryInsert(weapon, "altweapon", round, null), Is.True);
            Assert.That(SEntMan.System<AltWeaponrySystem>().TryFire(SPlayer, aim), Is.False, "The shotgun must still be wielded.");
            HandSys.AddHand(SPlayer, "hand_left", HandLocation.Left);
            Assert.That(SEntMan.System<SharedWieldableSystem>().TryWield(weapon,
                SEntMan.GetComponent<WieldableComponent>(weapon), SPlayer), Is.True);
        });

        await RunTicks(30);
        await Client.WaitAssertion(() =>
        {
            var clientWeapon = CEntMan.GetEntity(netWeapon);
            Assert.That(CEntMan.GetComponent<AltWeaponryComponent>(clientWeapon).Prototype, Is.EqualTo("ShellShotgun"));
            Assert.That(CEntMan.System<ItemSlotsSystem>().TryGetSlot(clientWeapon, "altweapon", out _), Is.True);
        });
        var target = SEntMan.GetNetCoordinates(SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(20, 0)));
        await PressKey(ContentKeyFunctions.SecondaryWeaponAction, coordinates: target);
        await RunTicks(10);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<CartridgeAmmoComponent>(round).Spent, Is.True);
            Assert.That(SEntMan.System<GunSystem>().GetAmmoCount(weapon), Is.EqualTo(primaryAmmo));
            Assert.That(SEntMan.System<SharedContainerSystem>().GetContainer(weapon, "altweapon").ContainedEntities, Is.Empty);

            // Change and clear the setting on the existing component through VV, without re-adding it.
            var path = Server.ResolveDependency<IViewVariablesManager>().ResolvePath($"/entity/{weapon}/AltWeaponry/Prototype")!;
            var slots = SEntMan.System<ItemSlotsSystem>();
            var slug = SEntMan.SpawnEntity("ShellShotgunSlug", SEntMan.GetCoordinates(PlayerCoords));
            Assert.That(slots.TryInsert(weapon, "altweapon", slug, null), Is.False);
            path.Set(string.Empty);
            Assert.That(slots.TryInsert(weapon, "altweapon", slug, null), Is.False);
            path.Set("ShellShotgunSlug");
            Assert.That(slots.TryInsert(weapon, "altweapon", slug, null), Is.True);
            round = slug;
        });
        await RunTicks(65);
        await Client.WaitAssertion(() =>
            Assert.That(CEntMan.GetComponent<AltWeaponryComponent>(CEntMan.GetEntity(netWeapon)).Prototype, Is.EqualTo("ShellShotgunSlug")));
        await PressKey(ContentKeyFunctions.SecondaryWeaponAction, coordinates: target);
        await RunTicks(10);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<CartridgeAmmoComponent>(round).Spent, Is.True);
            Assert.That(SEntMan.System<GunSystem>().GetAmmoCount(weapon), Is.EqualTo(primaryAmmo));
            SEntMan.RemoveComponent<AltWeaponryComponent>(weapon);
            // Removing the final slot also removes ItemSlots; querying a missing component logs an error.
            Assert.That(SEntMan.HasComponent<ItemSlotsComponent>(weapon), Is.False);
            Assert.That(SEntMan.System<SharedContainerSystem>().TryGetContainer(weapon, "alt_weapon", out _), Is.False);
        });
        await RunTicks(5);
        await Server.WaitAssertion(() => Assert.That(SEntMan.Deleted(secondary), Is.True));
    }

    [Test]
    public async Task ConflictingNamesLeavePrimaryWeaponIntact()
    {
        await Server.WaitAssertion(() =>
        {
            var containers = SEntMan.System<SharedContainerSystem>();
            var slots = SEntMan.System<ItemSlotsSystem>();
            var coordinates = SEntMan.GetCoordinates(PlayerCoords);
            foreach (var prototype in new[] { "AltWeaponryTestGunCollision", "AltWeaponryTestAmmoCollision", "AltWeaponryTestSameContainer" })
            {
                var weapon = SEntMan.SpawnEntity(prototype, coordinates);
                var alternate = SEntMan.GetComponent<AltWeaponryComponent>(weapon);
                Assert.That(alternate.OwnsContainers, Is.False);
                Assert.That(alternate.InitializationError, Is.Not.Empty);
                Assert.That(alternate.SecondaryGun, Is.Null);
                var magazineContainer = containers.GetContainer(weapon, "gun_magazine");
                var magazine = magazineContainer.ContainedEntities.Single();
                // A failed alternate slot must not impose its grenade whitelist on the rifle chamber.
                var cartridge = SEntMan.SpawnEntity("CartridgeRifleFMJ", coordinates);
                Assert.That(slots.TryInsert(weapon, "gun_chamber", cartridge, null), Is.True);
                SEntMan.RemoveComponent<AltWeaponryComponent>(weapon);
                Assert.That(containers.GetContainer(weapon, "gun_magazine"), Is.SameAs(magazineContainer));
                Assert.That(magazineContainer.ContainedEntities, Does.Contain(magazine));
                Assert.That(containers.GetContainer(weapon, "gun_chamber").ContainedEntities, Does.Contain(cartridge));
                Assert.That(SEntMan.Deleted(magazine), Is.False);
                SEntMan.DeleteEntity(weapon);
            }

            // Also reject an occupied default name when the component is added in-game.
            var shotgun = SEntMan.SpawnEntity("WeaponShotgunDoubleBarreled", coordinates);
            var foreign = containers.EnsureContainer<ContainerSlot>(shotgun, "alt_weapon");
            var shell = SEntMan.SpawnEntity("ShellShotgun", coordinates);
            Assert.That(containers.Insert(shell, foreign), Is.True);
            Assert.That(SEntMan.AddComponent<AltWeaponryComponent>(shotgun).OwnsContainers, Is.False);
            SEntMan.RemoveComponent<AltWeaponryComponent>(shotgun);
            Assert.That(containers.GetContainer(shotgun, "alt_weapon"), Is.SameAs(foreign));
            Assert.That(foreign.ContainedEntities, Does.Contain(shell));
            Assert.That(SEntMan.Deleted(shell), Is.False);
        });
    }

    [Test]
    public async Task CleanupOnlyRemovesOwnedResources()
    {
        await Server.WaitAssertion(() =>
        {
            var containers = SEntMan.System<SharedContainerSystem>();
            var slots = SEntMan.System<ItemSlotsSystem>();
            var coordinates = SEntMan.GetCoordinates(PlayerCoords);
            foreach (var replaceContainer in new[] { false, true })
            {
                var weapon = SEntMan.SpawnEntity("WeaponRifleM90GrenadeLauncher", coordinates);
                var alternate = SEntMan.GetComponent<AltWeaponryComponent>(weapon);
                var primaryContainer = containers.GetContainer(weapon, "gun_magazine");
                var primaryMagazine = primaryContainer.ContainedEntities.Single();
                var gunContainer = alternate.OwnedGunContainer!;
                var secondary = alternate.SecondaryGun!.Value;
                var grenade = SEntMan.SpawnEntity("Grenade40mmFrag", coordinates);
                Assert.That(slots.TryInsert(weapon, alternate.AmmoContainer, grenade, null), Is.True);
                var unrelated = SEntMan.SpawnEntity("MagazineRifleFMJ", coordinates);
                if (replaceContainer)
                {
                    containers.ShutdownContainer(gunContainer);
                    gunContainer = containers.EnsureContainer<ContainerSlot>(weapon, "underbarrel_gun");
                }
                else
                {
                    Assert.That(containers.Remove(secondary, gunContainer, force: true), Is.True);
                }
                Assert.That(containers.Insert(unrelated, gunContainer), Is.True);

                // Even a direct code change bypassing read-only VV must not redirect cleanup.
                alternate.Container = "gun_magazine";
                alternate.AmmoContainer = "gun_chamber";
                SEntMan.RemoveComponent<AltWeaponryComponent>(weapon);
                Assert.That(SEntMan.Deleted(primaryMagazine), Is.False);
                Assert.That(primaryContainer.ContainedEntities, Does.Contain(primaryMagazine));
                Assert.That(SEntMan.Deleted(grenade), Is.False);
                Assert.That(SEntMan.Deleted(unrelated), Is.False);
                Assert.That(containers.TryGetContainer(weapon, "grenade_chamber", out _), Is.False);
                if (replaceContainer)
                {
                    Assert.That(containers.GetContainer(weapon, "underbarrel_gun"), Is.SameAs(gunContainer));
                    Assert.That(gunContainer.ContainedEntities, Does.Contain(unrelated));
                }
                else
                {
                    Assert.That(containers.TryGetContainer(weapon, "underbarrel_gun", out _), Is.False);
                    SEntMan.DeleteEntity(secondary);
                }
                SEntMan.DeleteEntity(weapon);
            }
        });
    }

    [Test]
    public async Task SavedWeaponRetainsOwnership()
    {
        await Server.WaitAssertion(() =>
        {
            var containers = SEntMan.System<SharedContainerSystem>();
            var loader = SEntMan.System<MapLoaderSystem>();
            var coordinates = SEntMan.GetCoordinates(PlayerCoords);
            var weapon = SEntMan.SpawnEntity("WeaponRifleM90GrenadeLauncher", coordinates);
            var grenade = SEntMan.SpawnEntity("Grenade40mmFrag", coordinates);
            Assert.That(SEntMan.System<ItemSlotsSystem>().TryInsert(weapon, "grenade_chamber", grenade, null), Is.True);
            using var writer = new StringWriter();
            Assert.That(loader.TrySaveEntity(weapon, writer), Is.True);
            SEntMan.DeleteEntity(weapon);
            using var reader = new StringReader(writer.ToString());
            Assert.That(loader.TryLoadEntity(reader, "alt-weaponry-ownership", out var loaded), Is.True);
            weapon = loaded!.Value.Owner;
            Transform.SetCoordinates(weapon, coordinates);
            var alternate = SEntMan.GetComponent<AltWeaponryComponent>(weapon);
            Assert.That(alternate.OwnsContainers, Is.True);
            Assert.That(alternate.InitializationError, Is.Null);
            Assert.That(alternate.OwnedGunContainer!.ContainedEntities, Has.Count.EqualTo(1));
            Assert.That(alternate.OwnedGunContainer.ContainedEntity, Is.EqualTo(alternate.SecondaryGun));
            var magazine = containers.GetContainer(weapon, "gun_magazine").ContainedEntities.Single();
            grenade = containers.GetContainer(weapon, "grenade_chamber").ContainedEntities.Single();
            var secondary = alternate.SecondaryGun!.Value;
            SEntMan.RemoveComponent<AltWeaponryComponent>(weapon);
            Assert.That(SEntMan.Deleted(secondary), Is.True);
            Assert.That(SEntMan.Deleted(magazine), Is.False);
            Assert.That(SEntMan.Deleted(grenade), Is.False);
            Assert.That(containers.TryGetContainer(weapon, "underbarrel_gun", out _), Is.False);
            Assert.That(containers.TryGetContainer(weapon, "grenade_chamber", out _), Is.False);
        });
    }
}
