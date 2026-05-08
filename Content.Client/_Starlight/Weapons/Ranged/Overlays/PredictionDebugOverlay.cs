using System.Numerics;
using Content.Client.Stylesheets;
using Content.Client.Weapons.Ranged.Systems;
using Content.Shared._Starlight.Weapons.Ranged.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._Starlight.Weapons.Ranged.Overlays;

/// <summary>
/// Debug overlay for gun prediction.  Toggle with:  <c>gun.prediction_debug true</c>
///
/// Draws a corner panel showing local RTT / fake-lag / last predicted shot,
/// plus world-space labels above test dummies and other players.
/// </summary>
internal sealed class PredictionDebugOverlay : Robust.Client.Graphics.Overlay
{
    private readonly IEntityManager _entMan;
    private readonly IEyeManager _eye;
    private readonly IUserInterfaceManager _ui;
    private readonly IPlayerManager _player;
    private readonly IClientNetManager _net;
    private readonly IConfigurationManager _cfg;
    private readonly IGameTiming _timing;
    private readonly GunSystem _gunSystem;
    private readonly EntityLookupSystem _lookup;
    private readonly SharedTransformSystem _xform;
    private readonly Font _font;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public PredictionDebugOverlay(
        IEntityManager entMan,
        IEyeManager eye,
        IUserInterfaceManager ui,
        IPlayerManager player,
        IClientNetManager net,
        IConfigurationManager cfg,
        IGameTiming timing,
        GunSystem gunSystem,
        IResourceCache res)
    {
        _entMan    = entMan;
        _eye       = eye;
        _ui        = ui;
        _player    = player;
        _net       = net;
        _cfg       = cfg;
        _timing    = timing;
        _gunSystem = gunSystem;
        _lookup    = entMan.System<EntityLookupSystem>();
        _xform     = entMan.System<SharedTransformSystem>();
        ZIndex     = 201;
        _font      = res.NotoStack(size: 8);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle  = args.ScreenHandle;
        var uiScale = _ui.RootControl.UIScale;
        var lineH   = 14f * uiScale;

        var fakeLagMin  = (int)(_cfg.GetCVar(Robust.Shared.CVars.NetFakeLagMin)  * 1000f);
        var fakeLagRand = (int)(_cfg.GetCVar(Robust.Shared.CVars.NetFakeLagRand) * 1000f);
        var rtt = _net.ServerChannel?.Ping ?? 0;

        // ─── Corner panel (top-left, below the engine FPS counter) ───────────────
        var px = 10f * uiScale;
        var py = 60f * uiScale;

        DrawLine(handle, ref px, ref py, lineH, "── PREDICTION DEBUG ──", Color.White.WithAlpha(0.85f), uiScale);

        var rttColor = rtt switch { < 60 => Color.Lime, < 120 => Color.Yellow, _ => Color.OrangeRed };
        DrawLine(handle, ref px, ref py, lineH, $"Ping:    {rtt} ms", rttColor, uiScale);

        if (fakeLagMin > 0 || fakeLagRand > 0)
            DrawLine(handle, ref px, ref py, lineH, $"FakeLag: +{fakeLagMin} ms  ±{fakeLagRand} ms", Color.Orange, uiScale);

        // Flash "PRED ●" for 0.5 s after each predicted shot
        if (_gunSystem.LastPredictionRealTime is { } predTime)
        {
            var age = (_timing.RealTime - predTime).TotalSeconds;
            if (age < 0.5)
            {
                var alpha = 1f - (float)(age / 0.5);
                DrawLine(handle, ref px, ref py, lineH, "PRED  ●", Color.Lime.WithAlpha(alpha), uiScale);
            }
        }

        // ─── World-space labels above entities ───────────────────────────────────
        var localEnt = _player.LocalEntity;
        var viewport = args.WorldAABB;
        var entities = new HashSet<EntityUid>();
        _lookup.GetEntitiesIntersecting(args.MapId, viewport, entities);

        foreach (var uid in entities)
        {
            if (!_entMan.EntityExists(uid))
                continue;

            var isDummy = _entMan.HasComponent<PredictionDummyComponent>(uid);
            var isOtherPlayer = uid != localEnt && _entMan.HasComponent<ActorComponent>(uid);

            if (!isDummy && !isOtherPlayer)
                continue;

            var aabb = _lookup.GetWorldAABB(uid);
            if (!aabb.Intersects(in viewport))
                continue;

            var screenPos = _eye.WorldToScreen(new Vector2(aabb.Center.X, aabb.Top + 0.5f)).Rounded();

            if (isDummy)
            {
                DrawCentred(handle, screenPos, "[DUMMY]", Color.Yellow, uiScale);
            }
            else
            {
                var name = _entMan.GetComponent<MetaDataComponent>(uid).EntityName;
                DrawCentred(handle, screenPos, $"PLAYER: {name}", Color.Cyan, uiScale);
            }
        }
    }

    // Draws one line of text at (px, py) and advances py by lineH.
    private void DrawLine(DrawingHandleScreen handle, ref float px, ref float py, float lineH, string text, Color color, float scale)
    {
        handle.DrawString(_font, new Vector2(px, py), text, scale, color);
        py += lineH;
    }

    // Draws text centred horizontally around a screen position.
    private void DrawCentred(DrawingHandleScreen handle, Vector2 pos, string text, Color color, float scale)
    {
        var approxWidth = text.Length * 5f * scale;
        handle.DrawString(_font, pos - new Vector2(approxWidth, 0f), text, scale, color);
    }
}

