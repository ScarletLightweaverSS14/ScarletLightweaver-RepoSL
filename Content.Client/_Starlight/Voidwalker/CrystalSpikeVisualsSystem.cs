using System.Numerics;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;

namespace Content.Client._Starlight.Voidwalker;

public sealed partial class CrystalSpikeVisualsSystem : EntitySystem
{
    [Dependency] private AnimationPlayerSystem _animations = default!;
    [Dependency] private SpriteSystem _sprites = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CrystalSpikeVisualsComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<CrystalSpikeVisualsComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite) || ent.Comp.RiseTime <= 0)
            return;

        var scale = sprite.Scale;
        var offset = sprite.Offset;
        var startScale = scale * new Vector2(0.65f, 0.1f);
        var startOffset = offset - new Vector2(0, ent.Comp.RiseOffset);
        var settleTime = MathF.Max(0, ent.Comp.SettleTime);
        var peakScale = settleTime > 0 ? scale * ent.Comp.OvershootScale : scale;
        _sprites.SetScale((ent, sprite), startScale);
        _sprites.SetOffset((ent, sprite), startOffset);
        var scaleTrack = new AnimationTrackComponentProperty
        {
            ComponentType = typeof(SpriteComponent),
            Property = nameof(SpriteComponent.Scale),
            InterpolationMode = AnimationInterpolationMode.Linear,
            KeyFrames =
            {
                new AnimationTrackProperty.KeyFrame(startScale, 0),
                new AnimationTrackProperty.KeyFrame(peakScale, ent.Comp.RiseTime),
            },
        };
        if (settleTime > 0)
            scaleTrack.KeyFrames.Add(new AnimationTrackProperty.KeyFrame(scale, settleTime));

        var animation = new Animation
        {
            Length = TimeSpan.FromSeconds(ent.Comp.RiseTime + settleTime),
            AnimationTracks =
            {
                scaleTrack,
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(startOffset, 0),
                        new AnimationTrackProperty.KeyFrame(offset, ent.Comp.RiseTime),
                    },
                },
            },
        };
        _animations.Play((ent, EnsureComp<AnimationPlayerComponent>(ent)), animation, "voidwalker-crystal-rise");
    }
}
