using System.Numerics;
using Content.Shared.ActionBlocker;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;

namespace Content.Server._Starlight.Dragon;

public sealed partial class WesternDragonBossSystem
{
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private bool IsCountering(WesternDragonBossComponent boss, EntityUid target)
        => boss.CounterplayTarget == target && _timing.CurTime < boss.CounterplayUntil;

    private void UpdateCounterplay(EntityUid uid, WesternDragonBossComponent boss, Enemy target)
    {
        var now = _timing.CurTime;
        var canClose = target.Distance <= 3 && _interaction.InRangeUnobstructed(uid, target.Coordinates, range: 0,
            collisionMask: CollisionGroup.Impassable | CollisionGroup.InteractImpassable);
        if (boss.RecentAttacker == target.Uid && now < boss.RecentAttackUntil)
            boss.PursuitAttackedUntil = boss.RecentAttackUntil;
        if (boss.CounterplayTarget != null)
        {
            if (IsCountering(boss, target.Uid) && !canClose && _blocker.CanMove(uid))
                return;
            ClearCounterplay(boss);
        }

        // A path failure alone is not evidence of kiting. Require repeated attacks from the
        // pursued target, with no meaningful gain in distance for an entire observation window.
        if (now < boss.NextCounterplay || now >= boss.PursuitAttackedUntil || canClose ||
            now < boss.RecoverUntil || boss.ComboTarget != null || !_blocker.CanMove(uid) ||
            boss.Positioning is DragonPositioning.Retreat or DragonPositioning.Circle or DragonPositioning.Feed)
        {
            boss.PursuitTarget = null;
            return;
        }

        if (boss.PursuitTarget != target.Uid || target.Distance <= boss.PursuitDistance - 1)
        {
            boss.PursuitTarget = target.Uid;
            boss.PursuitDistance = target.Distance;
            boss.PursuitSampleAt = now;
            return;
        }
        if (now - boss.PursuitSampleAt < TimeSpan.FromSeconds(Math.Max(1, boss.CounterplayDelay)))
            return;

        // Don't warn or change stance for an attack that is unavailable. Existing action and
        // gun cooldowns still apply; this only temporarily bypasses the fireball's health gate.
        if (now < boss.NextAbility || target.Distance > boss.CounterplayRange || !TryComp<ActionGunComponent>(uid, out var gun) ||
            _actions.GetAction(gun.ActionEntity) is not { } action || !_actions.ValidAction(action) ||
            !TryComp<GunComponent>(gun.Gun, out var weapon) || now < weapon.NextFire)
            return;

        boss.CounterplayTarget = target.Uid;
        boss.CounterplayReadyAt = now + TimeSpan.FromSeconds(1.2);
        boss.CounterplayUntil = boss.CounterplayReadyAt + TimeSpan.FromSeconds(Math.Max(1, boss.CounterplayDuration));
        boss.NextCounterplay = boss.CounterplayUntil + TimeSpan.FromSeconds(Math.Max(1, boss.CounterplayCooldown));
        boss.NextPosition = now;
        _popup.PopupEntity(Loc.GetString("western-dragon-counterplay-warning", ("dragon", uid)), uid, PopupType.LargeCaution);
    }

    private static void ClearCounterplay(WesternDragonBossComponent boss)
    {
        boss.PursuitTarget = null;
        boss.CounterplayTarget = null;
        boss.PursuitAttackedUntil = TimeSpan.Zero;
        boss.CounterplayUntil = TimeSpan.Zero;
        boss.CounterplayReadyAt = TimeSpan.Zero;
        // Keep NextCounterplay through HTN replanning so breaking LOS cannot bypass its cooldown.
    }

    private EntityCoordinates PredictCounterShot(EntityUid uid, Enemy target)
    {
        if (!TryComp<ActionGunComponent>(uid, out var actionGun) ||
            !TryComp<GunComponent>(actionGun.Gun, out var gun))
            return target.Coordinates;

        // Lead using the existing gun's projectile speed, capped for abrupt turns and speed boosts.
        // Velocity is already relative to the dragon's grid, so a moving shuttle adds no false lead.
        var lead = target.Velocity * Math.Clamp(target.Distance / Math.Max(1, gun.ProjectileSpeedModified), 0, 0.75f);
        if (lead.LengthSquared() > 9)
            lead = Vector2.Normalize(lead) * 3;
        var map = _transform.ToMapCoordinates(target.Coordinates);
        var aim = _transform.ToCoordinates(target.Coordinates.EntityId, new MapCoordinates(target.Position + lead, map.MapId));
        return _interaction.InRangeUnobstructed(uid, aim, range: 0,
            collisionMask: CollisionGroup.Impassable | CollisionGroup.InteractImpassable) ? aim : target.Coordinates;
    }
}
