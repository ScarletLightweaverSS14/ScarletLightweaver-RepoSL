using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Starlight.Weapons.ArtilleryBeacon;

/// <summary>
/// Data component for the NEMESIS-IV orbital strike designator.
/// Once armed it fires <see cref="NumberOfStrikes"/> explosions scattered within
/// <see cref="StrikeRadius"/> tiles of the beacon, spaced by <see cref="StrikeInterval"/> seconds.
/// </summary>
[RegisterComponent]
public sealed partial class ArtilleryBeaconComponent : Component
{
    // ── Config ────────────────────────────────────────────────────────────────

    /// <summary>How long (s) before the first impact after arming.</summary>
    [DataField]
    public float CountdownSeconds = 30f;

    /// <summary>Number of individual kinetic impactors.</summary>
    [DataField]
    public int NumberOfStrikes = 5;

    /// <summary>Radius (tiles) around the beacon within which strikes are scattered.</summary>
    [DataField]
    public float StrikeRadius = 6f;

    /// <summary>Time (s) between successive impacts.</summary>
    [DataField]
    public float StrikeInterval = 1.5f;

    // ── Explosion params (passed to ExplosionSystem) ──────────────────────────

    [DataField]
    public string ExplosionType = "DemolitionCharge";

    [DataField]
    public float TotalIntensity = 280f;

    [DataField]
    public float Slope = 5f;

    [DataField]
    public float MaxTileIntensity = 25f;

    // ── Runtime state (not saved) ─────────────────────────────────────────────

    [DataField]
    public bool IsArmed;

    [DataField]
    public TimeSpan FirstStrikeTime;

    [DataField]
    public TimeSpan NextStrikeTime;

    [DataField]
    public int StrikesRemaining;
}
