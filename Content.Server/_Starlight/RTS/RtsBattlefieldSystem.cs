using System.Numerics;
using Content.Server._Starlight.RTS.Components;
using Content.Server.Decals;
using Content.Server.Parallax;
using Content.Shared._Starlight.RTS;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Noise;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.RTS;

/// <summary>
/// Materializes a small expedition-style battlefield once, without expedition streaming,
/// station registration, shuttle access, or any changes to ordinary round generation.
/// </summary>
public sealed partial class RtsBattlefieldSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private BiomeSystem _biomes = default!;
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private MetaDataSystem _metadata = default!;

    public const int MinimumSize = 80;
    public const int MaximumSize = 120;
    private static readonly ProtoId<RtsBattlefieldPrototype> Preset = "RtsSkirmish";

    /// <summary>
    /// Generate a repeatable square battlefield. No persistent update loop is needed.
    /// </summary>
    public Entity<RtsBattlefieldComponent> Generate(int seed, int? size = null)
    {
        var settings = _prototypes.Index(Preset);
        var length = size ?? settings.Size;
        if (length < MinimumSize || length > MaximumSize)
            throw new ArgumentOutOfRangeException(nameof(size), $"Battlefield size must be {MinimumSize}–{MaximumSize} tiles.");

        // Validate before creating a map, including the configurable space around each base.
        if (settings.ClearingRadius < 3 || settings.PathHalfWidth < 2 ||
            settings.BaseInset <= settings.ClearingRadius ||
            settings.BaseInset * 2 + settings.ClearingRadius * 2 >= length ||
            settings.StartingUnits is < 1 or > 8)
            throw new InvalidOperationException("RTS battlefield settings leave insufficient space for bases and routes.");

        var terrain = _prototypes.Index(settings.Terrain);
        var vegetation = _prototypes.Index(settings.Vegetation);
        var clearingTile = new Tile(_tiles[settings.ClearingTile].TileId);
        var pathTile = new Tile(_tiles[settings.PathTile].TileId);
        var map = _maps.CreateMap(out var mapId, runMapInit: false);

        try
        {
            // Reuse the expedition planet environment, then remove streaming altogether.
            // Enabled=false alone still allows shared biome queries to synthesize off-map tiles.
            _biomes.EnsurePlanet(map, terrain, seed, mapLight: Color.FromHex("#E6CB8B"));
            RemComp<BiomeComponent>(map);
            var grid = Comp<MapGridComponent>(map);
            var battlefield = AddComp<RtsBattlefieldComponent>(map);
            battlefield.Seed = seed;
            battlefield.Size = length;
            _metadata.SetEntityName(map, $"RTS battlefield ({seed})");

            var blue = new Vector2i(settings.BaseInset, length / 2);
            var red = new Vector2i(length - settings.BaseInset - 1, length / 2);
            var tiles = new List<(Vector2i, Tile)>(length * length);
            var reserved = new HashSet<Vector2i>();

            for (var x = 0; x < length; x++)
            {
                for (var y = 0; y < length; y++)
                {
                    var indices = new Vector2i(x, y);
                    if (!_biomes.TryGetTile(indices, terrain.Layers, seed, (Entity<MapGridComponent>?) null, out var tile))
                        throw new InvalidOperationException($"RTS terrain has no tile at {indices}.");

                    if ((indices - blue).LengthSquared <= settings.ClearingRadius * settings.ClearingRadius ||
                        (indices - red).LengthSquared <= settings.ClearingRadius * settings.ClearingRadius)
                    {
                        tile = clearingTile;
                        reserved.Add(indices);
                    }
                    else if (IsRoute(indices, blue, red, length, seed, settings.PathHalfWidth))
                    {
                        tile = pathTile;
                        reserved.Add(indices);
                    }

                    tiles.Add((indices, tile.Value));
                }
            }

            // Batch terrain before spawning anchored entities, as in dungeon biome generation.
            _maps.SetTiles(map, grid, tiles);
            var entityNoise = new Dictionary<int, FastNoiseLite>();
            var decalNoise = new Dictionary<int, FastNoiseLite>();
            foreach (var (indices, tile) in tiles)
            {
                if (reserved.Contains(indices))
                    continue;

                if (_biomes.TryGetDecals(indices, terrain.Layers, seed, (map, grid), out var decals, decalNoise))
                {
                    foreach (var decal in decals)
                        _decals.TryAddDecal(decal.ID, new EntityCoordinates(map, decal.Position), out _);
                }

                if (!_biomes.TryGetEntity(indices, vegetation.Layers, tile, seed, (map, grid), out var prototype, entityNoise))
                    continue;

                var tree = Spawn(prototype, new EntityCoordinates(map, indices + grid.TileSizeHalfVector));
                var treeTransform = Transform(tree);
                if (!treeTransform.Anchored)
                    _transform.AnchorEntity((tree, treeTransform), (map, grid), indices);
            }

            battlefield.BlueHeadquarters = SpawnHeadquarters(settings.BlueHeadquarters, map, grid, blue);
            battlefield.RedHeadquarters = SpawnHeadquarters(settings.RedHeadquarters, map, grid, red);
            SpawnSquad(settings.BlueUnit, map, blue, settings.StartingUnits, 1);
            SpawnSquad(settings.RedUnit, map, red, settings.StartingUnits, -1);
            _maps.InitializeMap(mapId);
            return (map, battlefield);
        }
        catch
        {
            // A bad preset must not leave a partial map or orphaned trees behind.
            Del(map);
            throw;
        }
    }

    private void SpawnSquad(EntProtoId prototype, EntityUid map, Vector2i headquarters, int count, int direction)
    {
        for (var i = 0; i < count; i++)
        {
            var offset = new Vector2(direction * (3 + i / 4), (i % 4 - 1.5f) * 1.25f);
            Spawn(prototype, new EntityCoordinates(map, headquarters + new Vector2(0.5f) + offset));
        }
    }

    private EntityUid SpawnHeadquarters(EntProtoId prototype, EntityUid map, MapGridComponent grid, Vector2i indices)
    {
        var headquarters = Spawn(prototype, new EntityCoordinates(map, indices + grid.TileSizeHalfVector));
        var transform = Transform(headquarters);
        if (!transform.Anchored)
            _transform.AnchorEntity((headquarters, transform), (map, grid), indices);
        return headquarters;
    }

    private static bool IsRoute(Vector2i tile, Vector2i blue, Vector2i red, int size, int seed, int halfWidth)
    {
        if (tile.X < blue.X || tile.X > red.X)
            return false;

        var progress = (tile.X - blue.X) / (float)(red.X - blue.X);
        // All three routes join the base clearings. The seed gently bends them without
        // changing route width or allowing obstacles to sever a connection.
        var meander = MathF.Sin(progress * MathF.Tau) * (seed % 7) * 0.35f;
        var flank = MathF.Sin(progress * MathF.PI) * size * 0.23f;
        var center = blue.Y + meander;
        return MathF.Abs(tile.Y - center) <= halfWidth ||
               MathF.Abs(tile.Y - (center + flank)) <= halfWidth ||
               MathF.Abs(tile.Y - (center - flank)) <= halfWidth;
    }
}
