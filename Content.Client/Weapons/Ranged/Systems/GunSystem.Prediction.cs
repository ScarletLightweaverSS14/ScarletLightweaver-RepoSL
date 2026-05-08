using System.Numerics;
using Content.Shared._Starlight.Weapons.Ranged.Components;
using Content.Shared.Physics;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;

namespace Content.Client.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;

    /// <summary>
    /// Real-time timestamp of the most recent predicted shot.
    /// Exposed so <see cref="PredictionDebugOverlay"/> can flash "PRED ●" for 0.5 s after each shot.
    /// </summary>
    internal TimeSpan? LastPredictionRealTime { get; private set; }

    /// <summary>
    /// Immediately renders a predicted bullet visual for cartridge / standard ammo the instant the
    /// player fires, without waiting for the server confirmation (~1 RTT later).
    ///
    /// Accepts pre-computed <see cref="MapCoordinates"/> from <see cref="Shoot"/> so the positions
    /// are immune to entity-transform changes between prediction replay ticks.
    ///
    /// Requires <see cref="GunPredictionComponent"/> on the gun entity.
    /// </summary>
    private void TryRenderPredictedBullet(
        Entity<GunComponent> gun,
        MapCoordinates fromMap,
        MapCoordinates toMap,
        EntityUid? user)
    {
        if (!Timing.IsFirstTimePredicted)
            return;

        if (!TryComp<GunPredictionComponent>(gun, out var pred))
            return;

        if (fromMap.MapId != toMap.MapId || fromMap.MapId == MapId.Nullspace)
            return;

        var dir = toMap.Position - fromMap.Position;
        if (dir == Vector2.Zero)
            return;

        var dirNorm = dir.Normalized();
        var maxDist = Math.Min(dir.Length(), pred.MaxRenderDistance);

        // Client-side opaque-blocker raycast so the bullet visual stops at walls/doors.
        var ray     = new CollisionRay(fromMap.Position, dirNorm, (int) CollisionGroup.Opaque);
        var results = Physics.IntersectRay(fromMap.MapId, ray, maxDist, user, false);

        var distance = maxDist;
        foreach (var result in results)
        {
            distance = result.Distance;
            break;
        }

        var mapUid      = _mapManager.GetMapEntityId(fromMap.MapId);
        var muzzle      = new EntityCoordinates(mapUid, fromMap.Position);
        var shotAngle   = dirNorm.ToAngle();
        var renderDist  = Math.Max(distance - 1.5f, 0f);

        var speed      = pred.BulletSpeed > 0f ? pred.BulletSpeed : SharedGunSystem.ProjectileSpeed;
        var visualSpeed = speed * 5f;                        // hitscan renderer slows bullets 5× for visibility
        var length      = renderDist / (visualSpeed / 5000f);

        LastPredictionRealTime = Timing.RealTime;

        RenderBullet(
            GetNetCoordinates(muzzle.Offset(shotAngle.ToVec().Normalized() / 2)),
            shotAngle,
            pred.Bullet,
            renderDist,
            length,
            0f);
    }

    /// <summary>
    /// Immediately renders predicted hitscan visuals (trace / muzzle flash / impact) on the local
    /// client without waiting for the server's <see cref="SharedGunSystem.HitscanEvent"/>.
    ///
    /// Mirrors <c>GenerateTraceStep</c> in <c>HitscanBasicRaycastSystem</c> to produce grid-relative
    /// coordinates that <see cref="GunSystem.RenderBullet"/> can render correctly on moving ships.
    ///
    /// <see cref="HitscanBasicRaycastComponent"/> is optional — sane defaults are used when absent.
    /// Returns <c>true</c> when visuals were successfully rendered.
    /// </summary>
    private bool TryPredictHitscan(
        Entity<GunComponent> gun,
        EntityUid ammoEnt,
        MapCoordinates fromMap,
        MapCoordinates toMap,
        EntityUid? user)
    {
        if (!Timing.IsFirstTimePredicted)
            return false;

        if (!TryComp<HitscanBasicVisualsComponent>(ammoEnt, out var visuals))
            return false;

        if (fromMap.MapId != toMap.MapId || fromMap.MapId == MapId.Nullspace)
            return false;

        var dir = toMap.Position - fromMap.Position;
        if (dir == Vector2.Zero)
            return false;

        var dirNorm = dir.Normalized();

        // HitscanBasicRaycastComponent is optional — fall back to defaults if missing.
        TryComp<HitscanBasicRaycastComponent>(ammoEnt, out var raycast);
        var collisionMask = (int)(raycast?.CollisionMask ?? CollisionGroup.BulletImpassable);
        var maxDistance   = raycast?.MaxDistance ?? 20f;

        // Raycast with the same collision mask the server uses.
        var ray     = new CollisionRay(fromMap.Position, dirNorm, collisionMask);
        var results = Physics.IntersectRay(fromMap.MapId, ray, maxDistance, user, false);

        var distance  = maxDistance;
        EntityUid? hitEntity = null;
        foreach (var result in results)
        {
            distance  = result.Distance;
            hitEntity = result.HitEntity;
            break;
        }

        // Mirror GenerateTraceStep: express coords & angle relative to the grid so the renderer
        // works correctly on rotating ships.  On a standard station gridRot ≈ 0 and this is a no-op.
        EntityCoordinates relFrom;
        var shotAngle = dirNorm.ToAngle();

        if (_mapManager.TryFindGridAt(fromMap, out var gridUid, out _)
            && TryComp(gridUid, out TransformComponent? gridXform))
        {
            var (_, gridRot, gridInvMatrix) = TransformSystem.GetWorldPositionRotationInvMatrix(gridXform!);
            relFrom    = new EntityCoordinates(gridUid, Vector2.Transform(fromMap.Position, gridInvMatrix));
            shotAngle -= gridRot;
        }
        else
        {
            var mapUid = _mapManager.GetMapEntityId(fromMap.MapId);
            relFrom    = new EntityCoordinates(mapUid, fromMap.Position);
        }

        var shotVec = shotAngle.ToVec().Normalized();
        var trace = new HitscanTrace
        {
            Angle             = shotAngle,
            Distance          = distance,
            MuzzleCoordinates = distance > 1f ? GetNetCoordinates(relFrom.Offset(shotVec / 2))                          : null,
            TravelCoordinates = distance > 1f ? GetNetCoordinates(relFrom.Offset(shotVec * (distance + 0.5f) / 2)) : null,
            ImpactCoordinates = GetNetCoordinates(relFrom.Offset(shotVec * distance)),
            ImpactedEnt       = GetNetEntity(hitEntity),
        };

        var ev = new SharedGunSystem.HitscanEvent
        {
            MuzzleFlash = visuals.MuzzleFlash,
            TravelFlash = visuals.TravelFlash,
            ImpactFlash = visuals.ImpactFlash,
            Bullet      = visuals.Bullet,
            Speed       = visuals.Speed,
            Traces      = new List<HitscanTrace> { trace },
        };

        LastPredictionRealTime = Timing.RealTime;
        OnHitscan(ev);
        return true;
    }
}

