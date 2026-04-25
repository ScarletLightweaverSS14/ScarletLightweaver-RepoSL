using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.FiringPins.Functionality;

/// <summary>
/// Placed on a firing pin entity to give it crystal-based heat accumulation behavior.
/// Heat only accumulates while firing; cooling is delayed until the player stops shooting.
/// Once overheated, each additional shot consumes one durability point — when durability
/// reaches zero the crystal shatters deterministically (no RNG).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FiringPinCrystalComponent : Component
{
    /// <summary>
    /// When false the crystal does not overheat.
    /// </summary>
    [DataField]
    public bool Enabled = true;

    // ── Heat ──────────────────────────────────────────────────────────────────

    /// <summary>Heat required to trigger overheat. HeatPerShot × MaxHeat = shots before overheat.</summary>
    [DataField]
    public float MaxHeat = 120f;

    /// <summary>Heat added per shot fired.</summary>
    [DataField]
    public float HeatPerShot = 10f;

    /// <summary>Heat dissipated per second (only after <see cref="CooldownDelay"/> seconds of not firing).</summary>
    [DataField]
    public float CooldownRate = 20f;

    /// <summary>Seconds of not firing before the crystal starts to cool.</summary>
    [DataField]
    public float CooldownDelay = 1.5f;

    [AutoNetworkedField]
    public float CurrentHeat = 0f;

    [AutoNetworkedField]
    public bool IsOverheated = false;

    // ── Lockout ───────────────────────────────────────────────────────────────

    /// <summary>How long (seconds) the crystal blocks firing immediately after entering overheat.</summary>
    [DataField]
    public float LockoutDuration = 1.5f;

    [ViewVariables]
    public float OverheatLockoutTimer = 0f;

    /// <summary>Server-only: seconds since the last shot, used to gate cooling.</summary>
    [ViewVariables]
    public float TimeSinceLastShot = float.MaxValue;

    // ── Durability ────────────────────────────────────────────────────────────

    /// <summary>
    /// Total overheated shots the crystal can survive before shattering.
    /// Shattering is deterministic — no random chance involved.
    /// </summary>
    [DataField]
    public int MaxDurability = 5;

    /// <summary>Remaining overheated shots before shattering. Networked for examine display.</summary>
    [AutoNetworkedField]
    public int CurrentDurability = 5;

    // ── Sounds ────────────────────────────────────────────────────────────────

    [DataField]
    public SoundSpecifier OverheatSound = new SoundPathSpecifier("/Audio/Effects/lightburn.ogg");

    [DataField]
    public SoundSpecifier BreakSound = new SoundCollectionSpecifier("GlassBreak");
}
