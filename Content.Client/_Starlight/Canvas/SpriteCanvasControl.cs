using System.Numerics;
using Content.Shared._Starlight.Canvas;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client._Starlight.Canvas;

/// <summary>
/// Custom Control that renders canvas strokes and captures mouse-draw input.
/// All drawing happens entirely client-side for instant, lag-free feedback.
/// Strokes are committed to the server only when the user presses Save.
/// </summary>
public sealed class SpriteCanvasControl : Control
{
    // ── Palette ────────────────────────────────────────────────────────────────
    public static readonly Color[] Palette =
    [
        Color.White,
        Color.Black,
        new Color(0.45f, 0.45f, 0.45f),   // Gray
        new Color(0.85f, 0.15f, 0.15f),   // Red
        new Color(0.95f, 0.45f, 0.05f),   // Orange
        new Color(0.95f, 0.90f, 0.10f),   // Yellow
        new Color(0.10f, 0.75f, 0.20f),   // Green
        new Color(0.05f, 0.70f, 0.70f),   // Cyan
        new Color(0.15f, 0.25f, 0.90f),   // Blue
        new Color(0.60f, 0.10f, 0.80f),   // Purple
        new Color(0.95f, 0.45f, 0.75f),   // Pink
        new Color(0.55f, 0.30f, 0.10f),   // Brown
        new Color(0.75f, 0.95f, 0.75f),   // Light-green
        new Color(0.65f, 0.80f, 0.95f),   // Light-blue
        new Color(0.95f, 0.75f, 0.85f),   // Light-pink
        new Color(0.70f, 0.05f, 0.10f),   // Dark-red
    ];

    // ── Brush sizes (canvas-pixel radius) ─────────────────────────────────────
    public static readonly float[] BrushSizes = { 2f, 6f, 14f };

    // ── State ──────────────────────────────────────────────────────────────────
    private readonly List<CanvasStroke> _committed   = new(); // saved to server
    private readonly List<CanvasStroke> _pending     = new(); // drawn this session, not yet saved
    private CanvasStroke?               _activeStroke;        // stroke being drawn right now

    private Color _selectedColor    = Color.Black;
    private float _selectedBrush    = BrushSizes[1];
    private bool  _isEraser         = false;
    private bool  _isLocked         = false; // true when canvas is signed, prevents local drawing

    private bool _mouseDown = false;
    private Vector2 _lastPoint;
    private Vector2? _cursorPos; // last known cursor position in UI-space, null when outside

    // Pre-allocated buffer for quad vertices (reused each draw call - avoids stackalloc).
    private readonly Vector2[] _quadBuf = new Vector2[6];

    // Minimum travel distance (in canvas pixels) before we add a new point.
    // Keeps point counts low while still giving smooth curves.
    private const float MinPointDistance = 2f;

    // ── Public API ─────────────────────────────────────────────────────────────
    /// <summary>Currently selected colour (accounts for eraser toggle).</summary>
    public Color ActiveColor => _isEraser ? Color.White : _selectedColor;

    /// <summary>Called when the user releases the mouse after drawing a stroke.</summary>
    public event Action<CanvasStroke>? OnStrokeCompleted;

    public SpriteCanvasControl()
    {
        RectClipContent    = true;
        MouseFilter        = MouseFilterMode.Stop;
        DefaultCursorShape = CursorShape.Crosshair; // crosshair hotspot is centered, arrow hotspot is the tip
        OnMouseExited      += _ => _cursorPos = null;
    }

    // ── Color / brush controls ─────────────────────────────────────────────────
    public void SetColor(Color color)
    {
        _selectedColor = color;
        _isEraser = false;
    }

    public void SetBrushSize(float radius) => _selectedBrush = radius;

    public void SetEraser(bool eraser) => _isEraser = eraser;

    public void SetLocked(bool locked)
    {
        _isLocked = locked;
        if (locked)
        {
            _mouseDown    = false;
            _activeStroke = null;
        }
    }

    // ── Stroke data management ─────────────────────────────────────────────────
    /// <summary>Replace all committed strokes (e.g. after receiving server state).</summary>
    /// Pending (unsaved) strokes and any active stroke are intentionally preserved
    /// so that an incoming server state update does not discard the user's in-progress work.
    public void SetStrokes(List<CanvasStroke> strokes)
    {
        _committed.Clear();
        _committed.AddRange(strokes);
        // Do NOT clear _pending or _activeStroke here.
        // Pending strokes have not been sent to the server yet, so they won't appear
        // in the incoming state.  Clearing them would silently discard unsaved work
        // whenever another player opens the canvas (which triggers a server broadcast).
    }

    /// <summary>Retrieve unsaved strokes and clear the pending list.</summary>
    public List<CanvasStroke> TakeAndClearPending()
    {
        var result = new List<CanvasStroke>(_pending);
        _pending.Clear();
        return result;
    }

    public bool HasPendingStrokes => _pending.Count > 0;

    // ── Input handling ─────────────────────────────────────────────────────────
    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick) return;
        if (_isLocked) return;

        _mouseDown = true;
        var canvasPos = ToNormalized(args.RelativePosition);

        _activeStroke = new CanvasStroke
        {
            Color     = ActiveColor,
            BrushSize = _selectedBrush,
            Points    = new List<Vector2> { canvasPos },
        };
        _lastPoint = args.RelativePosition;

        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function != EngineKeyFunctions.UIClick || !_mouseDown) return;

        _mouseDown = false;
        FinalizeStroke();
        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        _cursorPos = args.RelativePosition; // always track for brush preview

        if (!_mouseDown || _activeStroke == null) return;

        var delta = (args.RelativePosition - _lastPoint).Length();
        if (delta < MinPointDistance) return;

        _activeStroke.Points.Add(ToNormalized(args.RelativePosition));
        _lastPoint = args.RelativePosition;
    }

    private void FinalizeStroke()
    {
        if (_activeStroke == null || _activeStroke.Points.Count == 0)
        {
            _activeStroke = null;
            return;
        }

        _pending.Add(_activeStroke);
        OnStrokeCompleted?.Invoke(_activeStroke);
        _activeStroke = null;
    }

    // ── Coordinate helpers ─────────────────────────────────────────────────────
    // args.RelativePosition from mouse events is in logical (virtual-pixel) units,
    // i.e. it has already been divided by UIScale.  Width/Height are the logical
    // dimensions of this control, so we normalise against those.
    // DrawingHandleScreen works in screen-pixel space, so ToPixel multiplies the
    // 0-1 value by PixelWidth/PixelHeight to get the correct draw coordinate.
    private Vector2 ToNormalized(Vector2 logicalPos)
    {
        var w = Width  > 0 ? Width  : 1;
        var h = Height > 0 ? Height : 1;
        return new Vector2(
            Math.Clamp(logicalPos.X / w, 0f, 1f),
            Math.Clamp(logicalPos.Y / h, 0f, 1f));
    }

    private Vector2 ToPixel(Vector2 normalized)
        => new(normalized.X * PixelWidth, normalized.Y * PixelHeight);

    // ── Drawing ────────────────────────────────────────────────────────────────
    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        // White canvas background
        handle.DrawRect(PixelSizeBox, Color.White);

        foreach (var stroke in _committed)
            DrawStroke(handle, stroke);

        foreach (var stroke in _pending)
            DrawStroke(handle, stroke);

        if (_activeStroke != null)
            DrawStroke(handle, _activeStroke);

        // Brush cursor preview: semi-transparent brush-size circle + centre dot
        if (_cursorPos.HasValue)
        {
            var cp   = ToPixel(ToNormalized(_cursorPos.Value));
            var r    = _selectedBrush;
            var fill = _isEraser
                ? new Color(0.5f, 0.5f, 0.5f, 0.35f)
                : new Color(_selectedColor.R, _selectedColor.G, _selectedColor.B, 0.35f);
            handle.DrawCircle(cp, r, fill);
            // Small dark centre dot so you know the exact hotspot
            handle.DrawCircle(cp, 2f, new Color(0f, 0f, 0f, 0.75f));
        }
    }

    private void DrawStroke(DrawingHandleScreen handle, CanvasStroke stroke)
    {
        if (stroke.Points.Count == 0)
            return;

        var r     = stroke.BrushSize;
        var color = stroke.Color;

        // Single dot
        if (stroke.Points.Count == 1)
        {
            handle.DrawCircle(ToPixel(stroke.Points[0]), r, color);
            return;
        }

        // Thick line segments via triangle quads + circle caps
        var quad = new Span<Vector2>(_quadBuf);

        for (var i = 0; i < stroke.Points.Count - 1; i++)
        {
            var a = ToPixel(stroke.Points[i]);
            var b = ToPixel(stroke.Points[i + 1]);

            var diff = b - a;
            var len  = diff.Length();
            if (len < 0.001f) continue;

            // Perpendicular unit vector
            var perp = new Vector2(-diff.Y / len, diff.X / len) * r;

            // Two triangles forming a rectangle
            quad[0] = a - perp;
            quad[1] = a + perp;
            quad[2] = b + perp;
            quad[3] = a - perp;
            quad[4] = b + perp;
            quad[5] = b - perp;

            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, quad, color);

            // Round caps at each interior joint
            handle.DrawCircle(a, r, color);
        }

        // Cap at the final point
        handle.DrawCircle(ToPixel(stroke.Points[^1]), r, color);
    }
}
