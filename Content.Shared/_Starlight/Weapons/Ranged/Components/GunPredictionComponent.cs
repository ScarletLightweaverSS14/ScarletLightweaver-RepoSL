using Content.Shared.Starlight.Utility;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.Weapons.Ranged.Components;

/// <summary>
/// When present on a gun entity, the client will predict a visible bullet visual
/// immediately upon firing without waiting for the server's confirmed shot.
/// This gives the illusion of zero-latency shooting for projectile weapons.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class GunPredictionComponent : Component
{
    /// <summary>
    /// The sprite to render for the predicted bullet travel animation.
    /// Uses the same format as <see cref="Content.Shared.Weapons.Hitscan.Components.HitscanBasicVisualsComponent.Bullet"/>.
    /// </summary>
    [DataField(required: true)]
    public ExtendedSpriteSpecifier Bullet = default!;

    /// <summary>
    /// Physical projectile speed in tiles/second. Should match the gun's actual ProjectileSpeed
    /// so the predicted visual travel time matches the real bullet.
    /// Defaults to <see cref="Content.Shared.Weapons.Ranged.Systems.SharedGunSystem.ProjectileSpeed"/> (40 t/s).
    /// </summary>
    [DataField]
    public float BulletSpeed = 40f;

    /// <summary>
    /// Maximum render distance for the predicted bullet. Beyond this the visual is skipped.
    /// </summary>
    [DataField]
    public float MaxRenderDistance = 20f;
}
