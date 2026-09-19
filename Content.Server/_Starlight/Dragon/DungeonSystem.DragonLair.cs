using System.Linq;
using System.Numerics;
using Content.Server._Starlight.Dragon;
using Content.Server.Parallax;
using Content.Shared.Procedural;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server.Procedural;

public sealed partial class DungeonSystem
{
    private static readonly ProtoId<DungeonRoomPrototype> DragonLairRoom = "WesternDragonLair";

    /// <summary>
    /// One optional boss arena per eligible expedition, outside the normal dungeon and landing zone.
    /// Uses the cached room atlas, existing corridor routing and biome reservations; no runtime polling.
    /// </summary>
    public void SpawnWesternDragonLair(Entity<MapGridComponent> grid, string biomeId,
        IReadOnlyList<Dungeon> dungeons, int landingRadius)
    {
        if (biomeId is not ("Lava" or "Caves") || HasComp<WesternDragonLairComponent>(grid))
            return;

        var room = _prototype.Index(DragonLairRoom);
        var half = room.Size / 2;
        var occupied = new HashSet<Vector2i>();
        foreach (var dungeon in dungeons)
            occupied.UnionWith(dungeon.AllTiles);

        var min = new Vector2i(-landingRadius, -landingRadius);
        var max = new Vector2i(landingRadius, landingRadius);
        foreach (var tile in occupied)
        {
            min = new Vector2i(Math.Min(min.X, tile.X), Math.Min(min.Y, tile.Y));
            max = new Vector2i(Math.Max(max.X, tile.X), Math.Max(max.Y, tile.Y));
        }

        // Each candidate lies completely outside existing dungeon bounds, with an eight-tile buffer.
        // Prefer the nearest side so the lair stays discoverable even with long dungeon presets.
        var candidates = new (Vector2i Center, Vector2i Direction)[]
        {
            (new Vector2i(max.X + half.X + 9, 0), new Vector2i(1, 0)),
            (new Vector2i(min.X - half.X - 9, 0), new Vector2i(-1, 0)),
            (new Vector2i(0, max.Y + half.Y + 9), new Vector2i(0, 1)),
            (new Vector2i(0, min.Y - half.Y - 9), new Vector2i(0, -1)),
        };
        // Widen the exclusion by one tile to protect dungeon walls beside a three-wide approach.
        var forbidden = new HashSet<Vector2i>();
        foreach (var tile in occupied)
        for (var x = -1; x <= 1; x++)
        for (var y = -1; y <= 1; y++)
            forbidden.Add(tile + new Vector2i(x, y));

        var ordered = candidates.OrderBy(c => c.Center.LengthSquared).ToArray();
        var chosen = ordered[0];
        var corridor = new HashSet<Vector2i>();
        foreach (var candidate in ordered)
        {
            var door = candidate.Center - new Vector2i(candidate.Direction.X * half.X, candidate.Direction.Y * half.Y);
            // The landing zone is cleared on arrival; don't route through a dungeon that overlaps its center.
            var start = candidate.Direction * landingRadius;
            // Route to the single entrance, never through another side of the lava moat.
            var excluded = new HashSet<Vector2i>(forbidden);
            var candidateOrigin = candidate.Center - half;
            for (var x = 0; x < room.Size.X; x++)
            for (var y = 0; y < room.Size.Y; y++)
                excluded.Add(candidateOrigin + new Vector2i(x, y));
            excluded.Remove(door);
            GetCorridorNodes(corridor, [(start, door)], 8192, excluded);
            if (corridor.Count == 0)
                continue;
            corridor.Add(door);
            corridor.Add(start);
            chosen = candidate;
            break;
        }

        if (corridor.Count == 0)
            Log.Warning("Dragon lair has no clear route to the landing zone; dungeon obstacles require mining.");

        var origin = chosen.Center - half;
        // The atlas entrance faces south. Rotate its center toward the landing zone.
        var rotation = new Angle(Math.Atan2(chosen.Direction.Y, chosen.Direction.X) - Math.PI / 2);
        var transform = Matrix3Helpers.CreateTransform((Vector2) origin + (Vector2) room.Size / 2f, rotation);
        SpawnRoom(grid, grid.Comp, transform, room, clearExisting: true);

        var biome = EntityManager.System<BiomeSystem>();
        var reservation = new List<(Vector2i, Tile)>();
        biome.ReserveTiles(grid, new Box2(origin, origin + room.Size), reservation);

        var floor = new Tile(_tileDefManager["FloorBasalt"].TileId);
        var approach = new HashSet<Vector2i>();
        foreach (var node in corridor)
        for (var x = -1; x <= 1; x++)
        for (var y = -1; y <= 1; y++)
        {
            var tile = node + new Vector2i(x, y);
            if (tile.LengthSquared < (landingRadius * landingRadius) || occupied.Contains(tile))
                continue;
            // The atlas owns the bridge, moat and decorations inside these bounds.
            if (tile.X >= origin.X && tile.X < origin.X + room.Size.X &&
                tile.Y >= origin.Y && tile.Y < origin.Y + room.Size.Y)
                continue;
            approach.Add(tile);
        }
        foreach (var tile in approach)
        {
            foreach (var ent in _maps.GetAnchoredEntities(grid, grid.Comp, tile))
                QueueDel(ent);
        }
        _maps.SetTiles(grid, grid.Comp, approach.Select(tile => (tile, floor)).ToList());
        foreach (var node in corridor)
        {
            if (node.LengthSquared < (landingRadius * landingRadius))
                continue;
            reservation.Clear();
            biome.ReserveTiles(grid, new Box2(node - Vector2i.One, node + new Vector2i(2, 2)), reservation);
        }

        var lair = AddComp<WesternDragonLairComponent>(grid);
        lair.Origin = origin;
        lair.Entrance = chosen.Center - new Vector2i(chosen.Direction.X * half.X, chosen.Direction.Y * half.Y);
        lair.ApproachStart = chosen.Direction * landingRadius;
    }
}
