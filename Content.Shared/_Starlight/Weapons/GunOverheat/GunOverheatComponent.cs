using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Weapons.GunOverheat;

/// <summary>
/// Tracks heat buildup from sustained rapid fire.
/// When heat reaches <see cref="MaxHeat"/> the gun overheats and becomes
/// unable to fire for <see cref="OverheatDuration"/> seconds.
/// Heat dissipates passively at <see cref="CooldownRate"/> per second once
/// firing stops (or after the overheat lockout expires).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GunOverheatComponent : Component
{
    /// <summary>
    /// Current heat level. Ranges from 0 to <see cref="MaxHeat"/>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Heat = 0f;

    /// <summary>
    /// Heat threshold at which the gun overheats and locks out firing.
    /// </summary>
    [DataField]
    public float MaxHeat = 100f;

    /// <summary>
    /// Heat added per projectile fired. For single-shot weapons one trigger pull
    /// adds exactly this amount. Burst and automatic weapons accumulate faster.
    /// </summary>
    [DataField]
    public float HeatPerShot = 7f;

    /// <summary>
    /// Heat dissipated per second while the gun is not overheated.
    /// </summary>
    [DataField]
    public float CooldownRate = 18f;

    /// <summary>
    /// How many seconds the gun stays locked out after overheating before cooling
    /// begins.
    /// </summary>
    [DataField]
    public float OverheatDuration = 4f;

    /// <summary>
    /// Whether the gun is currently in an overheat lockout (cannot fire).
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsOverheated = false;

    /// <summary>
    /// Game-time at which the overheat lockout expires and passive cooling starts.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan OverheatEndsAt = TimeSpan.Zero;
}
