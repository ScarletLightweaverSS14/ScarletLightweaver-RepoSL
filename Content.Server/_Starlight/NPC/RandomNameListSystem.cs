using Content.Shared.Dataset;
using Content.Shared.Random.Helpers;
using Content.Shared.Starlight.NPC;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Starlight.NPC;

public sealed class RandomNameListSystem : EntitySystem
{
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _protoManager = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RandomNameListComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(EntityUid uid, RandomNameListComponent component, MapInitEvent args)
    {
        string name;

        if (component.DatasetId is { } datasetId &&
            _protoManager.TryIndex<LocalizedDatasetPrototype>(datasetId, out var dataset))
        {
            // Pick a localized name from the FTL-backed dataset.
            name = _random.Pick(dataset);
        }
        else if (component.Names.Count > 0)
        {
            // Fall back to the inline name list.
            name = _random.Pick(component.Names);
        }
        else
        {
            return;
        }

        if (!string.IsNullOrEmpty(component.Prefix))
            name = $"{component.Prefix} {name}";

        _metaData.SetEntityName(uid, name);
    }
}
