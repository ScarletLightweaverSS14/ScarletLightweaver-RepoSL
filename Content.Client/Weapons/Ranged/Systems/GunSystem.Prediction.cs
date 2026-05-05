using System.Numerics;
using Content.Client.Animations;
using Content.Shared._Starlight.Weapons.Ranged.Components;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Spawners;
using TimedDespawnComponent = Robust.Shared.Spawners.TimedDespawnComponent;

namespace Content.Client.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{
    [Dependency] private readonly SharedPhysicsSystem _predictionPhysics = default!;

    /// <summary>
    /// Called client-side on every shot. If the gun has <see cref="GunPredictionComponent"/>,
    /// does a quick raycast and renders a predicted bullet visual immediately.
    /// </summary>
    private void TryRenderPredictedBullet(Entity<GunComponent> gun, EntityCoordinates fromCoordinates, EntityCoordinates toCoordinates, EntityUid? user)
    {
        if (!Timing.IsFirstTimePredicted)
            return;

        if (!TryComp<GunPredictionComponent>(gun, out var pred))
            return;

        var fromMap = TransformSystem.ToMapCoordinates(fromCoordinates);
        var toMap = TransformSystem.ToMapCoordinates(toCoordinates);

        if (fromMap.MapId != toMap.MapId || fromMap.MapId == MapId.Nullspace)
            return;

        var dir = toMap.Position - fromMap.Position;
        if (dir == Vector2.Zero)
            return;

        var dirNorm = dir.Normalized();
        var maxDist = Math.Min(dir.Length(), pred.MaxRenderDistance);

        // Shoot a quick client-side ray to find the first wall/entity in the way
        var ray = new CollisionRay(fromMap.Position, dirNorm, (int) CollisionGroup.Opaque);
        var results = _predictionPhysics.IntersectRay(fromMap.MapId, ray, maxDist, user, false);

        float distance = maxDist;
        foreach (var result in results)
        {
            distance = result.Distance;
            break;
        }

        // Build coordinates relative to the grid/map for the muzzle origin
        var gridUid = Transform(fromCoordinates.EntityId).GridUid;
        EntityCoordinates muzzleCoords;
        if (gridUid != null && TryComp(gridUid, out MapGridComponent? _))
        {
            muzzleCoords = TransformSystem.WithEntityId(fromCoordinates, gridUid.Value);
        }
        else if (Transform(fromCoordinates.EntityId).MapUid is { } mapUid)
        {
            muzzleCoords = new EntityCoordinates(mapUid, fromMap.Position);
        }
        else
        {
            return;
        }

        var shotAngle = dirNorm.ToAngle();
        var muzzleVec = shotAngle.ToVec().Normalized();

        // Keep muzzle offset consistent with hitscan rendering
        var muzzleOffset = muzzleCoords.Offset(muzzleVec / 2);

        // Speed → length in ms identical to hitscan bullet rendering
        var speed = pred.BulletSpeed > 0 ? pred.BulletSpeed : 40f;
        var length = (distance - 1.5f) / (speed / 5000f);

        RenderBullet(
            GetNetCoordinates(muzzleOffset),
            shotAngle,
            pred.Bullet,
            distance - 1.5f,
            length,
            0f);
    }
}
