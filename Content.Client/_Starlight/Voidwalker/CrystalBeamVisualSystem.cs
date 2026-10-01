using System.Numerics;
using Content.Shared._Starlight.Voidwalker;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Client._Starlight.Voidwalker;

/// <summary>Owner mouse input and cosmetic entities. Damage and turn limits remain on the server.</summary>
public sealed partial class CrystalBeamVisualSystem : EntitySystem
{
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SpriteSystem _sprites = default!;
    [Dependency] private CrystalBeamTraceSystem _trace = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    internal readonly Dictionary<EntityUid, VisualState> Visuals = new();
    private CrystalBeamOverlay _overlay = default!;

    internal sealed class VisualState(CrystalBeamComponent beam)
    {
        public readonly CrystalBeamComponent Beam = beam;
        public readonly List<EntityUid> Focus = new();
        public MapCoordinates Origin;
        public Angle Direction = beam.Direction;
        public float Length;
        public float Age;
        public float Charge;
        public bool Visible;
        public TimeSpan NextAim;
        public TimeSpan NextImpact;
        public TimeSpan NextGrowth;
    }

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new CrystalBeamOverlay(this, _prototypes);
        _overlays.AddOverlay(_overlay);
        SubscribeLocalEvent<CrystalBeamComponent, ComponentShutdown>(OnShutdown);
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay(_overlay);
        foreach (var state in Visuals.Values)
            ClearFocus(state);
        Visuals.Clear();
        base.Shutdown();
    }

    private void OnShutdown(Entity<CrystalBeamComponent> ent, ref ComponentShutdown args)
    {
        if (Visuals.Remove(ent.Owner, out var state))
            ClearFocus(state);
    }

    private void ClearFocus(VisualState state)
    {
        foreach (var crystal in state.Focus)
            QueueDel(crystal);
        state.Focus.Clear();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        foreach (var state in Visuals.Values)
            state.Visible = false;

        var query = EntityQueryEnumerator<CrystalBeamComponent>();
        while (query.MoveNext(out var uid, out var beam))
        {
            if (beam.Caster is not { } caster || TerminatingOrDeleted(caster) || _timing.CurTime >= beam.EndAt)
                continue;
            var casterTransform = Transform(caster);
            if (casterTransform.MapID == MapId.Nullspace)
                continue;

            if (!Visuals.TryGetValue(uid, out var state))
            {
                state = new VisualState(beam);
                Visuals.Add(uid, state);
            }
            if (state.Focus.Count == 0)
            {
                for (var i = 0; i < Math.Clamp(beam.FocusCount, 0, 16); i++)
                {
                    var crystal = Spawn(beam.FocusPrototype, new EntityCoordinates(uid, Vector2.Zero));
                    EnsureComp<TimedDespawnComponent>(crystal).Lifetime = Math.Max(0.1f, (float) (beam.EndAt - _timing.CurTime).TotalSeconds);
                    state.Focus.Add(crystal);
                }
            }

            state.Visible = true;
            state.Origin = _transform.GetMapCoordinates(caster, xform: casterTransform);
            state.Age = (float) (_timing.CurTime - beam.StartedAt).TotalSeconds;
            state.Charge = Math.Clamp(state.Age / Math.Max(0.001f, (float) (beam.FireAt - beam.StartedAt).TotalSeconds), 0, 1);
            var turn = Angle.ShortestDistance(state.Direction, beam.Direction).Theta;
            state.Direction = (state.Direction + new Angle(turn * Math.Clamp(frameTime * 24, 0, 1))).Reduced();
            state.Length = beam.Firing ? _trace.Trace(beam, state.Origin, beam.Length, directionOverride: state.Direction) : 0;

            if (_player.LocalEntity == caster && _input.MouseScreenPosition.IsValid && _timing.CurTime >= state.NextAim)
            {
                var target = _eye.PixelToMap(_input.MouseScreenPosition);
                if (target.MapId == state.Origin.MapId)
                    RaiseNetworkEvent(new CrystalBeamAimEvent(GetNetEntity(uid), target));
                state.NextAim = _timing.CurTime + beam.AimInterval;
            }

            var radius = MathHelper.Lerp(beam.FocusRadius, beam.ChargedFocusRadius, state.Charge);
            for (var i = 0; i < state.Focus.Count; i++)
            {
                var crystal = state.Focus[i];
                if (TerminatingOrDeleted(crystal))
                    continue;
                var angle = new Angle(i * Math.Tau / state.Focus.Count + state.Age * 0.8);
                var offset = angle.ToVec() * radius;
                // Parent-space offset; changing caster/grid rotation must not rotate the whole visual unexpectedly.
                offset = (-_transform.GetWorldRotation(uid)).RotateVec(offset);
                _transform.SetCoordinates(crystal, new EntityCoordinates(uid, offset));
                _transform.SetWorldRotation(crystal, angle + Angle.FromDegrees(180));
                if (TryComp<SpriteComponent>(crystal, out var sprite))
                    _sprites.SetScale((crystal, sprite), new Vector2(0.4f + state.Charge * 0.35f));
            }

            if (!beam.Firing || state.Length <= 0.05f)
                continue;
            var endpoint = new MapCoordinates(state.Origin.Position + state.Direction.ToVec() * Math.Max(0, state.Length - 0.03f), state.Origin.MapId);
            if (beam.ImpactPrototype is { } impact && _timing.CurTime >= state.NextImpact)
            {
                Spawn(impact, endpoint);
                state.NextImpact = _timing.CurTime + beam.ImpactInterval;
            }
            if (beam.GrowthPrototype is { } growth && state.Length < beam.Range - 0.05f && _timing.CurTime >= state.NextGrowth)
            {
                Spawn(growth, endpoint);
                state.NextGrowth = _timing.CurTime + beam.GrowthInterval;
            }
        }

        // PVS departure can hide a channel without destroying it; don't leave local focusing crystals behind.
        foreach (var state in Visuals.Values)
        {
            if (!state.Visible)
                ClearFocus(state);
        }
    }
}
