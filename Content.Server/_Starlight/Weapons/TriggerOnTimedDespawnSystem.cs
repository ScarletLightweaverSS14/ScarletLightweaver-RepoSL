// 🌟Starlight🌟
using Content.Shared.Trigger.Systems;
using Robust.Shared.Spawners;

namespace Content.Server._Starlight.Weapons;

/// <summary>
/// When an entity with <see cref="TriggerOnTimedDespawnComponent"/> is about to be removed
/// by <see cref="SharedTimedDespawnSystem"/>, fire the trigger system so that any
/// <see cref="Content.Shared.Trigger.Components.SmokeOnTriggerComponent"/>,
/// <see cref="Content.Shared.Trigger.Components.EmitSoundOnTriggerComponent"/>, etc.
/// execute before the entity disappears.
///
/// Use this on projectiles that should detonate at end-of-range rather than only on wall collision.
/// </summary>
[RegisterComponent]
public sealed partial class TriggerOnTimedDespawnComponent : Component { }

public sealed class TriggerOnTimedDespawnSystem : EntitySystem
{
    [Dependency] private readonly TriggerSystem _trigger = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TriggerOnTimedDespawnComponent, TimedDespawnEvent>(OnDespawn);
    }

    private void OnDespawn(Entity<TriggerOnTimedDespawnComponent> ent, ref TimedDespawnEvent args)
    {
        _trigger.Trigger(ent.Owner);
    }
}
