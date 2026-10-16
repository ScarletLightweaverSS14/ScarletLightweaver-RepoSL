using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Decals;
using Content.Server.Gravity;
using Content.Shared._Starlight.TowerDefense;
using Content.Shared.Atmos;
using Content.Shared.Gravity;
using Robust.Shared.Map;

namespace Content.Server._Starlight.TowerDefense;

public sealed partial class TowerDefenseSystem
{
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private GravitySystem _gravity = default!;
    [Dependency] private DecalSystem _decals = default!;

    /// <summary>Creates an isolated arena with a control deck and a visible, fixed road.</summary>
    public EntityUid CreateArena(EntityUid player, out MapId mapId)
    {
        var config = _prototypes.Index(FirstWave);
        Validate(config);
        TowerDefenseRoute.TryValidate(config.Route, config.Width, config.Height, out var road);
        // Resolve tile IDs before creating any entities so invalid layout data fails cleanly.
        var buildFloor = new Tile(_tiles[config.BuildFloor].TileId);
        var roadFloor = new Tile(_tiles[config.RoadFloor].TileId);
        var deckFloor = new Tile(_tiles[config.DeckFloor].TileId);
        var map = _maps.CreateMap(out mapId);
        _maps.SetAmbientLight(mapId, Color.White);
        var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
        air.SetMoles(Gas.Oxygen, 21.824f);
        air.SetMoles(Gas.Nitrogen, 82.103f);
        _atmos.SetMapAtmosphere(map, false, air);
        var grid = _maps.CreateGridEntity(mapId);
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = -1; x <= config.Width; x++)
        for (var y = -3; y <= config.Height; y++)
        {
            var tile = new Vector2i(x, y);
            var inBoard = x >= 0 && x < config.Width && y >= 0 && y < config.Height;
            tiles.Add((tile, road.Contains(tile) ? roadFloor : inBoard ? buildFloor : deckFloor));
        }
        _maps.SetTiles(grid.Owner, grid.Comp, tiles);
        var gravity = EnsureComp<GravityComponent>(grid.Owner);
        _gravity.EnableGravity(grid.Owner);
        gravity.Inherent = true;
        Dirty(grid.Owner, gravity);
        DecorateArena(grid.Owner, config);

        var center = MathF.Floor(config.Width / 2f) + 0.5f;
        var console = Spawn("TowerDefenseConsole", new EntityCoordinates(grid.Owner, new Vector2(center, -1.5f)));
        var arena = Comp<TowerDefenseArenaComponent>(console);
        arena.Grid = grid.Owner;
        arena.Road = road;
        Reset(arena, config);
        _transform.SetCoordinates(player, new EntityCoordinates(grid.Owner, new Vector2(center, -2.5f)));
        Publish(console, arena, config);
        _ui.TryOpenUi(console, TowerDefenseUiKey.Key, player);
        return console;
    }

    private void DecorateArena(EntityUid grid, TowerDefenseArenaPrototype config)
    {
        // Railings sit outside all purchasable tiles, enclosing the arena and control deck.
        for (var x = 0; x < config.Width; x++)
        {
            Railing(new Vector2i(x, -3), Angle.Zero);
            Railing(new Vector2i(x, config.Height), Angle.FromDegrees(180));
        }
        for (var y = -2; y < config.Height; y++)
        {
            Railing(new Vector2i(-1, y), Angle.FromDegrees(-90));
            Railing(new Vector2i(config.Width, y), Angle.FromDegrees(90));
        }
        Railing(new Vector2i(-1, -3), Angle.FromDegrees(-90), true);
        Railing(new Vector2i(config.Width, -3), Angle.Zero, true);
        Railing(new Vector2i(config.Width, config.Height), Angle.FromDegrees(90), true);
        Railing(new Vector2i(-1, config.Height), Angle.FromDegrees(180), true);

        Spawn("TowerDefenseSpawnMarker", new EntityCoordinates(grid, TowerDefenseRoute.Center(config.Route[0])));
        Spawn("TowerDefenseBaseMarker", new EntityCoordinates(grid, TowerDefenseRoute.Center(config.Route[^1])));
        for (var i = 1; i < config.Route.Count; i++)
        {
            var from = config.Route[i - 1];
            var delta = config.Route[i] - from;
            var step = new Vector2i(Math.Sign(delta.X), Math.Sign(delta.Y));
            var length = Math.Abs(delta.X) + Math.Abs(delta.Y);
            for (var distance = 1; distance < length; distance += 2)
            {
                var tile = from + step * distance;
                _decals.TryAddDecal("Arrows", new EntityCoordinates(grid, tile), out _,
                    color: Color.White, rotation: ((Vector2) step).ToWorldAngle());
            }
        }

        void Railing(Vector2i tile, Angle rotation, bool corner = false)
        {
            var uid = Spawn(corner ? "RailingCornerYellow" : "RailingYellow", new EntityCoordinates(grid, TowerDefenseRoute.Center(tile)));
            _transform.SetLocalRotation(uid, rotation);
        }
    }
}
