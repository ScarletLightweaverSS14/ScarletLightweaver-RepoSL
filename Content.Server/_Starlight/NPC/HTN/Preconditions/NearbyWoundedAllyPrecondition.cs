using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Content.Shared.Damage.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using JetBrains.Annotations;

namespace Content.Server._Starlight.NPC.HTN.Preconditions;

/// <summary>
/// Returns true when at least one faction ally within <see cref="Range"/> has taken
/// damage exceeding <see cref="MinDamagePercent"/> of their critical threshold.
/// Used to gate the hybrid Red Vanguard's ally-healing branch.
/// </summary>
[UsedImplicitly]
public sealed partial class NearbyWoundedAllyPrecondition : HTNPrecondition
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    private NpcFactionSystem _faction = default!;
    private MobThresholdSystem _thresholds = default!;

    /// <summary>Search radius in tiles.</summary>
    [DataField]
    public float Range = 8.0f;

    /// <summary>
    /// Ally must have damage / crit-threshold >= this to be considered wounded.
    /// 0.35 = react when ally has lost 35% of health toward crit.
    /// </summary>
    [DataField]
    public float MinDamagePercent = 0.35f;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _faction    = sysManager.GetEntitySystem<NpcFactionSystem>();
        _thresholds = sysManager.GetEntitySystem<MobThresholdSystem>();
    }

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entManager))
            return false;

        foreach (var ally in _faction.GetNearbyFriendlies(owner, Range))
        {
            if (!_entManager.TryGetComponent<DamageableComponent>(ally, out var damageable))
                continue;
            if (!_thresholds.TryGetThresholdForState(ally, MobState.Critical, out var crit) || crit.Value == 0)
                continue;

            var pct = (float) damageable.TotalDamage / (float) crit.Value;
            if (pct >= MinDamagePercent)
                return true;
        }

        return false;
    }
}
