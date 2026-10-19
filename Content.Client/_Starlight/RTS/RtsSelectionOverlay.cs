using System.Numerics;
using Content.Shared._Starlight.RTS;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._Starlight.RTS;

public sealed class RtsSelectionOverlay : Robust.Client.Graphics.Overlay
{
    private readonly RtsCommanderSystem _commander;
    private readonly IEntityManager _entities;
    private readonly SharedTransformSystem _transform;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public RtsSelectionOverlay(RtsCommanderSystem commander, IEntityManager entities, SharedTransformSystem transform)
    {
        _commander = commander;
        _entities = entities;
        _transform = transform;
        ZIndex = 100;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_commander.Active)
            return;

        var handle = args.WorldHandle;
        foreach (var uid in _commander.Selected)
        {
            if (!_entities.TryGetComponent(uid, out TransformComponent? xform) || xform.MapID != args.MapId ||
                !_entities.TryGetComponent(uid, out RtsUnitComponent? unit))
                continue;

            var color = unit.State == RtsUnitState.Blocked ? Color.Orange : Color.FromHex("#7CF6C3");
            var position = _transform.GetWorldPosition(xform);
            handle.DrawCircle(position, 0.48f, color.WithAlpha(0.15f));
            handle.DrawCircle(position, 0.48f, color, false);
        }

        if (_commander.GetDragBox(args.MapId) is { } box)
        {
            handle.DrawRect(box, Color.FromHex("#7CF6C3").WithAlpha(0.1f));
            handle.DrawRect(box, Color.FromHex("#7CF6C3"), false);
        }

        if (_commander.OrderMarker is { } marker && marker.MapId == args.MapId)
        {
            var color = Color.FromHex("#98DFF3");
            handle.DrawCircle(marker.Position, 0.32f, color, false);
            handle.DrawLine(marker.Position - new Vector2(0.5f, 0), marker.Position + new Vector2(0.5f, 0), color);
            handle.DrawLine(marker.Position - new Vector2(0, 0.5f), marker.Position + new Vector2(0, 0.5f), color);
        }
    }
}
