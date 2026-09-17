using System.Collections.Generic;
using System.Linq;
using Content.Server._Starlight.Dragon;
using Content.Server.Ghost.Roles.Components;
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

namespace Content.IntegrationTests.Tests._Starlight;

[TestFixture]
public sealed class WesternDragonLairTest
{
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
            for (var x = 0; x < 25; x++)
            for (var y = 0; y < 25; y++)
            {
                var tile = lair.Origin + new Vector2i(x, y);
                Assert.That(tile.LengthSquared, Is.GreaterThan(24 * 24), "The arena must not overwrite the landing zone.");
                Assert.That(map.GetTileRef(mapUid, grid, tile).Tile.IsEmpty, Is.False);
                Assert.That(reserved.Contains(tile), Is.True, "Biome streaming must not grow lava or walls inside the arena.");
            }

            // Follow the actual painted trail through the wide doorway to the arena center.
            var floorId = server.ResolveDependency<ITileDefinitionManager>()["FloorSteelCheckerLight"].TileId;
            var trail = map.GetAllTiles(mapUid, grid).Where(t => t.Tile.TypeId == floorId).Select(t => t.GridIndices).ToHashSet();
            var start = trail.MinBy(t => t.LengthSquared);
            Assert.That(start.Length, Is.LessThan(27), "A visible approach must start beside the landing zone.");
            var center = lair.Origin + new Vector2i(12, 12);
            var seen = new HashSet<Vector2i> { start };
            var pending = new Queue<Vector2i>();
            pending.Enqueue(start);
            while (pending.TryDequeue(out var tile))
            {
                foreach (var offset in new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) })
                {
                    var neighbor = tile + offset;
                    if (!trail.Contains(neighbor) || !seen.Add(neighbor))
                        continue;
                    if (server.System<TurfSystem>().IsTileBlocked(map.GetTileRef(mapUid, grid, neighbor), CollisionGroup.Impassable))
                        continue;
                    pending.Enqueue(neighbor);
                }
            }
            Assert.That(seen.Contains(center), Is.True, "The marked approach must reach the boss without mining a wall.");

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
}
