using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.Canvas;

/// <summary>
/// Stores all drawing strokes for a sprite canvas item.
/// Strokes are only kept server-side and sent to clients via BUI state on demand.
/// </summary>
[RegisterComponent]
public sealed partial class SpriteCanvasComponent : Component
{
    /// <summary>
    /// Physical size of the canvas in pixels (UI display area).
    /// </summary>
    [DataField]
    public CanvasSizeType CanvasSize = CanvasSizeType.Small;

    /// <summary>
    /// Maximum number of strokes allowed on this canvas.
    /// </summary>
    [DataField]
    public int MaxStrokes = 512;

    /// <summary>
    /// Maximum number of points stored per stroke (server-enforced).
    /// </summary>
    [DataField]
    public int MaxPointsPerStroke = 64;

    /// <summary>
    /// All strokes on this canvas. Server-side only — not component-networked.
    /// Sent to clients via BUI state when they open the UI.
    /// </summary>
    public readonly List<CanvasStroke> Strokes = new();

    /// <summary>
    /// When true the canvas is signed and can no longer be drawn on by normal users.
    /// Only players who activated override mode by using an override pen on the canvas
    /// in the world (InteractUsing) are allowed to add strokes.
    /// </summary>
    [DataField]
    public bool IsSigned = false;

    /// <summary>Character name of whoever signed the canvas.</summary>
    [DataField]
    public string? SignedBy = null;

    /// <summary>
    /// Players who have activated override mode by clicking the canvas with an override pen.
    /// Cleared per-player when their BUI closes. Not persisted (runtime state only).
    /// </summary>
    public readonly HashSet<EntityUid> OverrideGranted = new();
}

/// <summary>
/// Canvas pixel-dimension tiers.
/// </summary>
[Serializable, NetSerializable]
public enum CanvasSizeType : byte
{
    Small  = 0, // 500×500
    Medium = 1, // 1000×1000
    Large  = 2, // 1500×1500
}

/// <summary>
/// A single continuous brush stroke on the canvas.
/// Points are stored as normalised (0–1) coordinates so the data is
/// independent of the display resolution at which it is rendered.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class CanvasStroke
{
    /// <summary>Ink colour (RGBA).</summary>
    [DataField]
    public Color Color = Color.Black;

    /// <summary>Brush radius in canvas pixels (see <see cref="CanvasSizeType"/>).</summary>
    [DataField]
    public float BrushSize = 4f;

    /// <summary>
    /// Ordered list of points that make up the stroke path.
    /// Each component is in the range 0–1 (normalised to canvas dimensions).
    /// </summary>
    [DataField]
    public List<Vector2> Points = new();
}
