using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Starlight.NPC;

/// <summary>
/// Gives NPCs flavour voice callouts when they start engaging an enemy,
/// take a significant hit, or enter critical condition.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NpcCalloutComponent : Component
{
    /// <summary>Phrases randomly chosen when the NPC begins combat (gun or melee starts).</summary>
    [DataField]
    public List<string> SpotPhrases = new();

    /// <summary>Phrases randomly chosen when the NPC takes a hit above <see cref="HurtThreshold"/>.</summary>
    [DataField]
    public List<string> HurtPhrases = new();

    /// <summary>Phrases randomly chosen when the NPC enters critical condition.</summary>
    [DataField]
    public List<string> CritPhrases = new();

    /// <summary>Minimum total damage in a single event to trigger a hurt callout.</summary>
    [DataField]
    public float HurtThreshold = 8f;

    /// <summary>Minimum seconds between consecutive spot callouts.</summary>
    [DataField]
    public float SpotCooldown = 18f;

    /// <summary>Minimum seconds between consecutive hurt/crit callouts.</summary>
    [DataField]
    public float HurtCooldown = 7f;

    /// <summary>Game-time after which the next spot callout is allowed. Managed by the system.</summary>
    [DataField]
    public TimeSpan NextSpotCallout = TimeSpan.Zero;

    /// <summary>Game-time after which the next hurt callout is allowed. Managed by the system.</summary>
    [DataField]
    public TimeSpan NextHurtCallout = TimeSpan.Zero;
}
