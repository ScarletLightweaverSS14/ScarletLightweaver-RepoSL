using Content.Shared.FixedPoint;

namespace Content.Server._Starlight.LearningCSharp.Components;
/// <summary>
/// Demonstrates how DataFields allow Components to store configurable data.
/// </summary>
[RegisterComponent]
public sealed partial class DataFieldTemplateComponent : Component
{
    [DataField]
    FixedPoint2 Value = 5.25f;
}
