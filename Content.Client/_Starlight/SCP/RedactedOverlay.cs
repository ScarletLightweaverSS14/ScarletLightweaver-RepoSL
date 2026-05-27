using System.Numerics;
using Content.Shared._Starlight.SCP;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._Starlight.SCP;

public sealed class RedactedOverlay : Robust.Client.Graphics.Overlay
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly SharedTransformSystem _xformSys;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public RedactedOverlay()
    {
        IoCManager.InjectDependencies(this);
        _xformSys = _entityManager.System<SharedTransformSystem>();
        ZIndex = 201;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var worldAABB = args.WorldAABB;
        var handle    = args.ScreenHandle;
        var t         = _timing.RealTime.TotalSeconds;

        var query = _entityManager.EntityQueryEnumerator<RedactedComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            var worldPos = _xformSys.GetWorldPosition(xform);
            if (!worldAABB.Contains(worldPos))
                continue;

            var phase   = uid.Id * 1.7f;
            var jitterX = (float)(Math.Sin(t * 3.1 + phase)       * 0.8);
            var jitterY = (float)(Math.Sin(t * 2.3 + phase + 1.0) * 0.8);
            var pulse   = (float)(1.0 + Math.Sin(t * 1.8 + phase + 2.5) * 0.04);

            if (comp.FullBody)
            {
                // ── Full-body black rectangle ─────────────────────────────────
                var screenTL = _eyeManager.WorldToScreen(worldPos + new Vector2(-0.25f,  0.72f));
                var screenBR = _eyeManager.WorldToScreen(worldPos + new Vector2( 0.25f, -0.60f));

                // Smaller jitter for full-body so it doesn't look jittery
                var jx2 = jitterX * 0.5f;
                var jy2 = jitterY * 0.5f;
                var cx2  = (screenTL.X + screenBR.X) * 0.5f + jx2;
                var cy2  = (screenTL.Y + screenBR.Y) * 0.5f + jy2;
                var hw   = (screenBR.X - screenTL.X) * 0.5f * pulse;
                var hh   = (screenBR.Y - screenTL.Y) * 0.5f * pulse;
                handle.DrawRect(new UIBox2(cx2 - hw, cy2 - hh, cx2 + hw, cy2 + hh), Color.Black);
            }
            else
            {
                // ── Head-only black square ─────────────────────────────────────
                // 0.11f right, 0.28f up from entity origin
                var headWorld  = worldPos + new Vector2(0.03f, 0.28f);
                var headScreen = _eyeManager.WorldToScreen(headWorld);

                // Pixel scale: project 0.26 world units to the right
                var scaleRef = _eyeManager.WorldToScreen(headWorld + new Vector2(0.26f, 0f));
                var halfPx   = Math.Abs(scaleRef.X - headScreen.X);

                var cx    = headScreen.X + jitterX;
                var cy    = headScreen.Y + jitterY;
                var half2 = halfPx * pulse;
                handle.DrawRect(new UIBox2(cx - half2, cy - half2, cx + half2, cy + half2), Color.Black);
            }
        }
    }
}
