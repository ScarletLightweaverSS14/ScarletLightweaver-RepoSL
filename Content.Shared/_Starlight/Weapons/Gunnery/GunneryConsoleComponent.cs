using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.Weapons.Gunnery;

/// <summary>
/// Marks an entity as a gunnery console — a targeting radar that can remotely aim and fire
/// shuttle-mounted cannons and guide EMP rockets.
/// </summary>
[RegisterComponent]
public sealed partial class GunneryConsoleComponent : Component
{
    // ── Server-only runtime state ──────────────────────────────────────────
    // These are never serialized; they are set each frame by GunneryConsoleSystem.

    /// <summary>Server: all guided projectiles currently being tracked by this console (multi-rocket support).</summary>
    public List<EntityUid> TrackedGuidedProjectiles = new();

    /// <summary>Server: game time at which the last fire command was sent (used to associate spawned guided projectiles).</summary>
    public TimeSpan LastFireTime;

    /// <summary>Server: map-space position of the last fire target (used to immediately activate guided projectile steering).</summary>
    public Vector2 LastFireTargetPos;

    /// <summary>Server: grid entity the last fire click landed on (set by the fire message; used to lock HEAT rockets onto a target grid).</summary>
    public EntityUid? LastFireTargetGrid;
}

/// <summary>
/// UI key for <see cref="GunneryConsoleComponent"/>.
/// </summary>
[Serializable, NetSerializable]
public enum GunneryConsoleUiKey : byte
{
    Key,
}
