using Content.Shared._Starlight.Canvas;
using Content.Shared.Interaction;
using Content.Shared.Tag;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Canvas;

/// <summary>
/// Server-side system for the sprite canvas.
/// Receives stroke data from clients, validates and stores it,
/// then broadcasts updated state to all players who have the UI open.
/// All drawing occurs client-side; the server only stores the final committed strokes.
/// </summary>
public sealed class SpriteCanvasSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui     = default!;
    [Dependency] private readonly TagSystem           _tag    = default!;
    [Dependency] private readonly IGameTiming         _timing = default!;

    /// <summary>Tag on pens that can be used on a signed canvas to enter override mode.</summary>
    private const string OverridePenTag = "CanvasOverridePen";

    /// <summary>Minimum time between accepted Save messages from the same player.</summary>
    private static readonly TimeSpan SaveCooldown = TimeSpan.FromSeconds(1.0);

    /// <summary>Maximum number of strokes accepted in a single Save message.</summary>
    private const int MaxStrokesPerMessage = 32;

    // Tracks the last accepted save time per actor to enforce the cooldown.
    private readonly Dictionary<EntityUid, TimeSpan> _lastSave = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpriteCanvasComponent, BeforeActivatableUIOpenEvent>(OnBeforeUIOpen);
        SubscribeLocalEvent<SpriteCanvasComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<SpriteCanvasComponent, BoundUIClosedEvent>(OnBuiClosed);
        SubscribeLocalEvent<SpriteCanvasComponent, SpriteCanvasAddStrokesMsg>(OnAddStrokes);
        SubscribeLocalEvent<SpriteCanvasComponent, SpriteCanvasClearMsg>(OnClear);
        SubscribeLocalEvent<SpriteCanvasComponent, SpriteCanvasSignMsg>(OnSign);
    }

    // ── Handlers ───────────────────────────────────────────────────────────────
    private void OnBeforeUIOpen(Entity<SpriteCanvasComponent> entity, ref BeforeActivatableUIOpenEvent args)
    {
        SendState(entity);
    }

    /// <summary>
    /// When a player uses an override pen on a signed canvas in the world, grant them
    /// override access and open (or bring to front) the canvas UI for them.
    /// </summary>
    private void OnInteractUsing(Entity<SpriteCanvasComponent> entity, ref InteractUsingEvent args)
    {
        if (!entity.Comp.IsSigned) return; // unsigned canvas: normal ActivatableUI click already works
        if (!_tag.HasTag(args.Used, OverridePenTag)) return;

        entity.Comp.OverrideGranted.Add(args.User);
        _ui.OpenUi(entity.Owner, SpriteCanvasUiKey.Key, args.User);
        SendState(entity);
        args.Handled = true;
    }

    /// <summary>Remove a player's override grant when they close the canvas UI.</summary>
    private void OnBuiClosed(Entity<SpriteCanvasComponent> entity, ref BoundUIClosedEvent args)
    {
        if (!args.UiKey.Equals(SpriteCanvasUiKey.Key)) return;
        entity.Comp.OverrideGranted.Remove(args.Actor);
        // No need to SendState here: the closing player won't receive it anyway,
        // and other viewers' edit access is unaffected.
    }

    private void OnAddStrokes(Entity<SpriteCanvasComponent> entity, ref SpriteCanvasAddStrokesMsg args)
    {
        var comp = entity.Comp;

        // If canvas is signed, only players who activated override mode may add strokes
        if (comp.IsSigned && !comp.OverrideGranted.Contains(args.Actor))
            return;

        // Rate-limit: one save per actor per SaveCooldown window
        var now = _timing.CurTime;
        if (_lastSave.TryGetValue(args.Actor, out var last) && now - last < SaveCooldown)
            return;
        _lastSave[args.Actor] = now;

        // Cap the number of strokes a single message may carry
        if (args.NewStrokes.Count > MaxStrokesPerMessage)
            args.NewStrokes.RemoveRange(MaxStrokesPerMessage, args.NewStrokes.Count - MaxStrokesPerMessage);

        var totalBefore = comp.Strokes.Count;

        foreach (var stroke in args.NewStrokes)
        {
            // Hard cap: refuse extra strokes once we're full
            if (comp.Strokes.Count >= comp.MaxStrokes)
                break;

            // Sanitise the point list
            if (stroke.Points.Count == 0)
                continue;

            if (stroke.Points.Count > comp.MaxPointsPerStroke)
                stroke.Points.RemoveRange(comp.MaxPointsPerStroke,
                    stroke.Points.Count - comp.MaxPointsPerStroke);

            // Clamp all normalised coordinates to [0, 1]
            for (var i = 0; i < stroke.Points.Count; i++)
            {
                var p = stroke.Points[i];
                stroke.Points[i] = new System.Numerics.Vector2(
                    Math.Clamp(p.X, 0f, 1f),
                    Math.Clamp(p.Y, 0f, 1f));
            }

            // Clamp brush size to reasonable bounds
            stroke.BrushSize = Math.Clamp(stroke.BrushSize, 1f, 50f);

            comp.Strokes.Add(stroke);
        }

        if (comp.Strokes.Count != totalBefore)
            SendState(entity);
    }

    private void OnClear(Entity<SpriteCanvasComponent> entity, ref SpriteCanvasClearMsg args)
    {
        var comp = entity.Comp;

        // Signed canvases may only be cleared by override-mode players
        if (comp.IsSigned && !comp.OverrideGranted.Contains(args.Actor))
            return;

        comp.Strokes.Clear();
        SendState(entity);
    }

    private void OnSign(Entity<SpriteCanvasComponent> entity, ref SpriteCanvasSignMsg args)
    {
        var comp = entity.Comp;

        // Already signed — nothing to do
        if (comp.IsSigned)
            return;

        // Record the signer's character name
        var signerName = MetaData(args.Actor).EntityName;
        comp.IsSigned  = true;
        comp.SignedBy   = signerName;

        SendState(entity);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    private void SendState(Entity<SpriteCanvasComponent> entity)
    {
        var comp = entity.Comp;

        // Convert runtime EntityUids to NetEntity so the client can identify them.
        var netOverride = new HashSet<NetEntity>(comp.OverrideGranted.Count);
        foreach (var uid in comp.OverrideGranted)
            netOverride.Add(GetNetEntity(uid));

        _ui.SetUiState(entity.Owner,
            SpriteCanvasUiKey.Key,
            new SpriteCanvasBuiState(comp.CanvasSize, comp.Strokes, comp.IsSigned, comp.SignedBy, netOverride));
    }
}
