using Robust.Shared.GameObjects;

namespace Content.Server._Starlight.Salvage;

/// <summary>
/// When the storage entity holding this component is first opened, all of its
/// contents are scattered on the ground and the entity is deleted.
/// Used by comrade loot bags so salvagers don't have to manually drag items out.
/// </summary>
[RegisterComponent]
public sealed partial class ScatterContentsOnOpenComponent : Component
{
    /// <summary>Speed (m/s) at which items are thrown outward when scattered.</summary>
    [DataField]
    public float ThrowSpeed { get; set; } = 2f;
}
