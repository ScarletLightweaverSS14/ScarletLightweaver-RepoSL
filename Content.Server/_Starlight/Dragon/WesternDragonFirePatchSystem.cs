using Content.Shared._Starlight.Dragon;
using Content.Shared.Atmos;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Starlight.Dragon;

public sealed partial class WesternDragonFirePatchSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WesternDragonFirePatchComponent, ExtinguishedEvent>(OnExtinguished);
    }

    private void OnExtinguished(Entity<WesternDragonFirePatchComponent> ent, ref ExtinguishedEvent args)
    {
        QueueDel(ent.Owner);
    }

    /// <summary>Shared by the breath and projectile trail; at most one lingering fire per open floor tile.</summary>
    public bool TrySpawnFire(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        if (_turf.IsSpace(_map.GetTileRef(gridUid, grid, tile)) ||
            _turf.IsTileBlocked(gridUid, tile, CollisionGroup.Impassable | CollisionGroup.InteractImpassable, grid))
            return false;

        foreach (var anchored in _map.GetAnchoredEntities(gridUid, grid, tile))
        {
            if (MetaData(anchored).EntityPrototype?.ID == "WesternDragonFirePatch")
                return false;
        }

        Spawn("WesternDragonFirePatch", _map.ToCenterCoordinates(gridUid, tile, grid));
        return true;
    }
}
