using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Content.Shared.Damage.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using JetBrains.Annotations;
using Robust.Shared.Utility;

namespace Content.Server._Starlight.NPC.HTN.Preconditions;

/// <summary>
/// Returns true when the NPC's total damage exceeds a given percentage of its critical threshold.
/// Use MinDamagePercent = 0.5 to mean "more than half of the damage needed to go crit".
/// </summary>
[UsedImplicitly]
public sealed partial class LowHealthPrecondition : HTNPrecondition
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    private MobThresholdSystem _thresholds = default!;

    /// <summary>
    /// Activate when damage / crit-threshold >= this value.
    /// 0.5 = react at half health lost (relative to crit point).
    /// </summary>
    [DataField]
    public float MinDamagePercent = 0.5f;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _thresholds = sysManager.GetEntitySystem<MobThresholdSystem>();
    }

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entManager))
            return false;

        if (!_entManager.TryGetComponent<DamageableComponent>(owner, out var damageable))
            return false;

        // Use the crit threshold so the NPC reacts before it goes incapacitated.
        if (!_thresholds.TryGetThresholdForState(owner, MobState.Critical, out var critThreshold) || critThreshold.Value == 0)
            return false;

        var damagePercent = (float) damageable.TotalDamage / (float) critThreshold.Value;
        return damagePercent >= MinDamagePercent;
    }
}
