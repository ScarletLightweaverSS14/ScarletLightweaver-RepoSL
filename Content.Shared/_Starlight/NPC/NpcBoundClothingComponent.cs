using Robust.Shared.GameStates;

namespace Content.Shared.Starlight.NPC;

/// <summary>
/// When applied to a clothing item, prevents ANY actor (including players) from unequipping it.
/// Used to permanently bind shoes and other gear to NPC mobs so players cannot loot them.
/// </summary>
[RegisterComponent]
[NetworkedComponent]
public sealed partial class NpcBoundClothingComponent : Component { }
