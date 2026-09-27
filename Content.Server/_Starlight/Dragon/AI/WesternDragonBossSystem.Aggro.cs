using System.Linq;
using System.Numerics;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Player;

namespace Content.Server._Starlight.Dragon;

public sealed partial class WesternDragonBossSystem
{
    private EntityUid? ResolveAttacker(EntityUid? origin)
    {
        if (TryComp<ProjectileComponent>(origin, out var projectile))
            origin = projectile.Shooter;

        // Hitscan damage names the gun as its origin; only a held item identifies its wielder.
        if (origin is { } item && !TerminatingOrDeleted(item) &&
            _containers.TryGetContainingContainer(item, out var container) &&
            _hands.TryGetHand(container.Owner, container.ID, out _))
            return container.Owner;

        return origin;
    }

    private void OnAttacked(EntityUid uid, WesternDragonBossComponent boss, ref BeforeDamageChangedEvent args)
    {
        // Observe incoming attacks before armor removes their damage, including heat-immune laser hits.
        if (args.Cancelled || !args.Damage.DamageDict.Values.Any(value => value > 0) ||
            !_npc.Enabled || !HasComp<ActiveNPCComponent>(uid) || HasComp<ActorComponent>(uid) ||
            !TryComp<HTNComponent>(uid, out var htn) || !htn.Enabled ||
            !TryComp<MobStateComponent>(uid, out var mob) || mob.CurrentState != MobState.Alive ||
            ResolveAttacker(args.Origin) is not { } source || source == uid || TerminatingOrDeleted(source) ||
            !TryComp<MobStateComponent>(source, out var sourceMob) || sourceMob.CurrentState != MobState.Alive ||
            _factions.IsEntityFriendly(uid, source))
            return;

        var origin = _transform.GetMapCoordinates(uid);
        var attacker = _transform.GetMapCoordinates(source);
        var parent = Transform(uid).GridUid ?? Transform(uid).MapUid;
        if (parent == null || attacker.MapId != origin.MapId ||
            Vector2.DistanceSquared(origin.Position, attacker.Position) > boss.CounterplayRange * boss.CounterplayRange)
            return;

        boss.RecentAttacker = source;
        boss.RecentAttackPosition = _transform.ToCoordinates(parent.Value, attacker);
        boss.RecentAttackUntil = _timing.CurTime + TimeSpan.FromSeconds(4);
        if (source == boss.Target)
            boss.PursuitAttackedUntil = boss.RecentAttackUntil;
    }

    /// <summary>Lets HTN react to an actual attack outside its ordinary visual target query.</summary>
    public bool HasRecentAttacker(EntityUid uid)
        => TryComp<WesternDragonBossComponent>(uid, out var boss) &&
           _timing.CurTime < boss.RecentAttackUntil && boss.RecentAttacker is { } attacker &&
           !TerminatingOrDeleted(attacker) &&
           TryComp<MobStateComponent>(attacker, out var mob) && mob.CurrentState == MobState.Alive &&
           !_factions.IsEntityFriendly(uid, attacker) &&
           boss.RecentAttackPosition is { } position && position.IsValid(EntityManager) &&
           _transform.GetMapId(position) == Transform(uid).MapID && Transform(attacker).MapID == Transform(uid).MapID;

    private void ObserveRecentAttacker(EntityUid uid, WesternDragonBossComponent boss, List<Enemy> enemies)
    {
        if (!HasRecentAttacker(uid) || boss.RecentAttacker is not { } attacker ||
            boss.RecentAttackPosition is not { } coordinates || enemies.Count >= 24 ||
            enemies.Any(enemy => enemy.Uid == attacker))
            return;

        var origin = _transform.GetMapCoordinates(uid);
        var visible = _interaction.InRangeUnobstructed(uid, attacker, range: 0, collisionMask: CollisionGroup.Opaque);
        var position = _transform.ToMapCoordinates(coordinates);
        var velocity = Vector2.Zero;
        if (visible)
        {
            position = _transform.GetMapCoordinates(attacker);
            coordinates = _transform.ToCoordinates(coordinates.EntityId, position);
            var frameVelocity = _physics.GetMapLinearVelocity(_transform.ToCoordinates(coordinates.EntityId, origin));
            velocity = _physics.GetMapLinearVelocity(attacker) - frameVelocity;
            if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y))
                velocity = Vector2.Zero;
        }

        // Hidden attackers use only the position recorded when they fired, never their live movement.
        var distance = Vector2.Distance(origin.Position, position.Position);
        if (distance > boss.CounterplayRange)
            return;
        var ranged = HasComp<GunComponent>(attacker) ||
                     (_hands.TryGetActiveItem(attacker, out var held) && HasComp<GunComponent>(held));
        enemies.Add(new Enemy(attacker, coordinates, position.Position, velocity, distance, ranged, visible));
    }
}
