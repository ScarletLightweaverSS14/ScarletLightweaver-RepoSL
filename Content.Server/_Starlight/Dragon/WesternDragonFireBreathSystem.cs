using System.Numerics;
using Content.Shared._Starlight.Dragon;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Dragon;

public sealed partial class WesternDragonFireBreathSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private Robust.Server.Audio.AudioSystem _audio = default!;
    [Dependency] private WesternDragonFirePatchSystem _fire = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WesternDragonFireBreathComponent, WesternDragonFireBreathEvent>(OnFireBreath);
    }

    private void OnFireBreath(Entity<WesternDragonFireBreathComponent> ent, ref WesternDragonFireBreathEvent args)
    {
        var xform = Transform(ent);
        if (args.Handled || xform.GridUid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var target = _transform.ToMapCoordinates(args.Target);
        if (target.MapId != xform.MapID)
            return;

        var delta = _transform.ToCoordinates(gridUid, target).Position -
                    _transform.ToCoordinates(gridUid, _transform.GetMapCoordinates(ent)).Position;
        var origin = _map.GetTileRef(gridUid, grid, xform.Coordinates);
        if (delta.LengthSquared() < 0.01f || _turf.IsSpace(origin))
            return;

        // Quantize in grid space, so all eight directions also work on rotated shuttles.
        var angle = MathF.Round(MathF.Atan2(delta.Y, delta.X) / (MathF.PI / 4)) * (MathF.PI / 4);
        var forward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var waveUid = Spawn("WesternDragonFireBreathWave", _map.ToCenterCoordinates(gridUid, origin.GridIndices, grid));
        var wave = Comp<WesternDragonFireBreathWaveComponent>(waveUid);
        wave.NextPulse = _timing.CurTime;

        // A narrow mouth, wider middle, and rounded tip instead of a rectangular lane.
        // Plan the small footprint once; each tile is attempted only in its own stage.
        for (var x = -5; x <= 5; x++)
        for (var y = -5; y <= 5; y++)
        {
            var offset = new Vector2(x, y);
            var depth = Vector2.Dot(offset, forward);
            if (depth < 0.5f || depth > 5.1f || offset.LengthSquared() > 25.25f)
                continue;

            var width = MathF.Abs(offset.X * forward.Y - offset.Y * forward.X);
            var halfWidth = MathF.Min(0.5f + 0.35f * depth, (5.5f - depth) * 1.5f);
            if (width > halfWidth)
                continue;

            var stage = Math.Clamp((int) MathF.Floor(depth - 0.5f), 0, wave.Stages.Length - 1);
            wave.Stages[stage].Add(origin.GridIndices + new Vector2i(x, y));
        }

        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Animals/space_dragon_roar.ogg"), ent);
        args.Handled = true;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<WesternDragonFireBreathWaveComponent>();
        while (query.MoveNext(out var uid, out var wave))
        {
            if (_timing.CurTime < wave.NextPulse)
                continue;

            var xform = Transform(uid);
            if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            {
                QueueDel(uid);
                continue;
            }

            foreach (var tile in wave.Stages[wave.Stage])
            {
                var coordinates = _map.ToCenterCoordinates(gridUid, tile, grid);
                // Recheck as the front advances, so closing doors and walls still stop it.
                if (_interaction.InRangeUnobstructed(uid, coordinates, range: 0))
                    _fire.TrySpawnFire(gridUid, grid, tile);
            }

            wave.Stage++;
            if (wave.Stage == wave.Stages.Length)
                QueueDel(uid);
            else
                wave.NextPulse = _timing.CurTime + TimeSpan.FromSeconds(0.1);
        }
    }
}
