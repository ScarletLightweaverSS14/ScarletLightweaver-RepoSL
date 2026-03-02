using Content.Shared.Dataset;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Starlight.NPC;

/// <summary>
/// Picks a random name on MapInit from either a <see cref="LocalizedDatasetPrototype"/> (preferred)
/// or the inline <see cref="Names"/> list, and optionally prepends a fixed <see cref="Prefix"/>.
/// </summary>
[RegisterComponent]
[NetworkedComponent]
public sealed partial class RandomNameListComponent : Component
{
    /// <summary>
    /// When set, names are drawn from this FTL-backed localized dataset instead of <see cref="Names"/>.
    /// </summary>
    [DataField]
    public ProtoId<LocalizedDatasetPrototype>? DatasetId;

    /// <summary>
    /// Fallback inline list of names (used when <see cref="DatasetId"/> is null).
    /// </summary>
    [DataField]
    public List<string> Names = new();

    /// <summary>
    /// If set, this string is prepended to the chosen name with a space.
    /// Example: Prefix = "Comrade" → "Comrade Ivan Petrov"
    /// </summary>
    [DataField]
    public string? Prefix;
}
