using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.SCP;

/// <summary>
/// Marks an entity as visually "redacted". Observers see a black box over the head
/// (or full body), a scrambled entity name, and glitch-text on examine.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RedactedComponent : Component
{
    /// <summary>If true, the overlay covers the full body; otherwise only the head.</summary>
    [DataField, AutoNetworkedField]
    public bool FullBody = false;

    /// <summary>Original entity name stored server-side before scrambling (not networked).</summary>
    [DataField]
    public string OriginalName = string.Empty;

    /// <summary>Countdown in seconds until the next name scramble.</summary>
    [DataField]
    public float NameTimer = 0f;
}
