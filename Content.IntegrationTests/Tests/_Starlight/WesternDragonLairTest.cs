using System.Collections.Generic;
using System.Linq;
using Content.Server._Starlight.Dragon;
using Content.Server.Ghost.Roles.Components;
using Content.Server.NPC.HTN;
using Content.Server.Parallax;
using Content.Server.Procedural;
using Content.Server.Salvage;
using Content.Server.Salvage.Expeditions;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Physics;
using Content.Shared.Procedural;
using Content.Shared.Salvage;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.CPUJob.JobQueues;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Containers;

namespace Content.IntegrationTests.Tests._Starlight;

[TestFixture]
public sealed class WesternDragonLairTest
{
    [TestCase(1, 0)]
    [TestCase(-1, 0)]
    [TestCase(0, 1)]
    [TestCase(0, -1)]
    public async Task SingleBridgeFacesApproachInEveryOrientation(int dx, int dy)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = server.System<SharedMapSystem>();
            var mapUid = map.CreateMap(out var mapId);
            var grid = em.EnsureComponent<MapGridComponent>(mapUid);
            em.EnsureComponent<BiomeComponent>(mapUid).Enabled = false;
            var min = new Vector2i(dx == -1 ? -24 : -120, dy == -1 ? -24 : -120);
            var max = new Vector2i(dx == 1 ? 24 : 120, dy == 1 ? 24 : 120);
            var occupied = new HashSet<Vector2i> { min, max, new(min.X, max.Y), new(max.X, min.Y) };
            var existing = new Dungeon([new DungeonRoom(occupied, System.Numerics.Vector2.Zero, new Box2i(min, max), [])]);
            server.System<DungeonSystem>().SpawnWesternDragonLair((mapUid, grid), "Lava", [existing], 24);
            var lair = em.GetComponent<WesternDragonLairComponent>(mapUid);
            Assert.That(lair.ApproachStart, Is.EqualTo(new Vector2i(dx, dy) * 24));

            var bosses = em.AllEntityQueryEnumerator<WesternDragonBossComponent, TransformComponent>();
            EntityUid boss = default;
            while (bosses.MoveNext(out var uid, out _, out var transform))
            {
                if (transform.MapUid == mapUid)
                    boss = uid;
            }
            Assert.That(boss, Is.Not.EqualTo(default(EntityUid)));
            var bossTile = map.GetTileRef(mapUid, grid, em.GetComponent<TransformComponent>(boss).Coordinates).GridIndices;
            var turf = server.System<TurfSystem>();
            var safe = map.GetAllTiles(mapUid, grid).Where(t => !t.Tile.IsEmpty &&
                !turf.IsTileBlocked(t, CollisionGroup.Impassable) &&
                !map.GetAnchoredEntities(mapUid, grid, t.GridIndices).Any(entity =>
                    em.GetComponent<MetaDataComponent>(entity).EntityPrototype?.ID == "FloorLavaEntity"))
                .Select(t => t.GridIndices).ToHashSet();
            Assert.That(Reachable(lair.ApproachStart, safe).Contains(bossTile), Is.True);
            ProtoId<DungeonRoomPrototype> roomId = "WesternDragonLair";
            AssertSingleEntrance(safe, bossTile, lair, server.ResolveDependency<IPrototypeManager>().Index(roomId).Size);
            map.DeleteMap(mapId);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("Lava", 0)]
    [TestCase("Lava", 100)]
    [TestCase("Caves", 0)]
    [TestCase("Caves", 100)]
    [TestCase("Grasslands", 0)]
    public async Task ExpeditionGeneratesExactlyOneAccessibleLair(string biomeId, int seedStart)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        SpawnSalvageMissionJob job = null;
        var seed = -1;
        await server.WaitAssertion(() =>
        {
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var salvage = server.System<SharedSalvageSystem>();
            ProtoId<SalvageDifficultyPrototype> difficultyId = "Moderate";
            var difficulty = prototypes.Index(difficultyId);
            seed = Enumerable.Range(seedStart, 1000).First(s => salvage.GetMission(difficulty, s).Biome == biomeId);
            job = new SpawnSalvageMissionJob(0.005, em, server.ResolveDependency<IGameTiming>(),
                server.ResolveDependency<ILogManager>(), prototypes, server.System<AnchorableSystem>(),
                server.System<BiomeSystem>(), server.System<DungeonSystem>(), server.System<MetaDataSystem>(),
                server.System<SharedMapSystem>(), EntityUid.Invalid, null,
                new SalvageMissionParams { Seed = seed, Difficulty = "Moderate" });
        });
        for (var i = 0; i < 400 && job.Status != JobStatus.Finished; i++)
        {
            await server.WaitPost(() =>
            {
                if (job.Status is not (JobStatus.Waiting or JobStatus.Finished))
                    job.Run();
            });
            await pair.RunTicksSync(5);
        }
        await server.WaitAssertion(() =>
        {
            Assert.That(job.Exception, Is.Null);
            Assert.That(job.Status, Is.EqualTo(JobStatus.Finished));
            Assert.That(job.Result, Is.True);
            var query = em.AllEntityQueryEnumerator<SalvageExpeditionComponent, MapGridComponent>();
            EntityUid mapUid = default;
            MapGridComponent grid = null;
            while (query.MoveNext(out var uid, out var expedition, out var mapGrid))
            {
                if (expedition.MissionParams.Seed != seed)
                    continue;
                mapUid = uid;
                grid = mapGrid;
                break;
            }
            Assert.That(grid, Is.Not.Null);
            var expected = biomeId is "Lava" or "Caves";
            Assert.That(em.HasComponent<WesternDragonLairComponent>(mapUid), Is.EqualTo(expected));
            if (!expected)
            {
                server.System<SharedMapSystem>().DeleteMap(em.GetComponent<MapComponent>(mapUid).MapId);
                return;
            }

            var dragons = new List<EntityUid>();
            var bosses = em.AllEntityQueryEnumerator<WesternDragonBossComponent, TransformComponent>();
            while (bosses.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapUid == mapUid)
                    dragons.Add(uid);
            }
            Assert.That(dragons, Has.Count.EqualTo(1));
            Assert.That(em.HasComponent<GhostRoleComponent>(dragons[0]), Is.False);
            var lair = em.GetComponent<WesternDragonLairComponent>(mapUid);
            var map = server.System<SharedMapSystem>();
            var biome = em.GetComponent<BiomeComponent>(mapUid);
            var reserved = biome.ModifiedTiles.Values.SelectMany(tiles => tiles).ToHashSet();
            ProtoId<DungeonRoomPrototype> roomId = "WesternDragonLair";
            var room = server.ResolveDependency<IPrototypeManager>().Index(roomId);
            for (var x = 0; x < room.Size.X; x++)
            for (var y = 0; y < room.Size.Y; y++)
            {
                var tile = lair.Origin + new Vector2i(x, y);
                Assert.That(tile.LengthSquared, Is.GreaterThan(24 * 24), "The arena must not overwrite the landing zone.");
                Assert.That(map.GetTileRef(mapUid, grid, tile).Tile.IsEmpty, Is.False);
                Assert.That(reserved.Contains(tile), Is.True, "Biome streaming must not grow lava or walls inside the arena.");
            }

            var lava = new HashSet<Vector2i>();
            var chests = new List<EntityUid>();
            var entities = em.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (entities.MoveNext(out var entity, out var meta, out var transform))
            {
                if (transform.MapUid != mapUid)
                    continue;
                if (meta.EntityPrototype?.ID == "FloorLavaEntity")
                    lava.Add(map.GetTileRef(mapUid, grid, transform.Coordinates).GridIndices);
                if (meta.EntityPrototype?.ID == "CrateWesternDragonHoard")
                    chests.Add(entity);
            }
            Assert.That(lava.Count, Is.GreaterThan(200), "The approach must not erase the lava moat.");
            Assert.That(chests, Has.Count.EqualTo(1));
            var containers = server.System<SharedContainerSystem>();
            Assert.That(containers.TryGetContainer(chests[0], "entity_storage", out var hoard), Is.True);
            Assert.That(hoard.ContainedEntities, Is.Not.Empty, "Atlas copies must spawn the filled hoard, not an empty chest.");
            Assert.That(em.GetComponent<HTNComponent>(dragons[0]).RootTask.Task.Id, Is.EqualTo("WesternDragonLairCompound"));

            var turf = server.System<TurfSystem>();
            var safe = map.GetAllTiles(mapUid, grid)
                .Where(t => !t.Tile.IsEmpty && !lava.Contains(t.GridIndices) &&
                    !turf.IsTileBlocked(t, CollisionGroup.Impassable))
                .Select(t => t.GridIndices).ToHashSet();
            var bossTile = map.GetTileRef(mapUid, grid, em.GetComponent<TransformComponent>(dragons[0]).Coordinates).GridIndices;
            Assert.That(safe.Contains(lair.ApproachStart), Is.True);
            Assert.That(Reachable(lair.ApproachStart, safe).Contains(bossTile), Is.True,
                "The approach and rotated bridge must reach the boss without crossing lava or mining walls.");
            AssertSingleEntrance(safe, bossTile, lair, room.Size);

            // The marker survives killing the boss: retries do not create a second encounter.
            em.DeleteEntity(dragons[0]);
            server.System<DungeonSystem>().SpawnWesternDragonLair((mapUid, grid), biomeId, new List<Dungeon>(), 24);
            bosses = em.AllEntityQueryEnumerator<WesternDragonBossComponent, TransformComponent>();
            while (bosses.MoveNext(out _, out _, out var xform))
                Assert.That(xform.MapUid, Is.Not.EqualTo(mapUid));
            map.DeleteMap(em.GetComponent<MapComponent>(mapUid).MapId);
        });
        await pair.CleanReturnAsync();
    }

    private static void AssertSingleEntrance(HashSet<Vector2i> safe, Vector2i bossTile,
        WesternDragonLairComponent lair, Vector2i size)
    {
        var interior = safe.Where(t => t.X >= lair.Origin.X && t.X < lair.Origin.X + size.X &&
            t.Y >= lair.Origin.Y && t.Y < lair.Origin.Y + size.Y).ToHashSet();
        var inward = (lair.Origin + size / 2 - lair.Entrance) / (size.X / 2);
        var across = new Vector2i(-inward.Y, inward.X);
        var bridge = lair.Entrance + inward * 6;
        for (var i = -2; i <= 2; i++)
            Assert.That(interior.Remove(bridge + across * i), Is.True, "Entrance must have five clear bridge tiles.");
        var reachable = Reachable(bossTile, interior);
        Assert.That(reachable.Any(t => t.X == lair.Origin.X || t.X == lair.Origin.X + size.X - 1 ||
            t.Y == lair.Origin.Y || t.Y == lair.Origin.Y + size.Y - 1), Is.False,
            "Closing the one bridge must disconnect the cavern from every outer edge.");
    }

    private static HashSet<Vector2i> Reachable(Vector2i start, HashSet<Vector2i> safe)
    {
        var seen = new HashSet<Vector2i> { start };
        var pending = new Queue<Vector2i>();
        pending.Enqueue(start);
        var offsets = new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) };
        while (pending.TryDequeue(out var tile))
        foreach (var offset in offsets)
        {
            var neighbor = tile + offset;
            if (safe.Contains(neighbor) && seen.Add(neighbor))
                pending.Enqueue(neighbor);
        }
        return seen;
    }
}
