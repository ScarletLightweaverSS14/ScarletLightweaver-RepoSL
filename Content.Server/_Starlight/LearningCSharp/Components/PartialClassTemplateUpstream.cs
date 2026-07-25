namespace Content.Server._Starlight.LearningCSharp.Components;

// -----------------------------------------------------------------------------
// Learning Example
//
// Imagine this file belongs to the upstream SS14 repository.
//
// As a downstream developer, you generally avoid modifying this file directly.
// Instead, you extend the same class using another file marked with the
// `partial` keyword.
// -----------------------------------------------------------------------------

/// <summary>
/// Upstream portion of the PartialClassComponentTemplate.
/// </summary>
[RegisterComponent]
public sealed partial class PartialClassComponentTemplateComponent : Component
{
    /// <summary>
    /// This field represents data that already exists in the upstream Component.
    /// </summary>
    [DataField]
    public string UpstreamName = "Upstream";
}
