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
    public override void Initialize()
    {
        // The relay system wraps events as StatusEffectRelayedEvent<T> before raising on effect entities
        SubscribeLocalEvent<PredatorHuntComponent, StatusEffectRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnRelayRefreshSpeed);
    }

    private void OnRelayRefreshSpeed(Entity<PredatorHuntComponent> ent, ref StatusEffectRelayedEvent<RefreshMovementSpeedModifiersEvent> ev)
    {
        ev.Args.ModifySpeed(ent.Comp.SpeedModifier, ent.Comp.SpeedModifier);
    }
}
