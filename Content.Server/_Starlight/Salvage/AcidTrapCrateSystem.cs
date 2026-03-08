using Content.Server.Cargo.Components;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Popups;
using Content.Shared.Storage.Components;
using Robust.Shared.Random;

namespace Content.Server._Starlight.Salvage;

/// <summary>
/// Handles <see cref="AcidTrapCrateComponent"/>:
/// the first time the crate is opened it spills a large acid puddle and
/// destroys the crate's cargo sell value — punishing salvagers who open
/// USSP/SFF supply caches instead of bringing them back intact.
/// </summary>
public sealed class AcidTrapCrateSystem : EntitySystem
{
    [Dependency] private readonly PuddleSystem _puddle = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AcidTrapCrateComponent, StorageAfterOpenEvent>(OnOpened);
    }

    private void OnOpened(EntityUid uid, AcidTrapCrateComponent component, ref StorageAfterOpenEvent args)
    {
        // Remove first — so this only ever fires once per crate
        RemComp<AcidTrapCrateComponent>(uid);

        // Roll the chance — if it doesn't fire, the crate is safe
        if (!_random.Prob(component.TriggerChance))
            return;

        // Spill acid at the crate's location
        var solution = new Solution();
        solution.AddReagent(component.AcidReagent, component.AcidVolume);
        _puddle.TrySpillAt(uid, solution, out _);

        // Destroy the cargo sell value
        if (TryComp<StaticPriceComponent>(uid, out var price))
            price.Price = 0;

        // Warn everybody nearby
        _popup.PopupEntity(
            Loc.GetString("acid-trap-crate-triggered"),
            uid,
            PopupType.LargeCaution);
    }
}
