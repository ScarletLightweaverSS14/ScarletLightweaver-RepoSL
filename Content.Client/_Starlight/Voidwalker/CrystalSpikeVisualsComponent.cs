using System.Numerics;

namespace Content.Client._Starlight.Voidwalker;

/// <summary>Animates the existing crystal sprite growing up out of the floor.</summary>
[RegisterComponent]
public sealed partial class CrystalSpikeVisualsComponent : Component
{
    [DataField]
    public float RiseTime = 0.12f;

    /// <summary>Starting vertical displacement, tuned to keep taller crystals rooted in the floor.</summary>
    [DataField]
    public float RiseOffset = 0.35f;

    /// <summary>Scale multiplier at the peak of the eruption, before settling to the prototype scale.</summary>
    [DataField]
    public Vector2 OvershootScale = Vector2.One;

    [DataField]
    public float SettleTime;
}
