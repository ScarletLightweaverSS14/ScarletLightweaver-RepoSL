using System.Collections.Generic;
using System.Numerics;
using Content.Shared._Starlight.TowerDefense;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._Starlight;

[TestFixture]
public sealed class TowerDefenseRouteTest
{
    private static readonly Vector2i[] Route = { new(0, 2), new(3, 2), new(3, 0) };

    [Test]
    public void RoadIncludesEveryTileAndBothEndpoints()
    {
        Assert.That(TowerDefenseRoute.TryValidate(Route, 4, 3, out var road), Is.True);
        Assert.That(road.SetEquals(new HashSet<Vector2i>
        {
            new(0, 2), new(1, 2), new(2, 2), new(3, 2), new(3, 1), new(3, 0),
        }), Is.True);
    }

    [Test]
    public void RejectsDiagonalDuplicateCrossingAndOutOfBounds()
    {
        Assert.That(TowerDefenseRoute.TryValidate(new[] { new Vector2i(0, 0), new(1, 1) }, 4, 4, out _), Is.False);
        Assert.That(TowerDefenseRoute.TryValidate(new[] { new Vector2i(0, 0), new(0, 0) }, 4, 4, out _), Is.False);
        Assert.That(TowerDefenseRoute.TryValidate(new[] { new Vector2i(0, 0), new(4, 0) }, 4, 4, out _), Is.False);
        Assert.That(TowerDefenseRoute.TryValidate(new[] { new Vector2i(0, 0), new(3, 0), new(1, 0) }, 4, 4, out _), Is.False);
    }

    [Test]
    public void LongFrameTurnsWithoutCuttingCornerOrLosingDistance()
    {
        var next = 1;
        var position = TowerDefenseRoute.Advance(Route, TowerDefenseRoute.Center(Route[0]), ref next, 4);
        Assert.That(position, Is.EqualTo(new Vector2(3.5f, 1.5f)));
        Assert.That(next, Is.EqualTo(2));
        position = TowerDefenseRoute.Advance(Route, position, ref next, 100);
        Assert.That(position, Is.EqualTo(TowerDefenseRoute.Center(Route[^1])));
        Assert.That(next, Is.EqualTo(Route.Length));
    }

    [Test]
    public void SplittingTimeStepsPreservesProgress()
    {
        var next = 1;
        var position = TowerDefenseRoute.Center(Route[0]);
        for (var i = 0; i < 16; i++)
            position = TowerDefenseRoute.Advance(Route, position, ref next, 0.25f);
        Assert.That(position, Is.EqualTo(new Vector2(3.5f, 1.5f)));
        Assert.That(next, Is.EqualTo(2));
    }

    [Test]
    public void WindingArenaTraversesWestboundAndFinalEastboundLegs()
    {
        Vector2i[] route = { new(0, 7), new(10, 7), new(10, 4), new(3, 4), new(3, 1), new(12, 1) };
        Assert.That(TowerDefenseRoute.TryValidate(route, 13, 9, out var road), Is.True);
        Assert.That(road.Count, Is.EqualTo(33));
        var next = 1;
        var position = TowerDefenseRoute.Advance(route, TowerDefenseRoute.Center(route[0]), ref next, 17);
        Assert.That(position, Is.EqualTo(new Vector2(6.5f, 4.5f)));
        Assert.That(next, Is.EqualTo(3));
        position = TowerDefenseRoute.Advance(route, position, ref next, 9);
        Assert.That(position, Is.EqualTo(new Vector2(6.5f, 1.5f)));
        Assert.That(next, Is.EqualTo(5));
        position = TowerDefenseRoute.Advance(route, position, ref next, 6);
        Assert.That(position, Is.EqualTo(TowerDefenseRoute.Center(route[^1])));
        Assert.That(next, Is.EqualTo(route.Length));
    }
}
