using Content.Shared.Interaction.Events;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Random;

namespace Content.Server._Starlight.Weapons.BrokenWeapon;

/// <summary>
/// Handles the <see cref="BrokenWeaponOnSpawnComponent"/> logic.
/// On first drop, rolls against <c>BrokenChance</c>; if the roll succeeds the
/// weapon's Gun component is removed so it can no longer be fired.
/// This means enemies carry fully functional weapons but drop potentially broken ones.
/// </summary>
public sealed class BrokenWeaponOnSpawnSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BrokenWeaponOnSpawnComponent, DroppedEvent>(OnDropped);
    }

    private void OnDropped(EntityUid uid, BrokenWeaponOnSpawnComponent component, DroppedEvent args)
    {
        // Remove self first — so this only ever fires once per weapon
        RemComp<BrokenWeaponOnSpawnComponent>(uid);

        if (!_random.Prob(component.BrokenChance))
            return;

        // Remove the ability to fire
        RemComp<GunComponent>(uid);

        // Also remove chamber/magazine ammo provider so there are no leftovers
        RemComp<ChamberMagazineAmmoProviderComponent>(uid);
        RemComp<MagazineAmmoProviderComponent>(uid);
        RemComp<RevolverAmmoProviderComponent>(uid);

        // Update name and description to reflect the broken state
        var meta = MetaData(uid);

        var newName = string.IsNullOrWhiteSpace(component.BrokenNameOverride)
            ? "broken " + meta.EntityName
            : component.BrokenNameOverride;

        _metaData.SetEntityName(uid, newName);
        _metaData.SetEntityDescription(uid, component.BrokenDescriptionOverride);
    }
}
