using System.Numerics;

namespace Content.Shared._Starlight.Weapons.Gunnery;

/// <summary>
/// A simple marker that lets the gunnery console identify shuttle-mounted guns
/// on the shuttle's grid. Add this component to any gun entity prototype that
/// should appear as a cannon blip in the gunnery console radar.
/// </summary>
[RegisterComponent]
public sealed partial class GunneryTrackableComponent : Component { }

/// <summary>
/// Marks a projectile as remotely guidable via a <see cref="GunneryConsoleComponent"/>.
/// The server's <c>GuidedProjectileSystem</c> steers the projectile's linear
/// velocity toward <see cref="SteeringTarget"/> each tick, limited by
/// <see cref="TurnRate"/>.
/// </summary>
[RegisterComponent]
public sealed partial class GuidedProjectileComponent : Component
{
    /// <summary>
    /// Maximum angular steering rate in degrees per second.
    /// Higher values allow tighter turns.
    /// </summary>
    [DataField]
    public float TurnRate = 180f;

    /// <summary>
    /// If true, the projectile automatically seeks a target without player input.
    /// Used for HEAT missiles. Will target <see cref="SeekingTarget"/> if set,
    /// otherwise locks onto the nearest valid ship grid after launch.
    /// </summary>
    [DataField]
    public bool AutoSeek = false;

    /// <summary>
    /// Detection range in tiles for auto-seek to find targets and flare decoys.
    /// </summary>
    [DataField]
    public float AutoSeekRange = 100f;

    /// <summary>
    /// The ship grid entity this HEAT missile is locked onto.
    /// Set at launch time by <c>GunneryConsoleSystem</c>.
    /// If null, falls back to nearest valid grid.
    /// </summary>
    public EntityUid? SeekingTarget;

    // ── Server-only runtime state ───────────────────────────────────────────

    /// <summary>The gunnery console entity that is currently guiding this projectile.</summary>
    public EntityUid? Controller;

    /// <summary>
    /// Map-space target position that the projectile should steer toward.
    /// Updated every tick by <c>GunneryConsoleSystem</c> when the player holds LMB,
    /// or by <c>GuidedProjectileSystem</c> for auto-seeking missiles.
    /// </summary>
    public Vector2 SteeringTarget;

    /// <summary>Whether active guidance is currently being applied this frame.</summary>
    public bool Active;

    /// <summary>
    /// The grid the launcher was mounted on when this missile was fired.
    /// Auto-seek will never home into this grid.
    /// </summary>
    public EntityUid? SourceGrid;
}
