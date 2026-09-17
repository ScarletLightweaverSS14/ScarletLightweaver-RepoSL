using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;

namespace Content.Server._Starlight.Dragon;

public sealed partial class WesternDragonFireImmunitySystem : EntitySystem
{
    [Dependency] private FlammableSystem _flammable = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WesternDragonFireImmunityComponent, IgnitedEvent>(OnIgnited);
    }

    private void OnIgnited(Entity<WesternDragonFireImmunityComponent> ent, ref IgnitedEvent args)
    {
        _flammable.Extinguish(ent);
    }
}
