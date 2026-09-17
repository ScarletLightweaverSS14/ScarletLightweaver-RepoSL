using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Starlight.Dragon;

public sealed partial class WesternDragonFireTrailSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private WesternDragonFirePatchSystem _fire = default!;

    private readonly HashSet<(EntityUid Grid, Vector2i Tile)> _pending = [];

    public override void Initialize()
    {
        SubscribeLocalEvent<WesternDragonFireTrailComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WesternDragonFireTrailComponent, MoveEvent>(OnMove);
    }

    private void OnMapInit(Entity<WesternDragonFireTrailComponent> ent, ref MapInitEvent args) => QueueTile(ent);
    private void OnMove(Entity<WesternDragonFireTrailComponent> ent, ref MoveEvent args) => QueueTile(ent);

    private void QueueTile(Entity<WesternDragonFireTrailComponent> ent)
    {
        var xform = Transform(ent);
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            ent.Comp.LastTile = null;
            return;
        }

        var tile = _map.GetTileRef(gridUid, grid, xform.Coordinates).GridIndices;
        if (ent.Comp.LastTile == (gridUid, tile))
            return;

        ent.Comp.LastTile = (gridUid, tile);
        // Defer spawning until outside physics/movement callbacks.
        _pending.Add((gridUid, tile));
    }

    public override void Update(float frameTime)
    {
        foreach (var (gridUid, tile) in _pending)
        {
            if (TryComp<MapGridComponent>(gridUid, out var grid))
                _fire.TrySpawnFire(gridUid, grid, tile);
        }
        _pending.Clear();
    }
}
