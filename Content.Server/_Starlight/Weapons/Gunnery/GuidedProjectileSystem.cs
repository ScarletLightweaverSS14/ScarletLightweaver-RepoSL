using System.Numerics;
using Content.Shared._Starlight.Weapons.Gunnery;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Starlight.Weapons.Gunnery;

/// <summary>
/// Steers guided projectiles (<see cref="GuidedProjectileComponent"/>) toward their
/// <see cref="GuidedProjectileComponent.SteeringTarget"/> every physics frame, limited
/// by the projectile's <see cref="GuidedProjectileComponent.TurnRate"/>.
///
/// Also handles auto-seeking behavior for HEAT missiles: each tick it finds the nearest
/// <see cref="FlareDecoyComponent"/> (prioritised) or the locked <see cref="GuidedProjectileComponent.SeekingTarget"/>
/// grid and updates <see cref="GuidedProjectileComponent.SteeringTarget"/> autonomously.
/// </summary>
public sealed class GuidedProjectileSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem    _physics   = default!;
    [Dependency] private readonly SharedTransformSystem  _transform = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = AllEntityQuery<GuidedProjectileComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var guided, out var physics, out var xform))
        {
            // Auto-seek: update SteeringTarget from world state each tick.
            if (guided.AutoSeek)
                UpdateAutoSeekTarget(uid, guided, xform);

            if (!guided.Active)
                continue;

            var currentSpeed = physics.LinearVelocity.Length();
            if (currentSpeed < 0.1f)
                continue;

            var currentPos = _transform.GetMapCoordinates(uid, xform).Position;
            var toTarget   = guided.SteeringTarget - currentPos;

            if (toTarget.LengthSquared() < 0.01f)
                continue;

            var desiredDir  = Vector2.Normalize(toTarget);
            var currentDir  = Vector2.Normalize(physics.LinearVelocity);

            // Maximum rotation this frame (radians).
            var maxTurn = float.DegreesToRadians(guided.TurnRate) * frameTime;

            var dot = Math.Clamp(Vector2.Dot(currentDir, desiredDir), -1f, 1f);
            var angleToTarget = MathF.Acos(dot);

            Vector2 newDir;
            if (angleToTarget <= maxTurn)
            {
                // Can fully align this frame.
                newDir = desiredDir;
            }
            else
            {
                // Rotate currentDir toward desiredDir by maxTurn.
                // Cross product sign determines rotation direction.
                var cross      = currentDir.X * desiredDir.Y - currentDir.Y * desiredDir.X;
                var rotAngle   = cross >= 0f ? maxTurn : -maxTurn;
                var cos        = MathF.Cos(rotAngle);
                var sin        = MathF.Sin(rotAngle);
                newDir = new Vector2(
                    cos * currentDir.X - sin * currentDir.Y,
                    sin * currentDir.X + cos * currentDir.Y);
            }

            _physics.SetLinearVelocity(uid, newDir * currentSpeed, body: physics);
        }
    }

    /// <summary>
    /// Updates the steering target for auto-seeking (HEAT) missiles.
    /// Priority: (1) nearest flare decoy within range, (2) locked SeekingTarget, (3) nearest ship grid.
    /// </summary>
    private void UpdateAutoSeekTarget(EntityUid uid, GuidedProjectileComponent guided, TransformComponent xform)
    {
        var missilePos = _transform.GetMapCoordinates(uid, xform);
        var rangeSquared = guided.AutoSeekRange * guided.AutoSeekRange;

        // Priority 1: nearest flare decoy – always diverts HEAT missiles.
        var bestFlareDist = float.MaxValue;
        Vector2? flarePos = null;

        var flareQuery = AllEntityQuery<FlareDecoyComponent, TransformComponent>();
        while (flareQuery.MoveNext(out _, out _, out var flareXform))
        {
            var fPos = _transform.GetMapCoordinates(flareXform);
            if (fPos.MapId != missilePos.MapId)
                continue;

            var dist = (fPos.Position - missilePos.Position).LengthSquared();
            if (dist < rangeSquared && dist < bestFlareDist)
            {
                bestFlareDist = dist;
                flarePos = fPos.Position;
            }
        }

        if (flarePos.HasValue)
        {
            guided.SteeringTarget = flarePos.Value;
            guided.Active = true;
            return;
        }

        // Priority 2: use the locked SeekingTarget grid (set when missile was launched).
        if (guided.SeekingTarget.HasValue && Exists(guided.SeekingTarget.Value))
        {
            var targetPos = _transform.GetMapCoordinates(guided.SeekingTarget.Value);
            if (targetPos.MapId == missilePos.MapId)
            {
                guided.SteeringTarget = targetPos.Position;
                guided.Active = true;
                return;
            }
        }

        // Priority 3: find the nearest ship grid not on the same grid as the missile
        // and not the source grid (the launcher's own ship).
        var missileGrid = xform.GridUid;
        var sourceGrid  = guided.SourceGrid;
        var bestGridDist = float.MaxValue;
        Vector2? gridPos = null;

        var gridQuery = AllEntityQuery<MapGridComponent, TransformComponent>();
        while (gridQuery.MoveNext(out var gridId, out _, out var gridXform))
        {
            if (gridId == missileGrid)
                continue;
            if (sourceGrid.HasValue && gridId == sourceGrid.Value)
                continue;

            var gPos = _transform.GetMapCoordinates(gridXform);
            if (gPos.MapId != missilePos.MapId)
                continue;

            var dist = (gPos.Position - missilePos.Position).LengthSquared();
            if (dist < bestGridDist)
            {
                bestGridDist = dist;
                gridPos = gPos.Position;
                guided.SeekingTarget = gridId;  // cache for future ticks
            }
        }

        if (gridPos.HasValue)
        {
            guided.SteeringTarget = gridPos.Value;
            guided.Active = true;
        }
    }
}
