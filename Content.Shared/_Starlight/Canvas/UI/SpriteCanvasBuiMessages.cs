using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.Canvas;

/// <summary>
/// Full UI state sent to the client when the canvas UI is opened or updated.
/// Contains all strokes so the client can fully reconstruct the drawing.
/// </summary>
[Serializable, NetSerializable]
public sealed class SpriteCanvasBuiState : BoundUserInterfaceState
{
    public readonly CanvasSizeType CanvasSize;
    public readonly List<CanvasStroke> Strokes;
    public readonly bool IsSigned;
    public readonly string? SignedBy;
    /// <summary>Net entities that have activated override mode on this canvas.</summary>
    public readonly HashSet<NetEntity> OverrideGranted;

    public SpriteCanvasBuiState(CanvasSizeType canvasSize, List<CanvasStroke> strokes,
        bool isSigned, string? signedBy, HashSet<NetEntity> overrideGranted)
    {
        CanvasSize      = canvasSize;
        Strokes         = strokes;
        IsSigned        = isSigned;
        SignedBy        = signedBy;
        OverrideGranted = overrideGranted;
    }
}

/// <summary>
/// Sent from client → server when the player commits their new strokes.
/// Only sends newly-drawn strokes (not the full history) to minimise bandwidth.
/// </summary>
[Serializable, NetSerializable]
public sealed class SpriteCanvasAddStrokesMsg : BoundUserInterfaceMessage
{
    public readonly List<CanvasStroke> NewStrokes;

    public SpriteCanvasAddStrokesMsg(List<CanvasStroke> newStrokes)
    {
        NewStrokes = newStrokes;
    }
}

/// <summary>
/// Sent from client → server to erase all strokes on the canvas.
/// </summary>
[Serializable, NetSerializable]
public sealed class SpriteCanvasClearMsg : BoundUserInterfaceMessage { }

/// <summary>
/// Sent from client → server to sign (lock) the canvas.
/// The server records the player's character name and prevents further edits
/// unless the player holds a <c>CanvasOverridePen</c>-tagged item.
/// </summary>
[Serializable, NetSerializable]
public sealed class SpriteCanvasSignMsg : BoundUserInterfaceMessage { }
