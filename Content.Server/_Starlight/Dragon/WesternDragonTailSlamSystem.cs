using Content.Shared._Starlight.Dragon;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Dragon;

public sealed partial class WesternDragonTailSlamSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private Robust.Server.Audio.AudioSystem _audio = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WesternDragonTailSlamComponent, WesternDragonTailSlamEvent>(OnTailSlam);
        SubscribeLocalEvent<WesternDragonFireWaveComponent, MapInitEvent>(OnWaveInit);
    }

    private void OnTailSlam(Entity<WesternDragonTailSlamComponent> ent, ref WesternDragonTailSlamEvent args)
    {
        var xform = Transform(ent);
        if (args.Handled || xform.GridUid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var tile = _map.GetTileRef(gridUid, grid, xform.Coordinates);
        var center = _map.ToCenterCoordinates(gridUid, tile.GridIndices, grid);
        if (_turf.IsSpace(tile))
            return;

        // Lock the wave's origin to the caster's current tile without moving the caster.
        Spawn("WesternDragonTailSlamWave", center);
        _audio.PlayPvs("/Audio/Effects/explosion_small1.ogg", ent);
        args.Handled = true;
    }

    private void OnWaveInit(Entity<WesternDragonFireWaveComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextPulse = _timing.CurTime;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<WesternDragonFireWaveComponent>();
        while (query.MoveNext(out var uid, out var wave))
        {
            if (_timing.CurTime < wave.NextPulse)
                continue;

            var xform = Transform(uid);
            if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            {
                QueueDel(uid);
                continue;
            }

            var origin = _map.GetTileRef(gridUid, grid, xform.Coordinates).GridIndices;
            for (var x = -wave.Radius; x <= wave.Radius; x++)
            for (var y = -wave.Radius; y <= wave.Radius; y++)
            {
                if (Math.Max(Math.Abs(x), Math.Abs(y)) != wave.Radius)
                    continue;

                var tile = origin + new Vector2i(x, y);
                var coordinates = _map.ToCenterCoordinates(gridUid, tile, grid);
                if (_turf.IsSpace(_map.GetTileRef(gridUid, grid, tile)) ||
                    !_interaction.InRangeUnobstructed(uid, coordinates, range: 0))
                    continue;

                var occupied = false;
                foreach (var anchored in _map.GetAnchoredEntities(gridUid, grid, tile))
                {
                    if (MetaData(anchored).EntityPrototype?.ID is "WesternDragonTailSlamWarning" or "WesternDragonTailSlamFire")
                    {
                        occupied = true;
                        break;
                    }
                }

                if (!occupied)
                    Spawn("WesternDragonTailSlamWarning", coordinates);
            }

            wave.Radius++;
            wave.NextPulse = _timing.CurTime + TimeSpan.FromSeconds(0.3);
            if (wave.Radius > 2)
                QueueDel(uid);
        }
    }
}
