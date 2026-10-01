using System.Numerics;
using Content.Shared._Starlight.NullSpace;
using Content.Shared.Mobs.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._Starlight.Voidwalker;

/// <summary>Uses the same collision query for damage and visual clipping.</summary>
public sealed partial class CrystalBeamTraceSystem : EntitySystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    public float Trace(CrystalBeamComponent beam, MapCoordinates origin, float maximum,
        Dictionary<EntityUid, float>? targets = null, Angle? directionOverride = null)
    {
        targets?.Clear();
        var direction = (directionOverride ?? beam.Direction).ToVec();
        var side = new Vector2(-direction.Y, direction.X);
        var length = maximum;
        // Bound the sampling cost even if an admin supplies an excessive ray count.
        var rays = Math.Clamp(beam.RayCount | 1, 3, 15);
        for (var i = 0; i < rays; i++)
        {
            var offset = side * (beam.Width * (i / (float) (rays - 1) - 0.5f));
            var ray = new CollisionRay(origin.Position + offset, direction, (int) beam.CollisionMask);
            foreach (var hit in _physics.IntersectRayWithPredicate(origin.MapId, ray, maximum,
                         uid => uid == beam.Caster || HasComp<NullSpaceComponent>(uid) ||
                                _containers.IsEntityOrParentInContainer(uid), false))
            {
                if (targets != null && (!targets.TryGetValue(hit.HitEntity, out var distance) || hit.Distance < distance))
                    targets[hit.HitEntity] = hit.Distance;
                // Creatures are pierced; solid fixtures such as walls, doors and machines stop the channel.
                if (!HasComp<MobStateComponent>(hit.HitEntity))
                    length = Math.Min(length, hit.Distance);
            }
        }

        return length;
    }
}
