using Robust.Shared.GameStates;

namespace Content.Shared.Damage.Components;

/// <summary>
/// Marks an entity as having a slime-like biology that converts toxin damage (Poison/Radiation) into healing.
/// The healing amount is tiered based on the toxin damage:
/// - Low toxin (0-5 damage): heals brute damage
/// - Medium toxin (5-10 damage): heals brute and burn damage  
/// - High toxin (10+ damage): heals all damage types proportionally (omnizine-like)
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SlimeAntitoxinComponent : Component
{
}
