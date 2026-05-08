using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Weapons.Ranged.Components;

/// <summary>
/// Marks an entity as a prediction test dummy.
/// The <see cref="Content.Client._Starlight.Weapons.Ranged.Systems.PredictionDebugSystem"/>
/// will display a "[DUMMY]" label above it when the prediction debug overlay is enabled.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class PredictionDummyComponent : Component;
