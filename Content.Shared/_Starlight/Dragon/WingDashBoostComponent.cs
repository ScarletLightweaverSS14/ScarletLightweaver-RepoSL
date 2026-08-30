namespace Content.Shared._Starlight.Dragon;

[RegisterComponent]
public sealed partial class WingDashBoostComponent : Component
{
    public float TimeRemaining;
    public float SpeedMultiplier = 7f;
}
