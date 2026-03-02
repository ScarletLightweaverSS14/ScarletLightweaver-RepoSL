// 🌟Starlight🌟
using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Silicons.Borgs.BorgFortify;

/// <summary>
/// Placed on a borg module. Tracks the state of the borg's Fortify stance:
/// a breakable damage-absorbing shield brace that roots the borg in place.
/// Managed exclusively by BorgFortifySystem.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BorgFortifyComponent : Component
{
    /// <summary>Maximum shield HP. Restored after weld repair.</summary>
    [DataField]
    public float MaxShieldHp = 200f;

    /// <summary>Current shield HP. Drained by incoming damage while fortified.</summary>
    [DataField, AutoNetworkedField]
    public float CurrentShieldHp = 200f;

    /// <summary>True while the borg has the shield raised.</summary>
    [DataField, AutoNetworkedField]
    public bool Fortified;

    /// <summary>True when shield HP reached 0 — needs welding before Fortify can be used again.</summary>
    [DataField, AutoNetworkedField]
    public bool ShieldBroken;

    /// <summary>
    /// Fraction of each incoming damage point that drains the shield rather than the borg.
    /// 0.5 = the shield absorbs 50% of each hit; the other 50% still hits the borg.
    /// </summary>
    [DataField]
    public float DamageAbsorption = 0.5f;

    /// <summary>Walk speed multiplier while fortified (0 = fully rooted).</summary>
    [DataField]
    public float FortifyWalkModifier = 0f;

    /// <summary>Sprint speed multiplier while fortified (0 = fully rooted).</summary>
    [DataField]
    public float FortifySprintModifier = 0f;

    /// <summary>Welder fuel consumed for a single repair.</summary>
    [DataField]
    public float RepairFuelCost = 10f;

    /// <summary>Seconds the weld repair DoAfter takes.</summary>
    [DataField]
    public float RepairTime = 5f;

    /// <summary>
    /// UID of the spawned shield-visual entity while fortified.
    /// Not networked — server-side bookkeeping only.
    /// </summary>
    public EntityUid? ShieldVisualEntity;
}
