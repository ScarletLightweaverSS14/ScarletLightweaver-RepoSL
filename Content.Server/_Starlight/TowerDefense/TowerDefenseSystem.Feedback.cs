using Content.Shared._Starlight.TowerDefense;
using Content.Shared._Starlight.Weapons.Hitscan.Events;
using Content.Shared.Effects;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.TowerDefense;

public sealed partial class TowerDefenseSystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private SharedColorFlashEffectSystem _colorFlash = default!;

    private static readonly SoundSpecifier BulletHitSound =
        new SoundPathSpecifier("/Audio/Weapons/Guns/Hits/bullet_hit.ogg");
    private static readonly SoundSpecifier MeleeHitSound = new SoundCollectionSpecifier("WeakHit");

    /// <summary>
    /// Reuses normal weapon feedback without firing a damaging projectile or performing a combat attack.
    /// Damage and rewards remain part of the arena simulation.
    /// </summary>
    private void PlayAttackFeedback(EntityUid tower, EntityUid weapon, EntityUid target, TowerDefenseTowerLevel level)
    {
        if (TerminatingOrDeleted(weapon))
            return;

        var origin = Transform(tower).Coordinates;
        // Keep effects relative to the arena grid, so a lethal hit can remove the enemy immediately.
        var impact = _transform.ToCoordinates(origin.EntityId, _transform.ToMapCoordinates(Transform(target).Coordinates));
        var direction = impact.Position - origin.Position;
        var distance = direction.Length();
        if (distance <= 0)
            return;

        var filter = Filter.Pvs(origin, entityMan: EntityManager)
            .Merge(Filter.Pvs(impact, entityMan: EntityManager));
        if (TryComp<TowerDefenseEnemyComponent>(target, out var enemy) && enemy.Health > level.Damage)
            _colorFlash.RaiseEffect(Color.Red, new List<EntityUid> { target }, filter);

        if (level.ShotVisuals is { } shotVisuals && TryComp<GunComponent>(weapon, out var gun) &&
            _prototypes.Index(shotVisuals).TryComp<HitscanBasicVisualsComponent>(out var visuals, EntityManager.ComponentFactory))
        {
            var sound = gun.SoundGunshotModified ?? gun.SoundGunshot;
            _audio.PlayPvs(sound, origin, sound?.Params.WithVolume(-8f));
            _audio.PlayPvs(BulletHitSound, impact, BulletHitSound.Params.WithVolume(-12f).WithVariation(0.05f));

            // The ordinary muzzle event also supplies the short orange light pulse.
            var worldAngle = direction.ToAngle() + _transform.GetWorldRotation(origin.EntityId);
            RaiseNetworkEvent(new MuzzleFlashEvent(GetNetEntity(weapon), "MuzzleFlashEffect", worldAngle), filter);

            // These native cosmetic entities despawn themselves and contain no projectile or physics components.
            var effect = Spawn("BulletImpactEffect", impact);
            _transform.SetLocalRotation(effect, direction.ToAngle());

            RaiseNetworkEvent(new SharedGunSystem.HitscanEvent
            {
                // Muzzle and impact are separate effects so disabling tracers still leaves attack feedback.
                MuzzleFlash = null,
                ImpactFlash = null,
                TravelFlash = visuals.TravelFlash,
                Bullet = distance > 1.5f ? visuals.Bullet : null,
                Speed = visuals.Speed,
                Shooter = GetNetEntity(tower),
                Gun = GetNetEntity(weapon),
                Traces = new List<HitscanTrace>
                {
                    new()
                    {
                        Angle = direction.ToAngle(),
                        Distance = distance,
                        MuzzleCoordinates = GetNetCoordinates(origin.Offset(direction / distance * 0.5f)),
                        TravelCoordinates = distance > 1.5f
                            ? GetNetCoordinates(origin.Offset(direction / distance * (distance + 0.5f) / 2f))
                            : null,
                        ImpactCoordinates = GetNetCoordinates(impact),
                        // A deleted enemy must not be needed to render the shot.
                        ImpactedEnt = null,
                    },
                },
            }, filter);
            return;
        }

        if (!TryComp<MeleeWeaponComponent>(weapon, out var melee))
            return;

        _audio.PlayPvs(melee.SwingSound, origin, melee.SwingSound.Params.WithVolume(-10f));
        var hitSound = melee.HitSound ?? MeleeHitSound;
        _audio.PlayPvs(hitSound, impact, hitSound.Params.WithVolume(-8f).WithVariation(0.05f));
        _melee.DoLunge(tower, weapon, melee.Angle, direction, melee.Animation.Id, predicted: false);
        var contact = Spawn("WeaponArcSmash", impact);
        _transform.SetLocalRotation(contact, direction.ToWorldAngle());
    }
}
