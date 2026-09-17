using System.Numerics;
using Content.Shared.Humanoid;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Map;

namespace Content.Server._Starlight.Dragon;

public sealed partial class PredatorHuntVisualSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<PredatorHuntVisualComponent, StatusEffectAppliedEvent>(OnApplied);
        SubscribeLocalEvent<PredatorHuntVisualComponent, StatusEffectRemovedEvent>(OnRemoved);
        SubscribeLocalEvent<PredatorHuntVisualComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnApplied(Entity<PredatorHuntVisualComponent> ent, ref StatusEffectAppliedEvent args)
    {
        if (ent.Comp.Mark != null || !HasComp<HumanoidAppearanceComponent>(args.Target))
            return;

        ent.Comp.Mark = SpawnAttachedTo("WesternDragonRoarMark", new EntityCoordinates(args.Target, Vector2.Zero));
    }

    private void ClearMark(Entity<PredatorHuntVisualComponent> ent)
    {
        if (ent.Comp.Mark is { } mark)
            QueueDel(mark);
        ent.Comp.Mark = null;
    }

    private void OnRemoved(Entity<PredatorHuntVisualComponent> ent, ref StatusEffectRemovedEvent args) => ClearMark(ent);
    private void OnShutdown(Entity<PredatorHuntVisualComponent> ent, ref ComponentShutdown args) => ClearMark(ent);
}
