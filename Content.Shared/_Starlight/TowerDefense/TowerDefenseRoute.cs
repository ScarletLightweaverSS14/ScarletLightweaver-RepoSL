using System.Numerics;

namespace Content.Shared._Starlight.TowerDefense;

/// <summary>Grid-aligned route validation and distance-based movement, without pathfinding.</summary>
public static class TowerDefenseRoute
{
    public static Vector2 Center(Vector2i tile) => new(tile.X + 0.5f, tile.Y + 0.5f);

    public static bool TryValidate(IReadOnlyList<Vector2i> route, int width, int height,
        out HashSet<Vector2i> road)
    {
        road = new();
        if (width < 1 || height < 1 || width > 32 || height > 32 || route.Count < 2)
            return false;

        foreach (var point in route)
        {
            if (point.X < 0 || point.Y < 0 || point.X >= width || point.Y >= height)
                return false;
        }

        road.Add(route[0]);
        for (var i = 1; i < route.Count; i++)
        {
            var previous = route[i - 1];
            var delta = route[i] - previous;
            // Exactly one axis must change. Duplicate corners are invalid too.
            if ((delta.X == 0) == (delta.Y == 0))
                return false;

            var step = new Vector2i(Math.Sign(delta.X), Math.Sign(delta.Y));
            for (var tile = previous + step; ; tile += step)
            {
                // Reject loops/crossings, which make placement and route progress ambiguous.
                if (!road.Add(tile))
                    return false;
                if (tile == route[i])
                    break;
            }
        }

        return true;
    }

    /// <summary>Consumes leftover distance around corners, including frames spanning several segments.</summary>
    public static Vector2 Advance(IReadOnlyList<Vector2i> route, Vector2 position, ref int next, float distance)
    {
        while (next < route.Count && distance > 0)
        {
            var target = Center(route[next]);
            var delta = target - position;
            var remaining = delta.Length();
            if (remaining > distance)
                return position + delta / remaining * distance;

            position = target;
            distance -= remaining;
            next++;
        }

        return position;
    }
}
