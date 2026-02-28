using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Starlight.Weapons.MinigunCharge;

/// <summary>
/// Makes a gun require a spin-up DoAfter before it can fire.
/// The gun stays charged for <see cref="ChargedDuration"/> seconds after spin-up completes.
/// </summary>
[RegisterComponent]
public sealed partial class MinigunChargeComponent : Component
{
    /// <summary>
    /// How long (in seconds) the spin-up DoAfter takes.
    /// </summary>
    [DataField]
    public float ChargeTime = 2.0f;

    /// <summary>
    /// How many seconds the gun stays "hot" after spinning up before needing to spin up again.
    /// </summary>
    [DataField]
    public float ChargedDuration = 8.0f;

    /// <summary>
    /// Sound played while the minigun is spinning up.
    /// </summary>
    [DataField]
    public SoundSpecifier ChargeSound = new SoundPathSpecifier("/Audio/Machines/spinning.ogg");

    /// <summary>
    /// Time at which the charge expires. Set by the system after a successful spin-up.
    /// </summary>
    public TimeSpan NextReadyTime = TimeSpan.Zero;

    /// <summary>
    /// Whether a spin-up DoAfter is currently in progress.
    /// </summary>
    public bool IsCharging = false;
}
