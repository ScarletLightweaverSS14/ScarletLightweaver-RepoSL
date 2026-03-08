using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Throwing;
using Robust.Shared.Containers;
using Robust.Shared.Random;
using Robust.Shared.Map;

namespace Content.Server._Starlight.Salvage;

/// <summary>
/// Handles <see cref="ScatterContentsOnOpenComponent"/>:
/// On the first storage-open event the bag's contents are
/// emptied to the ground, thrown outward, and the bag entity
/// is deleted — giving the same feel as the Dumpable mechanic.
/// </summary>
public sealed class ScatterContentsOnOpenSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ScatterContentsOnOpenComponent, StorageAfterOpenEvent>(OnOpened);
    }

    private void OnOpened(EntityUid uid, ScatterContentsOnOpenComponent comp, ref StorageAfterOpenEvent args)
    {
        if (!TryComp<StorageComponent>(uid, out var storage))
            return;

        // Remove component first so this only ever fires once
        RemComp<ScatterContentsOnOpenComponent>(uid);

        // Drop all items at the bag's current position, then throw them outward
        var coords = Transform(uid).Coordinates;
        var dropped = _container.EmptyContainer(storage.Container, destination: coords);

        foreach (var item in dropped)
        {
            _throwing.TryThrow(item, _random.NextVector2(), comp.ThrowSpeed, doSpin: true);
        }

        QueueDel(uid);
    }
}
