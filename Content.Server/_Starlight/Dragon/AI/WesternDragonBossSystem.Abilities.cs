using System.Linq;
using System.Numerics;
using Content.Shared._Starlight.Dragon;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._Starlight.Dragon;

public sealed partial class WesternDragonBossSystem
{
    private readonly record struct AbilityChoice(DragonAbility Ability, EntityUid Action, EntityCoordinates Aim, float Score);

    private bool TryAbility(EntityUid uid, WesternDragonBossComponent boss, List<Enemy> enemies, Enemy target, float health)
    {
        var now = _timing.CurTime;
        var followUp = boss.ComboTarget != null;
        if (followUp && (boss.ComboTarget != target.Uid || now >= boss.ComboUntil))
        {
            EndCombo(boss);
            return false;
        }
        var origin = _transform.GetMapCoordinates(uid).Position;
        var choices = new List<AbilityChoice>(5);
        foreach (var action in _actions.GetActions(uid))
        {
            if (!_actions.ValidAction(action))
                continue;
            var ability = MetaData(action).EntityPrototype?.ID switch
            {
                "ActionWesternTailSlam" => DragonAbility.TailSlam,
                "ActionWesternFireBreath" => DragonAbility.Breath,
                "ActionWesternDragonsBreath" => DragonAbility.Fireball,
                "ActionDragonRoar" => DragonAbility.Roar,
                "ActionWingDash" => DragonAbility.Dash,
                _ => DragonAbility.None,
            };
            if ((followUp && ability != DragonAbility.Dash) ||
                (ability == DragonAbility.TailSlam && boss.LowestHealth > boss.TailSlamHealth) ||
                (ability == DragonAbility.Breath && boss.LowestHealth > boss.BreathHealth) ||
                (ability == DragonAbility.Fireball && boss.LowestHealth > boss.FireballHealth))
                continue;
            var aim = target.Coordinates;
            var score = 0f;
            switch (ability)
            {
                case DragonAbility.TailSlam when boss.CloseEnemies >= 1:
                    score = 12 + (boss.CloseEnemies * 3);
                    aim = Transform(uid).Coordinates;
                    break;
                case DragonAbility.Breath:
                    foreach (var candidate in enemies)
                    {
                        if (candidate.Distance is > 5 or < 0.5f)
                            continue;
                        var forward = Vector2.Normalize(candidate.Position - origin);
                        var hits = enemies.Count(e => e.Distance is <= 5 and >= 0.5f &&
                            Vector2.Dot(Vector2.Normalize(e.Position - origin), forward) > 0.85f);
                        var value = 6 + (hits * 4);
                        if (value <= score)
                            continue;
                        score = value;
                        aim = candidate.Coordinates;
                    }
                    break;
                case DragonAbility.Fireball:
                    foreach (var candidate in enemies)
                    {
                        if (candidate.Distance is < 2.5f or > 12)
                            continue;
                        var grouped = enemies.Count(e => Vector2.DistanceSquared(e.Position, candidate.Position) <= 6.25f);
                        var value = 4 + (grouped * 3) + (candidate.Ranged ? 3 : 0) + (candidate.Distance > 5 ? 3 : 0);
                        if (value <= score)
                            continue;
                        score = value;
                        aim = candidate.Coordinates;
                    }
                    break;
                case DragonAbility.Roar when enemies.Any(e => e.Distance <= 7):
                    score = 10 + (enemies.Count(e => e.Distance <= 7) * 2) + (boss.RecentDamage > 60 ? 4 : 0);
                    break;
                case DragonAbility.Dash:
                    var retreat = !followUp && health < 0.45f && boss.CloseEnemies >= 2;
                    if ((retreat || target.Distance > (followUp ? 2 : 4)) && _actions.GetEvent(action) is WingDashEvent dash &&
                        dash.DashDistance > 0 && dash.DashSpeed > 0 &&
                        (!followUp || target.Distance <= dash.DashDistance + 1) &&
                        TryPosition(uid, target, retreat ? DragonPositioning.Retreat : DragonPositioning.Approach,
                            boss.CircleDirection, Math.Max(0.25f, dash.DashDistance - 0.25f), out var destination))
                    {
                        aim = destination;
                        score = retreat ? 22 : target.Ranged || boss.Threat.GetValueOrDefault(target.Uid) > 30 ? 13 : 6;
                    }
                    break;
            }

            if (score <= 0)
                continue;
            // Situation dominates; small jitter breaks ties. Recent attacks are discouraged, never forced into a rotation.
            score *= ability == boss.LastAbility ? 0.25f : ability == boss.PreviousAbility ? 0.65f : 1;
            choices.Add(new AbilityChoice(ability, action, aim, score * _random.NextFloat(0.9f, 1.1f)));
        }

        foreach (var choice in choices.OrderByDescending(c => c.Score))
        {
            if (!_actions.TryPerformDragonAction(uid, choice.Action, choice.Aim))
                continue;
            boss.PreviousAbility = boss.LastAbility;
            boss.LastAbility = choice.Ability;
            boss.AbilitiesUsed++;
            EndCombo(boss);
            if (choice.Ability == DragonAbility.Roar)
            {
                boss.ComboTarget = target.Uid;
                boss.ComboUntil = now + TimeSpan.FromSeconds(4);
                boss.NextAbility = now + TimeSpan.FromSeconds(1);
            }
            boss.NextPosition = now;
            boss.RecoverUntil = now + TimeSpan.FromSeconds(choice.Ability == DragonAbility.TailSlam ? 0.6 : 0.3);
            // Finish the dash with sustained melee pursuit, not an immediate retreat or another spell.
            if (choice.Ability == DragonAbility.Dash)
            {
                boss.NextReposition = boss.NextAbility;
                _steering.Unregister(uid);
            }
            if (_random.Prob(0.35f))
                TryTaunt(uid, boss, choice.Ability == DragonAbility.TailSlam
                    ? "western-dragon-taunt-slam" : "western-dragon-taunt-flames");
            return true;
        }
        // A blocked/cooling-down dash must fall back to melee without waiting or dumping other abilities.
        if (followUp)
            EndCombo(boss);
        return false;
    }

    private void EndCombo(WesternDragonBossComponent boss)
    {
        boss.ComboTarget = null;
        boss.NextAbility = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(1, boss.AbilityInterval) + _random.NextFloat(0, 1));
    }
}
