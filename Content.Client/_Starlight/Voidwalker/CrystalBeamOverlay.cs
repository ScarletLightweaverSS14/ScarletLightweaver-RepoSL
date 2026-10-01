using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._Starlight.Voidwalker;

/// <summary>Angular, layered geometry with crystal facets, drawn at the game's native world scale.</summary>
public sealed class CrystalBeamOverlay : Robust.Client.Graphics.Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;
    private static readonly ProtoId<ShaderPrototype> Unshaded = "unshaded";
    private readonly CrystalBeamVisualSystem _system;
    private readonly ShaderInstance _shader;
    // Content assemblies must use verifiable IL; stackalloc is rejected by the client sandbox.
    // DrawPrimitives consumes the vertices immediately, so one managed buffer serves every facet.
    private readonly Vector2[] _vertices = new Vector2[6];

    public CrystalBeamOverlay(CrystalBeamVisualSystem system, IPrototypeManager prototypes)
    {
        _system = system;
        _shader = prototypes.Index(Unshaded).Instance();
        ZIndex = (int) Content.Shared.DrawDepth.DrawDepth.Effects;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(_shader);
        foreach (var state in _system.Visuals.Values)
        {
            if (!state.Visible || state.Origin.MapId != args.MapId)
                continue;
            var beam = state.Beam;
            var origin = state.Origin.Position;
            var direction = state.Direction.ToVec();
            var side = new Vector2(-direction.Y, direction.X);
            var pulse = 1 + Math.Clamp(beam.Flicker, 0, 0.5f) * MathF.Sin(state.Age * 43);
            var coreSize = (beam.Firing ? 0.2f : 0.05f + state.Charge * 0.15f) * pulse;
            Facet(handle, origin, direction * coreSize, side * coreSize, beam.InnerColor);
            Facet(handle, origin, direction * coreSize * 0.55f, side * coreSize * 0.55f, beam.CoreColor);
            if (!beam.Firing || state.Length <= 0)
                continue;

            // Small stepped changes make the edges feel fractured, not like a smooth gun laser.
            var segments = Math.Clamp((int) MathF.Ceiling(state.Length / 0.3f), 1, 100);
            for (var i = 0; i < segments; i++)
            {
                var start = origin + direction * (state.Length * i / segments);
                var end = origin + direction * (state.Length * (i + 1) / segments);
                var flicker = 0.8f + 0.2f * MathF.Sin(i * 2.1f + MathF.Floor(state.Age * 18));
                var halfWidth = beam.Width * 0.5f * flicker;
                Ribbon(handle, start, end, side * halfWidth, beam.OuterColor);
                Ribbon(handle, start, end, side * halfWidth * 0.6f, beam.InnerColor);
                Ribbon(handle, start, end, side * halfWidth * 0.18f, beam.CoreColor);
            }

            var spacing = Math.Max(0.2f, beam.ParticleSpacing);
            var count = Math.Min(64, (int) MathF.Ceiling(state.Length / spacing));
            for (var i = 0; i < count; i++)
            {
                var distance = (i * spacing + state.Age * beam.ParticleSpeed) % state.Length;
                var drift = MathF.Sin(i * 3.4f + state.Age * 7) * beam.Width * 0.6f;
                var position = origin + direction * distance + side * drift;
                // Keep facets inside the endpoint even at point-blank range.
                var extent = Math.Min(0.1f, Math.Min(distance, state.Length - distance));
                Facet(handle, position, direction * extent, side * 0.035f, beam.CoreColor);
            }
            var tip = origin + direction * state.Length;
            Facet(handle, tip - direction * 0.1f, direction * Math.Min(0.1f, state.Length * 0.5f), side * (0.18f * pulse), beam.InnerColor);
        }
        handle.UseShader(null);
    }

    private void Ribbon(DrawingHandleWorld handle, Vector2 start, Vector2 end, Vector2 side, Color color)
    {
        _vertices[0] = start - side;
        _vertices[1] = end - side;
        _vertices[2] = end + side;
        _vertices[3] = start - side;
        _vertices[4] = end + side;
        _vertices[5] = start + side;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _vertices, color);
    }

    private void Facet(DrawingHandleWorld handle, Vector2 center, Vector2 forward, Vector2 side, Color color)
    {
        _vertices[0] = center + forward;
        _vertices[1] = center + side;
        _vertices[2] = center - forward;
        _vertices[3] = center + forward;
        _vertices[4] = center - forward;
        _vertices[5] = center - side;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _vertices, color);
    }
}
