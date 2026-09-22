using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    /// <summary>
    /// Fires a contained secondary gun while respecting the held weapon's firing restrictions.
    /// Its ContainerAmmoProvider reads the alternate weapon's ammo slot on the held weapon.
    /// </summary>
    public bool TryShootAlternate(EntityUid user, EntityUid weapon, EntityUid alternate, string ammoContainer, EntityCoordinates coordinates)
    {
        if (!_actionBlockerSystem.CanAttack(user) ||
            !TryComp<GunComponent>(weapon, out var mainGun) ||
            !TryComp<GunComponent>(alternate, out var gun) ||
            !TryComp<ContainerAmmoProviderComponent>(alternate, out var provider) ||
            !CanShoot(gun))
        {
            return false;
        }

        // Apply the held weapon's restrictions, including its firing pin and wield requirement.
        var shotAttempt = new ShotAttemptedEvent { User = user, Used = (weapon, mainGun) };
        RaiseLocalEvent(weapon, ref shotAttempt);
        if (shotAttempt.Cancelled)
            return false;

        var attempt = new AttemptShootEvent(user, null);
        RaiseLocalEvent(weapon, ref attempt);
        if (attempt.Cancelled)
        {
            if (attempt.Message != null)
                PopupSystem.PopupClient(attempt.Message, weapon, user);
            return false;
        }

        // Cartridges supply the projectile; the normal pipeline handles casing ejection and spread.
        provider.ProviderUid = weapon;
        provider.Container = ammoContainer;
        return AttemptShoot(user, (alternate, gun), coordinates);
    }
}
