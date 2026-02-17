namespace Content.Server.GameTicking;

/// <summary>
/// Component that tracks various statistics during the round for display at round end.
/// </summary>
[RegisterComponent]
public sealed partial class RoundEndStatisticsComponent : Component
{
    /// <summary>
    /// Total money earned/lost by cargo across all accounts
    /// </summary>
    [DataField]
    public int TotalCargoMoney;

    /// <summary>
    /// Total damage healed by medical staff/items
    /// </summary>
    [DataField]
    public int TotalDamageHealed;

    /// <summary>
    /// Total science points generated this round
    /// </summary>
    [DataField]
    public int TotalSciencePoints;

    /// <summary>
    /// Highest power supply observed from an SMES
    /// </summary>
    [DataField]
    public float HighestSMESPowerOutput;

    /// <summary>
    /// Number of times the clown was beaten (had 50 or more damage)
    /// </summary>
    [DataField]
    public int ClownBeatenCount;
}
