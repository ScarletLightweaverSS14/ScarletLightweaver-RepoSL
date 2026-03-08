using Content.Shared.FixedPoint;

namespace Content.Server._Starlight.Salvage;

/// <summary>
/// When added to a crate, causes an acid spill (destroying the cargo sell value)
/// the first time the crate is successfully opened.
/// </summary>
[RegisterComponent]
public sealed partial class AcidTrapCrateComponent : Component
{
    /// <summary>
    /// Reagent ID to spill. Defaults to sulfuric acid.
    /// </summary>
    [DataField]
    public string AcidReagent = "SulfuricAcid";

    /// <summary>
    /// How much acid (in units) to spill.
    /// </summary>
    [DataField]
    public FixedPoint2 AcidVolume = FixedPoint2.New(120);

    /// <summary>
    /// Probability (0–1) that the trap actually fires when the crate is opened.
    /// Defaults to 0.5 (50% chance).
    /// </summary>
    [DataField]
    public float TriggerChance = 0.5f;
}
