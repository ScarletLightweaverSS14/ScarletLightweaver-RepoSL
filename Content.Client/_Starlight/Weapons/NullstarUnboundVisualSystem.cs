using System.Numerics;
using Content.Shared._Starlight.Weapons.Melee;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._Starlight.Weapons;

/// <summary>Four inexpensive sprite layers, synchronized to the server's expanding wave.</summary>
public sealed partial class NullstarUnboundVisualSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprites = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var query = EntityQueryEnumerator<NullstarUnboundWaveComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var wave, out var sprite))
        {
            var t = Math.Max(0f, (float) (_timing.CurTime - wave.StartTime).TotalSeconds);
            // Layer order is defined by EffectNullstarUnbound. The bright ring is approximately 44px in radius.
            var radius = wave.Radius * Math.Clamp(t / Math.Max(0.01f, wave.ExpansionSeconds), 0f, 1f);
            Layer((uid, sprite), 0, new Vector2(Math.Max(0.01f, radius / 1.375f)), Fade(t, 0.5f, 1.05f));
            Layer((uid, sprite), 1, new Vector2(0.15f + 1.55f * Math.Clamp(t / 0.55f, 0f, 1f)), Fade(t, 0.35f, 0.9f));
            Layer((uid, sprite), 2, new Vector2(0.3f + 0.9f * Math.Clamp(t / 0.08f, 0f, 1f)), Fade(t, 0.08f, 0.3f));
            Layer((uid, sprite), 3, new Vector2(Math.Max(0.01f, Fade(t, 0.7f, 2.8f)), 1f - 0.3f * Math.Clamp(t / 3f, 0f, 1f)), Fade(t, 1.8f, 2.9f));
        }
    }

    private static float Fade(float t, float start, float end) => 1f - Math.Clamp((t - start) / (end - start), 0f, 1f);

    private void Layer(Entity<SpriteComponent?> ent, int index, Vector2 scale, float alpha)
    {
        _sprites.LayerSetVisible(ent, index, alpha > 0);
        _sprites.LayerSetScale(ent, index, scale);
        _sprites.LayerSetColor(ent, index, Color.White.WithAlpha(alpha));
    }
}
