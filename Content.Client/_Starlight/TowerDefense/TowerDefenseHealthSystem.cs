using System.Numerics;
using Content.Shared._Starlight.TowerDefense;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._Starlight.TowerDefense;

public sealed partial class TowerDefenseHealthSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new TowerDefenseHealthOverlay(EntityManager));
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<TowerDefenseHealthOverlay>();
        base.Shutdown();
    }
}

public sealed class TowerDefenseHealthOverlay(IEntityManager entities) : Robust.Client.Graphics.Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    protected override void Draw(in OverlayDrawArgs args)
    {
        var transform = entities.System<SharedTransformSystem>();
        var query = entities.EntityQueryEnumerator<TowerDefenseEnemyComponent, TransformComponent>();
        while (query.MoveNext(out var health, out var xform))
        {
            if (xform.MapID != args.MapId || health.MaxHealth <= 0)
                continue;
            var position = transform.GetWorldPosition(xform);
            if (!args.WorldAABB.Contains(position))
                continue;
            var rotation = Matrix3Helpers.CreateRotation(-(args.Viewport.Eye?.Rotation ?? Angle.Zero));
            args.WorldHandle.SetTransform(rotation * Matrix3x2.CreateTranslation(position));
            args.WorldHandle.DrawRect(new Box2(-0.4f, 0.5f, 0.4f, 0.59f), Color.Black);
            var fraction = Math.Clamp(health.Health / health.MaxHealth, 0, 1);
            args.WorldHandle.DrawRect(new Box2(-0.4f, 0.5f, -0.4f + 0.8f * fraction, 0.59f), Color.LimeGreen);
        }
        args.WorldHandle.SetTransform(Matrix3x2.Identity);
    }
}
