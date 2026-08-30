using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Tag;

namespace Content.Server._Starlight.Dragon;

public sealed partial class WesternDragonFirePatchSystem : EntitySystem
{
    [Dependency] private readonly TagSystem _tag = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<FlammableComponent, ExtinguishedEvent>(OnExtinguished);
    }

    private void OnExtinguished(Entity<FlammableComponent> ent, ref ExtinguishedEvent args)
    {
        if (_tag.HasTag(ent.Owner, "WesternDragonFirePatch"))
            QueueDel(ent.Owner);
    }
}
