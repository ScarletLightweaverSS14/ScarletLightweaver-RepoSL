using System.Numerics;
using Content.Shared._Starlight.Weapons.Gunnery;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Starlight.Weapons.Gunnery;

/// <summary>
/// Steers guided projectiles (<see cref="GuidedProjectileComponent"/>) toward their
/// <see cref="GuidedProjectileComponent.SteeringTarget"/> every physics frame, limited
/// by the projectile's <see cref="GuidedProjectileComponent.TurnRate"/>.
///
/// Also handles:
/// • Grid-tracking mode: if <see cref="GuidedProjectileComponent.TrackingTarget"/> is set,
///   the steering target is updated each frame to match that entity's world position.
/// • Flare countermeasures: HEAT-seekable rockets (<see cref="GuidedProjectileComponent.HeatSeekable"/>)
///   will divert toward the nearest <see cref="FlareComponent"/> within detection range,
///   then self-destruct on proximity contact.
/// </summary>
public sealed class GuidedProjectileSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem    _physics   = default!;
    [Dependency] private readonly SharedTransformSystem  _transform = default!;

    // Proximity in tiles at which a flare-diverted rocket deletes itself (and the flare).
    private const float FlareDetonationRange = 1.5f;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = AllEntityQuery<GuidedProjectileComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var guided, out var physics, out var xform))
        {
            if (!guided.Active)
                continue;

            var currentSpeed = physics.LinearVelocity.Length();
            if (currentSpeed < 0.1f)
                continue;

            var currentPos = _transform.GetMapCoordinates(uid, xform).Position;

            // ── 1. Update steering target from grid-tracking ────────────────
            if (guided.TrackingTarget.HasValue && !guided.DivertedByFlare)
            {
                if (Exists(guided.TrackingTarget.Value))
                {
                    var trackPos = _transform.GetMapCoordinates(guided.TrackingTarget.Value).Position;
                    guided.SteeringTarget = trackPos;
                }
                else
                {
                    // Target grid destroyed — clear tracking
                    guided.TrackingTarget = null;
                }
            }

            // ── 2. Check for flare countermeasures ──────────────────────────
            if (guided.HeatSeekable && !guided.DivertedByFlare)
            {
                if (TryFindNearestFlare(currentPos, xform.MapID, out var flareUid, out var flarePos))
                {
                    guided.DivertedByFlare  = true;
                    guided.TargetFlare      = flareUid;
                    guided.TrackingTarget   = null;
                    guided.SteeringTarget   = flarePos;
                }
            }

            // ── 3. Handle flare pursuit and proximity detonation ────────────
            if (guided.DivertedByFlare && guided.TargetFlare.HasValue)
            {
                if (!Exists(guided.TargetFlare.Value))
                {
                    // Flare expired — rocket keeps flying on current heading
                    guided.TargetFlare = null;
                }
                else
                {
                    var flareWorldPos = _transform.GetMapCoordinates(guided.TargetFlare.Value).Position;
                    guided.SteeringTarget = flareWorldPos;

                    // Within detonation range: delete rocket and flare silently
                    if ((flareWorldPos - currentPos).LengthSquared() <= FlareDetonationRange * FlareDetonationRange)
                    {
                        EntityManager.QueueDeleteEntity(guided.TargetFlare.Value);
                        EntityManager.QueueDeleteEntity(uid);
                        continue;
                    }
                }
            }

            // ── 4. Steer toward SteeringTarget ──────────────────────────────
            var toTarget = guided.SteeringTarget - currentPos;
            if (toTarget.LengthSquared() < 0.01f)
                continue;

            var desiredDir  = Vector2.Normalize(toTarget);
            var currentDir  = Vector2.Normalize(physics.LinearVelocity);

            var maxTurn = float.DegreesToRadians(guided.TurnRate) * frameTime;

            var dot = Math.Clamp(Vector2.Dot(currentDir, desiredDir), -1f, 1f);
            var angleToTarget = MathF.Acos(dot);

            Vector2 newDir;
            if (angleToTarget <= maxTurn)
            {
                newDir = desiredDir;
            }
            else
            {
                var cross    = currentDir.X * desiredDir.Y - currentDir.Y * desiredDir.X;
                var rotAngle = cross >= 0f ? maxTurn : -maxTurn;
                var cos      = MathF.Cos(rotAngle);
                var sin      = MathF.Sin(rotAngle);
                newDir = new Vector2(
                    cos * currentDir.X - sin * currentDir.Y,
                    sin * currentDir.X + cos * currentDir.Y);
            }

            _physics.SetLinearVelocity(uid, newDir * currentSpeed, body: physics);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private bool TryFindNearestFlare(Vector2 currentPos, MapId mapId,
        out EntityUid flareUid, out Vector2 flarePos)
    {
        flareUid = EntityUid.Invalid;
        flarePos = default;
        var bestDistSq = float.MaxValue;

        var flareQuery = AllEntityQuery<FlareComponent, TransformComponent>();
        while (flareQuery.MoveNext(out var fid, out var flare, out var fxform))
        {
            if (fxform.MapID != mapId)
                continue;

            var fp     = _transform.GetMapCoordinates(fid, fxform).Position;
            var distSq = (fp - currentPos).LengthSquared();
            var rangeSq = flare.DetectionRange * flare.DetectionRange;

            if (distSq > rangeSq || distSq >= bestDistSq)
                continue;

            bestDistSq = distSq;
            flareUid   = fid;
            flarePos   = fp;
        }

        return flareUid.IsValid();
    }
}

