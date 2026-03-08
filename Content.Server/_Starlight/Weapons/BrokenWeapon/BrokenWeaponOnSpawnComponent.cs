using Robust.Shared.GameStates;

namespace Content.Server._Starlight.Weapons.BrokenWeapon;

/// <summary>
/// When added to a weapon entity, gives it a probability of becoming non-functional
/// (Gun component removed) on MapInit. Used for Soviet expedition enemy weapon drops.
/// </summary>
[RegisterComponent]
public sealed partial class BrokenWeaponOnSpawnComponent : Component
{
    /// <summary>
    /// Probability (0-1) that this weapon spawns broken and unable to fire.
    /// E.g. 0.8 means 80% chance to be broken.
    /// </summary>
    [DataField]
    public float BrokenChance = 0.80f;

    /// <summary>
    /// Name override to use when the weapon is broken.
    /// If empty, prepends "broken " to the existing name.
    /// </summary>
    [DataField]
    public string BrokenNameOverride = string.Empty;

    /// <summary>
    /// Description override for when the weapon is broken.
    /// </summary>
    [DataField]
    public string BrokenDescriptionOverride = "A damaged weapon with a broken action. It cannot be fired.";
}
