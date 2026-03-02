using Content.Shared.Inventory.Events;

namespace Content.Shared.Starlight.NPC;

/// <summary>
/// Prevents anyone — including other players — from unequipping clothing that has
/// <see cref="NpcBoundClothingComponent"/>. This is used to stop players looting
/// shoes (or other gear) off NPC mobs.
/// </summary>
public sealed class NpcBoundClothingSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NpcBoundClothingComponent, BeingUnequippedAttemptEvent>(OnUnequip);
    }

    private void OnUnequip(Entity<NpcBoundClothingComponent> ent, ref BeingUnequippedAttemptEvent args)
    {
        args.Cancel();
    }
}
