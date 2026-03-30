using Content.Server.EnergyDome;
using Content.Server.Mech.Systems;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Mech;
using Content.Shared.Mech.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Containers;

namespace Content.Server.Mech;

/// <summary>
/// Handles the Durand's built-in turtle shield:
/// - Provides the ActionToggleDome action to the pilot when they enter the mech.
/// - Passively drains mech reactor energy every tick while the shield is active.
/// - Drains mech energy when the shield absorbs damage (handled in EnergyDomeSystem).
/// - Turns the shield off automatically when the reactor runs dry.
/// - Applies a heavy movement speed penalty while the shield is active.
/// </summary>
public sealed class MechBuiltInShieldSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly MechSystem _mech = default!;
    [Dependency] private readonly EnergyDomeSystem _dome = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MechBuiltInShieldComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<MechBuiltInShieldComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<MechBuiltInShieldComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<MechBuiltInShieldComponent, BeforeDamageChangedEvent>(OnBeforeDamage);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<MechBuiltInShieldComponent, MechComponent, EnergyDomeGeneratorComponent>();
        while (query.MoveNext(out var uid, out var shieldComp, out var mechComp, out var genComp))
        {
            // Detect toggle transitions and refresh movement speed accordingly.
            if (genComp.Enabled != shieldComp.ShieldWasActive)
            {
                shieldComp.ShieldWasActive = genComp.Enabled;
                _movement.RefreshMovementSpeedModifiers(uid);
            }

            if (!genComp.Enabled)
                continue;

            if (!_mech.TryChangeEnergy(uid, -(shieldComp.PassiveDrainRate * frameTime), mechComp))
                _dome.Toggle(uid, false, genComp);
        }
    }

    private void OnRefreshSpeed(EntityUid uid, MechBuiltInShieldComponent shieldComp, RefreshMovementSpeedModifiersEvent args)
    {
        if (!TryComp<EnergyDomeGeneratorComponent>(uid, out var genComp) || !genComp.Enabled)
            return;

        args.ModifySpeed(shieldComp.ShieldSpeedMultiplier);
    }

    /// <summary>
    /// While the shield is active, cancel all incoming damage to the mech itself.
    /// The dome entity absorbs it instead via its own Damageable + EnergyDomeSystem.
    /// </summary>
    private void OnBeforeDamage(EntityUid uid, MechBuiltInShieldComponent shieldComp, ref BeforeDamageChangedEvent args)
    {
        if (!TryComp<EnergyDomeGeneratorComponent>(uid, out var genComp) || !genComp.Enabled)
            return;

        args.Cancelled = true;
    }

    /// <summary>
    /// When a pilot enters the mech-pilot-slot, give them the shield toggle action.
    /// </summary>
    private void OnInserted(EntityUid mech, MechBuiltInShieldComponent shieldComp, EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != "mech-pilot-slot")
            return;

        _actions.AddAction(args.Entity, ref shieldComp.ShieldActionEntity, shieldComp.ShieldAction, mech);
    }

    /// <summary>
    /// When the pilot is ejected, clean up the shield action and restore movement speed.
    /// </summary>
    private void OnRemoved(EntityUid mech, MechBuiltInShieldComponent shieldComp, EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != "mech-pilot-slot")
            return;

        _actions.RemoveProvidedActions(args.Entity, mech);
        _movement.RefreshMovementSpeedModifiers(mech);
    }

}
