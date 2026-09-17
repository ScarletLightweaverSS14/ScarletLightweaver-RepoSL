using Content.Shared.Movement.Systems;
using Content.Shared.StatusEffectNew;
using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Dragon;

[RegisterComponent, NetworkedComponent]
public sealed partial class PredatorHuntComponent : Component
{
    [DataField]
    public float SpeedModifier = 0.65f;
}

public sealed partial class PredatorHuntSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PredatorHuntComponent, StatusEffectRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnRefreshSpeed);
        SubscribeLocalEvent<PredatorHuntComponent, StatusEffectAppliedEvent>(OnApplied);
        SubscribeLocalEvent<PredatorHuntComponent, StatusEffectRemovedEvent>(OnRemoved);
    }

    private void OnRefreshSpeed(Entity<PredatorHuntComponent> ent, ref StatusEffectRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        args.Args.ModifySpeed(ent.Comp.SpeedModifier, ent.Comp.SpeedModifier);
    }

    private void OnApplied(Entity<PredatorHuntComponent> ent, ref StatusEffectAppliedEvent args)
        => _movement.RefreshMovementSpeedModifiers(args.Target);

    private void OnRemoved(Entity<PredatorHuntComponent> ent, ref StatusEffectRemovedEvent args)
        => _movement.RefreshMovementSpeedModifiers(args.Target);
}
