namespace Content.Shared._Starlight.Weapons.Gunnery;

/// <summary>
/// Marks an entity as an infrared countermeasure decoy.
/// HEAT-seeking missiles (<see cref="GuidedProjectileComponent"/> with <c>AutoSeek = true</c>)
/// will divert to follow the nearest <see cref="FlareDecoyComponent"/> entity within their
/// seek range, allowing players to shoot down or redirect incoming HEAT missiles by firing flares.
/// </summary>
[RegisterComponent]
public sealed partial class FlareDecoyComponent : Component
{
}
