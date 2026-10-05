using System.Numerics;
using Content.Shared._Starlight.Weapons.Melee;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._Starlight.Weapons;

public sealed partial class NullstarRiftVisualSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprites = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var query = EntityQueryEnumerator<NullstarRiftComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var rift, out var sprite))
        {
            if (IsPaused(uid))
                continue;
            var t = Math.Max(0f, (float) (_timing.CurTime - rift.PhaseStarted).TotalSeconds);
            // Exported artwork spans 120x24 pixels within the 128x32 frame. Match the actual lane dimensions.
            var x = rift.Length / (120f / 32f);
            Layer((uid, sprite), 0, new Vector2(x, rift.Width / 0.75f),
                rift.Active ? 0 : 0.45f + 0.5f * Math.Clamp(t / Math.Max(0.01f, rift.WindupSeconds), 0, 1));
            Layer((uid, sprite), 1, new Vector2(x, rift.Width / 0.75f),
                rift.Active ? 1f - Math.Clamp(t / 0.10f, 0, 1) : 0);
            var close = 1f - Math.Clamp((t - rift.Duration) / Math.Max(0.01f, rift.CloseSeconds), 0, 1);
            var shimmer = 0.88f + 0.08f * MathF.Sin(t * 17f) * MathF.Sin(t * 11f);
            Layer((uid, sprite), 2, new Vector2(x, Math.Max(0.01f, rift.ResidualWidth / 0.75f * close)),
                rift.Active ? close * shimmer : 0);
            var cut = Math.Clamp(t / 0.24f, 0, 1);
            Layer((uid, sprite), 3, new Vector2(x * (0.65f + 0.35f * Math.Min(1, cut * 4)), rift.Width / 1.5f),
                rift.Active ? 1f - cut * cut : 0);
            _sprites.LayerSetRotation((uid, sprite), 3, Angle.FromDegrees(-18 + 36 * cut));
        }

        var motionQuery = EntityQueryEnumerator<NullstarCleaveMotionComponent, SpriteComponent>();
        while (motionQuery.MoveNext(out var uid, out var motion, out var sprite))
        {
            if (IsPaused(uid))
                continue;
            var t = Math.Max(0f, (float) (_timing.CurTime - motion.PhaseStarted).TotalSeconds);
            var charge = Math.Clamp(t / Math.Max(0.01f, motion.WindupSeconds), 0, 1);
            var fade = motion.Finished ? 1 - Math.Clamp(t / 0.35f, 0, 1) : 1;
            var coreScale = motion.Dashing ? 0.38f : 0.85f - 0.5f * charge;
            Layer((uid, sprite), 0, new Vector2(coreScale), fade * (motion.Dashing ? 0.9f : 0.3f + 0.7f * charge));
            _sprites.LayerSetOffset((uid, sprite), 0, new Vector2(motion.Dashing ? motion.Travelled : 0, 0));
            _sprites.LayerSetRotation((uid, sprite), 0, Angle.FromDegrees(60 * (1 - charge)));
            var length = motion.Dashing ? motion.Travelled : motion.Distance;
            Layer((uid, sprite), 1, new Vector2(Math.Max(0.01f, length / 3.75f), motion.Dashing ? 0.6f * fade + 0.1f : 0.18f),
                length < 0.03f ? 0 : motion.Dashing ? fade * 0.85f : 0.12f + 0.12f * charge);
            _sprites.LayerSetOffset((uid, sprite), 1, new Vector2(length / 2, 0));
        }
    }

    private void Layer(Entity<SpriteComponent?> ent, int index, Vector2 scale, float alpha)
    {
        _sprites.LayerSetVisible(ent, index, alpha > 0);
        if (alpha <= 0)
            return;
        _sprites.LayerSetScale(ent, index, scale);
        _sprites.LayerSetColor(ent, index, Color.White.WithAlpha(alpha));
    }
}
