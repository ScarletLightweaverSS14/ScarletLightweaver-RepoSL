using Content.Shared.Shuttles.BUIStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.Weapons.Gunnery;

/// <summary>
/// Broad ammo category for a cannon, used by the gunnery console filter tabs.
/// </summary>
[Serializable, NetSerializable]
public enum CannonAmmoCategory : byte
{
    Ballistic,  // magazine-fed solid/HE projectiles (20mm, 75mm, 90mm, 120mm, 280mm, etc.)
    Rocket,     // rocket/missile launchers (Vanyk 60mm, Vespera 50mm)
    Energy,     // battery/power-cage weapons (Apollo, Scylla, Dynamre)
    Grenade,    // grenade launchers (Friendship, Tarnyx)
}

/// <summary>
/// Full BUI state sent from server to client for the gunnery console.
/// Wraps the standard <see cref="NavInterfaceState"/> radar data and adds
/// a list of cannon positions and guided-projectile tracking.
/// </summary>
[Serializable, NetSerializable]
public sealed class GunneryConsoleBoundUserInterfaceState : BoundUserInterfaceState
{
    /// <summary>Standard radar state (grids, docks, blips, laser traces).</summary>
    public readonly NavInterfaceState NavState;

    /// <summary>
    /// Positions and identities of all shuttle-mounted cannons on this grid
    /// that are visible to this console.
    /// </summary>
    public readonly List<CannonBlipData> Cannons;

    /// <summary>
    /// Network entity of the guided projectile currently being tracked by this
    /// console, or <c>null</c> if no guidance is active.
    /// </summary>
    public readonly NetEntity? TrackedGuidedProjectile;
    
    public readonly bool HasServer = true;

    /// <summary>
    /// True when at least one HEAT missile is currently locked onto this console's grid.
    /// The client plays an alarm and shows a warning banner when this is set.
    /// </summary>
    public readonly bool IncomingMissile;

    public GunneryConsoleBoundUserInterfaceState(
        NavInterfaceState navState,
        List<CannonBlipData> cannons,
        NetEntity? trackedGuidedProjectile,
        bool hasServer = true,
        bool incomingMissile = false)
    {
        NavState       = navState;
        Cannons        = cannons;
        TrackedGuidedProjectile = trackedGuidedProjectile;
        HasServer      = hasServer;
        IncomingMissile = incomingMissile;
    }
}

/// <summary>
/// Represents a shuttle-mounted cannon on the gunnery radar.
/// </summary>
[Serializable, NetSerializable]
public readonly struct CannonBlipData
{
    /// <summary>Entity-space coordinates of the cannon (same grid as the console).</summary>
    public readonly NetCoordinates Coordinates;

    /// <summary>Network entity identifier — sent back in fire messages.</summary>
    public readonly NetEntity Entity;

    /// <summary>Display name shown in the cannon list.</summary>
    public readonly string Name;

    /// <summary>Remaining cooldown in seconds; 0 when the cannon is ready to fire.</summary>
    public readonly float CooldownSeconds;

    /// <summary>
    /// Broad ammo category used by the filter tabs in the gunnery console sidebar.
    /// </summary>
    public readonly CannonAmmoCategory AmmoCategory;

    public CannonBlipData(NetCoordinates coordinates, NetEntity entity, string name, float cooldownSeconds = 0f, CannonAmmoCategory ammoCategory = CannonAmmoCategory.Ballistic)
    {
        Coordinates     = coordinates;
        Entity          = entity;
        Name            = name;
        CooldownSeconds = cooldownSeconds;
        AmmoCategory    = ammoCategory;
    }
}
