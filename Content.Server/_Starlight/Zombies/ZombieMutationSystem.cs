using Content.Server.Actions;
using Content.Server.DoAfter;
using Content.Shared._Starlight.Zombies;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Zombies;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Zombies;

/// <summary>
/// Handles the zombie evolution / mutation system.
/// After 5–10 minutes as a zombie the player is offered a one-time grotesque mutation
/// that permanently augments their stats.
/// </summary>
public sealed class ZombieMutationSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;

    // DEBUG: 30 seconds flat (change back to 5-10 minutes for production)
    private const float MutationMinMinutes = 0.5f;
    private const float MutationMaxMinutes = 0.5f;

    // DoAfter duration (must stand still this long)
    private static readonly TimeSpan MutationDoAfterDuration = TimeSpan.FromSeconds(8);

    // Sound played during mutation
    private static readonly SoundPathSpecifier MutationSound = new("/Audio/Effects/gib1.ogg");

    private static readonly ProtoId<DamageModifierSetPrototype> ZombieArmoredModifier = "ZombieArmored";
    private static readonly ProtoId<DamageModifierSetPrototype> ZombieModifier = "Zombie";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ZombieMutationComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ZombieMutationComponent, ZombieEvolveActionEvent>(OnEvolveAction);
        SubscribeLocalEvent<ZombieMutationComponent, ZombieEvolveDoAfterEvent>(OnEvolveDoAfter);
        SubscribeLocalEvent<ZombieMutationComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnMapInit(Entity<ZombieMutationComponent> ent, ref MapInitEvent args)
    {
        var delay = TimeSpan.FromMinutes(_random.NextFloat(MutationMinMinutes, MutationMaxMinutes));
        ent.Comp.MutationUnlockTime = _timing.CurTime + delay;
    }

    private void OnShutdown(Entity<ZombieMutationComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.MutationActionEntity != null)
            _actions.RemoveAction(ent.Owner, ent.Comp.MutationActionEntity);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ZombieMutationComponent, ZombieComponent>();
        while (query.MoveNext(out var uid, out var mutation, out _))
        {
            if (mutation.HasMutated || mutation.MutationReady)
                continue;

            if (_timing.CurTime < mutation.MutationUnlockTime)
                continue;

            // Unlock the mutation for this zombie
            mutation.MutationReady = true;

            // Grant the action
            _actions.AddAction(uid, ref mutation.MutationActionEntity, mutation.MutationAction);

            // Notify the player
            _popup.PopupEntity(Loc.GetString("zombie-mutation-ready"), uid, uid, PopupType.LargeCaution);
        }
    }

    private void OnEvolveAction(Entity<ZombieMutationComponent> ent, ref ZombieEvolveActionEvent args)
    {
        if (ent.Comp.HasMutated)
            return;

        // Play the ling-like consuming sound before the DoAfter
        _audio.PlayPvs(MutationSound, ent.Owner);

        // Notify nearby about the horrifying transformation
        _popup.PopupEntity(Loc.GetString("zombie-mutation-begin"), ent.Owner, PopupType.LargeCaution);

        var doAfterArgs = new DoAfterArgs(EntityManager, ent.Owner, MutationDoAfterDuration,
            new ZombieEvolveDoAfterEvent(), ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = false,
            NeedHand = false,
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
        args.Handled = true;
    }

    private void OnEvolveDoAfter(Entity<ZombieMutationComponent> ent, ref ZombieEvolveDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        if (ent.Comp.HasMutated)
            return;

        args.Handled = true;
        ent.Comp.HasMutated = true;

        // Remove the action now that the mutation is complete
        if (ent.Comp.MutationActionEntity != null)
        {
            _actions.RemoveAction(ent.Owner, ent.Comp.MutationActionEntity);
            ent.Comp.MutationActionEntity = null;
        }

        // Pick a random mutation
        var mutations = new[]
        {
            ZombieMutation.Speed,
            ZombieMutation.Health,
            ZombieMutation.Healing,
            ZombieMutation.Armor,
            ZombieMutation.Virulence,
        };

        var chosen = _random.Pick(mutations);
        ApplyMutation(ent.Owner, chosen);
    }

    private void ApplyMutation(EntityUid uid, ZombieMutation mutation)
    {
        switch (mutation)
        {
            case ZombieMutation.Speed:
                if (TryComp<ZombieComponent>(uid, out var zombieSpd))
                {
                    // Remove the movement speed debuff — zombie now moves at full speed
                    zombieSpd.ZombieMovementSpeedDebuff = 1.0f;
                    _movementSpeed.RefreshMovementSpeedModifiers(uid);
                }
                _popup.PopupEntity(Loc.GetString("zombie-mutation-complete-speed"), uid, uid, PopupType.LargeCaution);
                break;

            case ZombieMutation.Health:
                if (TryComp<MobThresholdsComponent>(uid, out var threshComp))
                {
                    var healthBoost = FixedPoint2.New(30);
                    var boosts = new List<(FixedPoint2 NewValue, MobState State)>();
                    foreach (var state in new[] { MobState.Critical, MobState.Dead })
                    {
                        if (_mobThreshold.TryGetThresholdForState(uid, state, out var cur, threshComp))
                            boosts.Add((cur.Value + healthBoost, state));
                    }
                    foreach (var (newValue, state) in boosts)
                        _mobThreshold.SetMobStateThreshold(uid, newValue, state, threshComp);
                }
                _popup.PopupEntity(Loc.GetString("zombie-mutation-complete-health"), uid, uid, PopupType.LargeCaution);
                break;

            case ZombieMutation.Healing:
                if (TryComp<ZombieComponent>(uid, out var zombieHeal))
                {
                    // Double all passive healing values
                    var doubled = new DamageSpecifier();
                    foreach (var (type, amount) in zombieHeal.PassiveHealing.DamageDict)
                        doubled.DamageDict[type] = amount * 2;
                    zombieHeal.PassiveHealing = doubled;
                }
                _popup.PopupEntity(Loc.GetString("zombie-mutation-complete-healing"), uid, uid, PopupType.LargeCaution);
                break;

            case ZombieMutation.Armor:
                // Switch damage modifier set to the armored variant
                _damageable.SetDamageModifierSetId(uid, ZombieArmoredModifier);
                _popup.PopupEntity(Loc.GetString("zombie-mutation-complete-armor"), uid, uid, PopupType.LargeCaution);
                break;

            case ZombieMutation.Virulence:
                if (TryComp<ZombieComponent>(uid, out var zombieVir))
                    zombieVir.BaseZombieInfectionChance = 1.0f;
                _popup.PopupEntity(Loc.GetString("zombie-mutation-complete-virulence"), uid, uid, PopupType.LargeCaution);
                break;
        }
    }

    private enum ZombieMutation
    {
        Speed,
        Health,
        Healing,
        Armor,
        Virulence,
    }
}
