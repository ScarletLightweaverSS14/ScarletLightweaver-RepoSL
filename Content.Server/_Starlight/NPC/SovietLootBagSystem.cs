using Content.Shared.Interaction.Events;
using Content.Shared.Starlight.NPC;

namespace Content.Server.Starlight.NPC;

/// <summary>
/// Handles <see cref="SovietLootBagComponent"/>: when a player uses the bag in-hand,
/// all loot items are spawned at the bag's location and the bag deletes itself.
/// </summary>
public sealed class SovietLootBagSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SovietLootBagComponent, UseInHandEvent>(OnUseInHand);
    }

    private void OnUseInHand(EntityUid uid, SovietLootBagComponent comp, UseInHandEvent args)
    {
        if (args.Handled)
            return;

        var coords = _transform.GetMoverCoordinates(uid);

        foreach (var protoId in comp.Loot)
            Spawn(protoId, coords);

        args.Handled = true;
        QueueDel(uid);
    }
}
