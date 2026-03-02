using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Content.Shared.Damage.Components;
using Content.Shared.Mobs.Systems;
using JetBrains.Annotations;
using Robust.Shared.Utility;

namespace Content.Server._Starlight.NPC.HTN.Preconditions;

/// <summary>
/// Returns true when the NPC's total damage exceeds a given percentage of its dead threshold (i.e. it is badly hurt).
/// Use MinDamagePercent = 0.5 to mean "more than half of max-HP taken".
/// </summary>
[UsedImplicitly]
public sealed partial class LowHealthPrecondition : HTNPrecondition
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    private MobThresholdSystem _thresholds = default!;

    /// <summary>
    /// Activate when damage / death-threshold >= this value.
    /// 0.5 = react at half health lost.
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

        if (!_thresholds.TryGetDeadThreshold(owner, out var deadThreshold) || deadThreshold.Value == 0)
            return false;

        var damagePercent = (float) damageable.TotalDamage / (float) deadThreshold.Value;
        return damagePercent >= MinDamagePercent;
    }
}
