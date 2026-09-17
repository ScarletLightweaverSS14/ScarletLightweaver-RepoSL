using System.Numerics;
using Content.Server.NPC.Components;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._Starlight.Dragon;

public sealed partial class WesternDragonBossSystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;

    private void Position(EntityUid uid, WesternDragonBossComponent boss, Enemy target, float health)
    {
        var now = _timing.CurTime;
        // A completed short maneuver must not leave the dragon waiting for its decision timer.
        if (now < boss.NextPosition && boss.Positioning is DragonPositioning.Circle or DragonPositioning.Retreat &&
            TryComp<NPCSteeringComponent>(uid, out var current) && current.Status == SteeringStatus.Moving)
            return;

        var mode = DragonPositioning.Approach;
        if (now >= boss.NextReposition && boss.RecentDamage > 80 && target.Distance < 4)
            mode = DragonPositioning.Retreat;
        else if (now >= boss.NextReposition && health <= boss.BreathHealth && target.Distance is > 4 and < 7 &&
                 boss.LastAbility is DragonAbility.Breath or DragonAbility.Fireball && _random.Prob(0.25f))
            mode = DragonPositioning.Circle;

        // Normal melee AI follows entity-relative coordinates so a visible moving target cannot outrun
        // a sequence of stale 2.5-tile waypoints. LOS loss still switches to the fixed last-seen snapshot.
        var destination = new EntityCoordinates(target.Uid, Vector2.Zero);
        var clearLocalMove = false;
        if (mode != DragonPositioning.Approach)
        {
            boss.NextPosition = now + TimeSpan.FromSeconds(1.5);
            boss.NextReposition = now + TimeSpan.FromSeconds(8);
            if (_random.Prob(0.25f))
                boss.CircleDirection *= -1;
            clearLocalMove = TryPosition(uid, target, mode, boss.CircleDirection, 2.5f, out var local);
            if (clearLocalMove)
                destination = local;
            else
                mode = DragonPositioning.Approach;
        }
        boss.Positioning = mode;
        // A failed path leaves steering stopped until its owner replaces it.
        if (TryComp<NPCSteeringComponent>(uid, out var previous) && previous.Status == SteeringStatus.NoPath)
            _steering.Unregister(uid);
        var steering = _steering.Register(uid, destination);
        steering.Range = mode == DragonPositioning.Approach && _melee.TryGetWeapon(uid, out _, out var weapon)
            ? Math.Max(0.3f, weapon.Range * 0.8f) : 0.3f;
        // These short destinations were already checked for floor and clear line of travel.
        // Keep context steering/collision avoidance, without waiting on a navigation job for every sidestep.
        steering.DirectMove = clearLocalMove || Transform(uid).GridUid == null ||
            (mode == DragonPositioning.Approach && _interaction.InRangeUnobstructed(uid, destination, range: 0,
                collisionMask: CollisionGroup.Impassable | CollisionGroup.InteractImpassable));
        steering.InRangeMaxSpeed = null;
    }

    /// <summary>At most three nearby probes; existing steering performs the actual obstacle avoidance/pathfinding.</summary>
    private bool TryPosition(EntityUid uid, Enemy target, DragonPositioning mode, float side, float step, out EntityCoordinates destination)
    {
        destination = default;
        var origin = _transform.GetMapCoordinates(uid);
        var delta = target.Position - origin.Position;
        if (delta.LengthSquared() < 0.01f)
            delta = Vector2.UnitX;
        var forward = Vector2.Normalize(delta);
        var tangent = new Vector2(-forward.Y, forward.X) * side;
        var direction = mode switch
        {
            DragonPositioning.Retreat => -forward + tangent * 0.35f,
            DragonPositioning.Circle => tangent + forward * Math.Clamp((target.Distance - 4) * 0.35f, -0.7f, 0.7f),
            _ => forward,
        };
        step = mode == DragonPositioning.Approach ? Math.Min(step, Math.Max(0.25f, target.Distance - 1)) : step;
        var xform = Transform(uid);
        for (var i = 0; i < 3; i++)
        {
            var dir = i switch { 1 => direction + tangent * 0.7f, 2 => direction - tangent * 0.7f, _ => direction };
            var point = new MapCoordinates(origin.Position + Vector2.Normalize(dir) * step, origin.MapId);
            var parent = xform.GridUid ?? xform.MapUid;
            if (parent == null)
                return false;
            var coordinates = _transform.ToCoordinates(parent.Value, point);
            if (xform.GridUid is { } gridUid && TryComp<MapGridComponent>(gridUid, out var grid))
            {
                var tile = _map.GetTileRef(gridUid, grid, coordinates);
                if (_turf.IsSpace(tile) || _turf.IsTileBlocked(tile, CollisionGroup.Impassable | CollisionGroup.InteractImpassable))
                    continue;
            }
            if (!_interaction.InRangeUnobstructed(uid, coordinates, range: 0,
                    collisionMask: CollisionGroup.Impassable | CollisionGroup.InteractImpassable))
                continue;
            destination = coordinates;
            return true;
        }
        return false;
    }
}
