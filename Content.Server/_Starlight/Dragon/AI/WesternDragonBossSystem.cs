using System.Linq;
using System.Numerics;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._Starlight.Dragon;
using Content.Shared.Actions;
using Content.Shared.CombatMode;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.NPC.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Starlight.Dragon;

public sealed partial class WesternDragonBossSystem : EntitySystem
{
    [Dependency] private NPCUtilitySystem _utility = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    private readonly record struct Enemy(EntityUid Uid, EntityCoordinates Coordinates, Vector2 Position, Vector2 Velocity, float Distance, bool Ranged);

    public override void Initialize()
    {
        SubscribeLocalEvent<WesternDragonBossComponent, DamageChangedEvent>(OnDamage);
        SubscribeLocalEvent<WesternDragonBossComponent, ComponentShutdown>(OnShutdown);
        InitializeDevour();
    }

    /// <summary>Called by HTN, not a second independently ticking AI loop.</summary>
    public bool Tick(EntityUid uid, NPCBlackboard blackboard)
    {
        if (!TryComp<WesternDragonBossComponent>(uid, out var boss) || !_npc.Enabled ||
            !HasComp<ActiveNPCComponent>(uid) || HasComp<ActorComponent>(uid) ||
            !TryComp<HTNComponent>(uid, out var htn) || !htn.Enabled ||
            !TryComp<MobStateComponent>(uid, out var state) || state.CurrentState != MobState.Alive)
            return false;

        var now = _timing.CurTime;
        if (now < boss.NextThink)
            return true;
        boss.NextThink = now + TimeSpan.FromSeconds(Math.Max(0.1f, boss.ThinkInterval));

        // While dashing, the jump system must be the only system driving movement.
        if (TryComp<WingDashComponent>(uid, out var dash) && dash.EndTime > now)
        {
            _steering.Unregister(uid);
            return true;
        }

        foreach (var attacker in boss.Threat.Keys.ToArray())
        {
            boss.Threat[attacker] *= 0.9f;
            if (Deleted(attacker) || boss.Threat[attacker] < 1)
                boss.Threat.Remove(attacker);
        }
        boss.RecentDamage *= 0.85f;

        var enemies = Observe(uid, blackboard);
        boss.VisibleEnemies = enemies.Count;
        boss.CloseEnemies = enemies.Count(e => e.Distance <= 2.5f);
        var health = HealthFraction(uid);
        boss.LowestHealth = Math.Min(boss.LowestHealth, health);
        CheckTaunts(uid, boss, health);

        if (TryDevour(uid, boss, blackboard, enemies))
            return true;

        if (enemies.Count == 0)
        {
            // Investigate the last observed position briefly, never track hidden enemies through walls.
            if (boss.Target is { } lost && !TerminatingOrDeleted(lost) &&
                TryComp<MobStateComponent>(lost, out var lostState) && lostState.CurrentState == MobState.Alive &&
                boss.LastKnownPosition is { } last && last.IsValid(EntityManager) &&
                _transform.GetMapId(last) == Transform(uid).MapID &&
                now - boss.LastSeen < TimeSpan.FromSeconds(boss.ForgetTargetAfter))
            {
                boss.Positioning = DragonPositioning.Search;
                var search = _steering.Register(uid, last);
                search.DirectMove = Transform(uid).GridUid == null;
                search.Range = 0.5f;
                return true;
            }
            return false;
        }

        if (!boss.Engaged)
        {
            boss.Engaged = true;
            boss.CircleDirection = _random.Prob(0.5f) ? 1 : -1;
            if (!boss.LairTauntSpoken && TryTaunt(uid, boss, "western-dragon-taunt-aggro"))
                boss.LairTauntSpoken = true;
        }

        var target = SelectTarget(boss, enemies);
        boss.Target = target.Uid;
        boss.LastSeen = now;
        boss.LastKnownPosition = target.Coordinates;
        blackboard.SetValue("Target", target.Uid);
        blackboard.SetValue("TargetCoordinates", target.Coordinates);
        // Visible in showhtn / VV for tuning without an enormous branching YAML tree.
        blackboard.SetValue("DragonCloseEnemies", (float) boss.CloseEnemies);
        blackboard.SetValue("DragonHealth", health);
        _combat.SetInCombatMode(uid, true);

        if (now < boss.RecoverUntil)
        {
            Position(uid, boss, target, health);
            return true;
        }

        if (now >= boss.NextAbility && TryAbility(uid, boss, enemies, target, health))
        {
            if (boss.LastAbility != DragonAbility.Dash)
                Position(uid, boss, target, health);
            return true;
        }

        // Use the normal melee validation/cooldowns, without NPCMeleeCombat's forced chase steering.
        if (_melee.TryGetWeapon(uid, out var weaponUid, out var weapon) && target.Distance <= weapon.Range)
            _melee.AttemptLightAttack(uid, weaponUid, weapon, target.Uid);

        Position(uid, boss, target, health);
        return true;
    }

    private List<Enemy> Observe(EntityUid uid, NPCBlackboard blackboard)
    {
        var origin = _transform.GetMapCoordinates(uid);
        var parent = Transform(uid).GridUid ?? Transform(uid).MapUid;
        if (parent == null)
            return new List<Enemy>();
        var frameVelocity = _physics.GetMapLinearVelocity(_transform.ToCoordinates(parent.Value, origin));
        var enemies = new List<Enemy>();
        foreach (var (target, _) in _utility.GetEntities(blackboard, "WesternDragonTargets", bestOnly: false).Entities.Take(24))
        {
            if (!TryComp<MobStateComponent>(target, out var mob) || mob.CurrentState != MobState.Alive)
                continue;
            var map = _transform.GetMapCoordinates(target);
            if (map.MapId != origin.MapId)
                continue;
            var ranged = HasComp<GunComponent>(target) ||
                         (_hands.TryGetActiveItem(target, out var held) && HasComp<GunComponent>(held));
            // Snapshot on the grid/map, never on a vehicle or the target itself.
            var coordinates = _transform.ToCoordinates(parent.Value, map);
            var velocity = _physics.GetMapLinearVelocity(target) - frameVelocity;
            if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y))
                velocity = Vector2.Zero;
            enemies.Add(new Enemy(target, coordinates, map.Position, velocity,
                Vector2.Distance(origin.Position, map.Position), ranged));
        }
        return enemies;
    }

    private Enemy SelectTarget(WesternDragonBossComponent boss, List<Enemy> enemies)
    {
        var best = enemies[0];
        var bestScore = float.MinValue;
        Enemy? current = null;
        var currentScore = 0f;
        foreach (var enemy in enemies)
        {
            var score = 12f / (1 + enemy.Distance) + (enemy.Ranged ? 2 : 0);
            score += boss.Threat.GetValueOrDefault(enemy.Uid) * 0.15f;
            score += Math.Clamp(1 - HealthFraction(enemy.Uid), 0, 1);
            if (enemy.Uid == boss.Target)
            {
                current = enemy;
                currentScore = score;
            }
            if (score > bestScore)
            {
                best = enemy;
                bestScore = score;
            }
        }

        if (current is { } retained &&
            (_timing.CurTime < boss.NextRetarget || bestScore < currentScore * 1.3f))
            return retained;

        if (boss.Target != best.Uid)
        {
            boss.NextRetarget = _timing.CurTime + TimeSpan.FromSeconds(boss.TargetCommitment);
            boss.NextPosition = _timing.CurTime;
        }
        return best;
    }

    private float HealthFraction(EntityUid uid)
    {
        if (!TryComp<DamageableComponent>(uid, out var damage) ||
            !_thresholds.TryGetIncapThreshold(uid, out var threshold) || threshold.Value <= 0)
            return 1;
        return Math.Clamp(1 - (float) damage.TotalDamage / (float) threshold.Value, 0, 1);
    }

    private void OnDamage(EntityUid uid, WesternDragonBossComponent boss, DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta == null)
            return;
        // Damage-free time survives disengaging, HTN resets, and player takeover.
        boss.DevourAvailableAt = _timing.CurTime + TimeSpan.FromSeconds(boss.DevourSafeTime);
        CancelDevour(boss);
        if (!HasComp<ActiveNPCComponent>(uid) || HasComp<ActorComponent>(uid))
            return;
        var damage = args.DamageDelta.DamageDict.Values.Sum(value => Math.Max(0, (float) value));
        boss.RecentDamage += damage;
        var attacker = args.Origin;
        if (TryComp<ProjectileComponent>(attacker, out var projectile))
            attacker = projectile.Shooter;
        if (attacker is not { } source || source == uid || Deleted(source) || _factions.IsEntityFriendly(uid, source))
            return;
        if (boss.Threat.Count < 32 || boss.Threat.ContainsKey(source))
            boss.Threat[source] = Math.Min(300, boss.Threat.GetValueOrDefault(source) + damage);
        if (damage >= 80)
            boss.NextRetarget = _timing.CurTime;
    }

    public void Stop(EntityUid uid, NPCBlackboard blackboard)
    {
        if (!TryComp<WesternDragonBossComponent>(uid, out var boss))
            return;
        CancelDevour(boss);
        _steering.Unregister(uid);
        if (!TerminatingOrDeleted(uid))
            _combat.SetInCombatMode(uid, false);
        boss.Target = null;
        if (boss.ComboTarget != null)
            EndCombo(boss);
        boss.LastKnownPosition = null;
        boss.Engaged = false;
        boss.Threat.Clear();
        boss.RecentDamage = 0;
        blackboard.Remove<EntityUid>("Target");
        blackboard.Remove<EntityCoordinates>("TargetCoordinates");
    }

    private void OnShutdown(EntityUid uid, WesternDragonBossComponent boss, ComponentShutdown args)
    {
        if (TryComp<HTNComponent>(uid, out var htn))
            Stop(uid, htn.Blackboard);
    }
}
