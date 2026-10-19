#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._Starlight.RTS;
using Content.Server._Starlight.RTS.Components;
using Content.Shared._Starlight.RTS;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Starlight.RTS;

[TestOf(typeof(RtsBattlefieldSystem))]
public sealed class RtsBattlefieldTests : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestCase(80)]
    [TestCase(120)]
    public async Task RtsBattlefieldHasFiniteTerrainAndAccessibleHeadquarters(int size)
    {
        Entity<RtsBattlefieldComponent> battlefield = default;
        BattlefieldSnapshot? initial = null;
        try
        {
            await Server.WaitAssertion(() =>
            {
                battlefield = Server.System<RtsBattlefieldSystem>().Generate(173, size);
                initial = Snapshot(battlefield);

                Assert.That(battlefield.Comp.Seed, Is.EqualTo(173));
                Assert.That(battlefield.Comp.Size, Is.EqualTo(size));
                Assert.That(SEntMan.HasComponent<MapComponent>(battlefield), Is.True);
                Assert.That(SEntMan.HasComponent<BiomeComponent>(battlefield), Is.False,
                    "A finished battlefield must not retain biome streaming or synthesized off-map terrain.");
                Assert.That(initial.Tiles.Count, Is.EqualTo(size * size));
                Assert.That(initial.Tiles.Keys.All(tile => Inside(tile, size)), Is.True,
                    "Terrain must stay inside the requested square.");
                Assert.That(initial.Trees, Is.Not.Empty, "The battlefield should contain a forest.");
                Assert.That(initial.Trees.All(tree => Inside(TileAt(tree.Position), size)), Is.True);

                var headquarters = new List<EntityUid>();
                var query = SEntMan.EntityQueryEnumerator<RtsHeadquartersComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out _, out var transform))
                {
                    if (transform.MapUid == battlefield.Owner)
                        headquarters.Add(uid);
                }

                Assert.That(headquarters, Is.EquivalentTo(new[]
                {
                    battlefield.Comp.BlueHeadquarters,
                    battlefield.Comp.RedHeadquarters,
                }));

                var blue = CheckHeadquarters(battlefield, battlefield.Comp.BlueHeadquarters, RtsTeam.Blue);
                var red = CheckHeadquarters(battlefield, battlefield.Comp.RedHeadquarters, RtsTeam.Red);
                Assert.That(blue, Is.Not.EqualTo(red));
                CheckStartingArmy(battlefield, RtsTeam.Blue, blue);
                CheckStartingArmy(battlefield, RtsTeam.Red, red);
                Assert.That(initial.Trees.All(tree =>
                    Vector2.DistanceSquared(tree.Position, blue) > 9 * 9 &&
                    Vector2.DistanceSquared(tree.Position, red) > 9 * 9), Is.True,
                    "Both headquarters need an unobstructed deployment area.");
                Assert.That(HasTreeFreeRoute(initial, TileAt(blue), TileAt(red), size), Is.True,
                    "Forest placement must leave a route between headquarters.");
            });

            // With no player near this map, biome streaming would otherwise unload its terrain.
            await Pair.RunTicksSync(10);
            await Server.WaitAssertion(() =>
            {
                var afterTicks = Snapshot(battlefield);
                Assert.That(SameTerrain(afterTicks, initial!), Is.True,
                    "Terrain types and variants must remain unchanged after ticking.");
                Assert.That(SameForest(afterTicks, initial!), Is.True,
                    "Vegetation prototypes and positions must remain unchanged after ticking.");
                Assert.That(afterTicks.Entities.ToHashSet().SetEquals(initial!.Entities), Is.True,
                    "Ticking must not add or unload battlefield entities.");
            });
        }
        finally
        {
            await DeleteBattlefield(battlefield.Owner, initial);
        }
    }

    [Test]
    public async Task RtsBattlefieldSeedReproducesTerrainAndForest()
    {
        var first = await GenerateSnapshotAndDelete(173);
        var repeated = await GenerateSnapshotAndDelete(173);
        var different = await GenerateSnapshotAndDelete(619);

        Assert.That(SameTerrain(repeated, first), Is.True,
            "A seed must reproduce tile types, flags, and variants.");
        Assert.That(SameForest(repeated, first), Is.True,
            "A seed must reproduce vegetation prototypes and positions.");
        Assert.That(SameTerrain(different, first), Is.False,
            "Changing the seed should change the terrain.");
        Assert.That(SameForest(different, first), Is.False,
            "Changing the seed should change the forest.");
    }

    [TestCase(79)]
    [TestCase(121)]
    public async Task RtsBattlefieldRejectsInvalidSizeBeforeCreatingMap(int size)
    {
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            var before = maps.GetAllMapIds().ToArray();
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Server.System<RtsBattlefieldSystem>().Generate(173, size));
            Assert.That(maps.GetAllMapIds(), Is.EquivalentTo(before),
                "Rejected generation must not leave a partial map behind.");
        });
    }

    private Vector2 CheckHeadquarters(Entity<RtsBattlefieldComponent> battlefield, EntityUid uid, RtsTeam team)
    {
        var transform = SEntMan.GetComponent<TransformComponent>(uid);
        Assert.That(SEntMan.GetComponent<RtsOwnerComponent>(uid).Team, Is.EqualTo(team));
        Assert.That(transform.ParentUid, Is.EqualTo(battlefield.Owner));
        Assert.That(transform.Anchored, Is.True);
        Assert.That(Inside(TileAt(transform.LocalPosition), battlefield.Comp.Size), Is.True);
        return transform.LocalPosition;
    }

    private void CheckStartingArmy(Entity<RtsBattlefieldComponent> battlefield, RtsTeam team, Vector2 headquarters)
    {
        var positions = new List<Vector2>();
        var query = SEntMan.EntityQueryEnumerator<RtsUnitComponent, RtsOwnerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var unit, out var owner, out var transform))
        {
            if (transform.MapUid != battlefield.Owner || owner.Team != team)
                continue;

            Assert.That(SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID,
                Is.EqualTo(team == RtsTeam.Blue ? "RtsInfantryBlue" : "RtsInfantryRed"));
            Assert.That(unit.State, Is.EqualTo(RtsUnitState.Idle));
            Assert.That(transform.Anchored, Is.False);
            Assert.That(Inside(TileAt(transform.LocalPosition), battlefield.Comp.Size), Is.True);
            Assert.That(Vector2.Distance(transform.LocalPosition, headquarters), Is.InRange(1.5f, 9f),
                "Starting units must deploy inside their base clearing, clear of headquarters.");
            positions.Add(transform.LocalPosition);
        }

        Assert.That(positions, Has.Count.EqualTo(4), "Each team starts with a small infantry squad.");
        Assert.That(positions.Distinct().Count(), Is.EqualTo(positions.Count),
            "Starting units need separate deployment positions.");
    }

    private BattlefieldSnapshot Snapshot(Entity<RtsBattlefieldComponent> battlefield)
    {
        var grid = SEntMan.GetComponent<MapGridComponent>(battlefield);
        var tiles = Server.System<SharedMapSystem>().GetAllTiles(battlefield, grid)
            .ToDictionary(tile => tile.GridIndices, tile => tile.Tile);
        var trees = new List<(string Prototype, Vector2 Position)>();
        var entities = new List<EntityUid>();
        var query = SEntMan.EntityQueryEnumerator<TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var transform, out var metadata))
        {
            if (uid == battlefield.Owner || transform.MapUid != battlefield.Owner)
                continue;

            entities.Add(uid);
            if (metadata.EntityPrototype?.ID is "FloraTree" or "FloraTreeLarge")
                trees.Add((metadata.EntityPrototype!.ID, transform.LocalPosition));
        }

        return new BattlefieldSnapshot(tiles, trees, entities);
    }

    private async Task<BattlefieldSnapshot> GenerateSnapshotAndDelete(int seed)
    {
        Entity<RtsBattlefieldComponent> battlefield = default;
        BattlefieldSnapshot? snapshot = null;
        try
        {
            await Server.WaitAssertion(() =>
            {
                battlefield = Server.System<RtsBattlefieldSystem>().Generate(seed, 80);
                snapshot = Snapshot(battlefield);
            });
            return snapshot!;
        }
        finally
        {
            await DeleteBattlefield(battlefield.Owner, snapshot);
        }
    }

    private async Task DeleteBattlefield(EntityUid map, BattlefieldSnapshot? snapshot)
    {
        await Server.WaitAssertion(() =>
        {
            if (SEntMan.EntityExists(map))
                SEntMan.DeleteEntity(map);

            Assert.That(SEntMan.EntityExists(map), Is.False);
            if (snapshot != null)
            {
                Assert.That(snapshot.Entities.All(uid => !SEntMan.EntityExists(uid)), Is.True,
                    "Deleting the battlefield must also delete its headquarters and forest.");
            }
        });
    }

    private static bool HasTreeFreeRoute(BattlefieldSnapshot battlefield, Vector2i start, Vector2i goal, int size)
    {
        // Treat the entire tile occupied by a tree as impassable, rather than only its trunk.
        var blocked = battlefield.Trees.Select(tree => TileAt(tree.Position)).ToHashSet();
        var visited = new HashSet<Vector2i> { start };
        var pending = new Queue<Vector2i>();
        pending.Enqueue(start);
        Vector2i[] directions = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
        while (pending.TryDequeue(out var tile))
        {
            if (tile == goal)
                return true;

            foreach (var direction in directions)
            {
                var next = tile + direction;
                if (Inside(next, size) && !blocked.Contains(next) && visited.Add(next))
                    pending.Enqueue(next);
            }
        }

        return false;
    }

    private static bool Inside(Vector2i tile, int size) =>
        tile.X >= 0 && tile.X < size && tile.Y >= 0 && tile.Y < size;

    private static Vector2i TileAt(Vector2 position) =>
        new((int) MathF.Floor(position.X), (int) MathF.Floor(position.Y));

    // Compare thousands of tiles by their keys, avoiding unordered NUnit element matching.
    private static bool SameTerrain(BattlefieldSnapshot left, BattlefieldSnapshot right) =>
        left.Tiles.Count == right.Tiles.Count &&
        left.Tiles.All(pair => right.Tiles.TryGetValue(pair.Key, out var tile) && pair.Value.Equals(tile));

    private static bool SameForest(BattlefieldSnapshot left, BattlefieldSnapshot right)
    {
        var trees = left.Trees.ToHashSet();
        return left.Trees.Count == right.Trees.Count &&
               trees.Count == left.Trees.Count &&
               trees.SetEquals(right.Trees);
    }

    private sealed record BattlefieldSnapshot(
        Dictionary<Vector2i, Tile> Tiles,
        List<(string Prototype, Vector2 Position)> Trees,
        List<EntityUid> Entities);
}
