namespace Content.Server._Starlight.LearningCSharp.Components;
/// <summary>
/// -----------------------------------------------------------------------------
/// Learning Example
///
/// Imagine this file belongs to your downstream project (Starlight).
///
/// Instead of editing the upstream file, we extend the same Component using
/// another partial class.
/// -----------------------------------------------------------------------------
/// </summary>
public sealed partial class PartialClassComponentTemplateComponent : Component
{
/// <summary>
/// This field exists only in the downstream file.
/// It demonstrates how partial classes allow downstream projects to extend the same Component without modifying the upstream file.
/// </summary>
    [DataField]
    public bool StarlightFeatureEnabled = true;
}
