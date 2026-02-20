using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Log;

namespace Content.Server.NPC.HTN.Preconditions;

/// <summary>
/// Checks if the owner's health percentage is below or above a threshold.
/// Health percentage is calculated as (CriticalThreshold - CurrentDamage) / CriticalThreshold
/// </summary>
public sealed partial class HealthPercentPrecondition : HTNPrecondition
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    private MobThresholdSystem _mobThreshold = default!;

    /// <summary>
    /// Health percentage threshold (0.0 to 1.0)
    /// </summary>
    [DataField(required: true)]
    public float Threshold = 0.5f;

    /// <summary>
    /// If true, precondition is met when health is BELOW threshold (injured).
    /// If false, precondition is met when health is ABOVE threshold (healthy).
    /// </summary>
    [DataField]
    public bool IsBelow = true;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _mobThreshold = sysManager.GetEntitySystem<MobThresholdSystem>();
    }

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entManager))
        {
            Logger.Error("HealthPercentPrecondition: Could not get owner from blackboard");
            return false;
        }

        // Get damage and threshold components
        if (!_entManager.TryGetComponent<DamageableComponent>(owner, out var damageable) ||
            !_entManager.TryGetComponent<MobThresholdsComponent>(owner, out var thresholds))
        {
            Logger.Debug($"HealthPercentPrecondition: Entity {_entManager.ToPrettyString(owner)} missing required components (Damageable: {_entManager.HasComponent<DamageableComponent>(owner)}, MobThresholds: {_entManager.HasComponent<MobThresholdsComponent>(owner)})");
            return false;
        }

        // Get the critical threshold (the damage value at which mob goes critical)
        if (!_mobThreshold.TryGetThresholdForState(owner, MobState.Critical, out var criticalThreshold, thresholds))
        {
            // If no critical threshold, use Dead threshold as fallback
            if (!_mobThreshold.TryGetThresholdForState(owner, MobState.Dead, out criticalThreshold, thresholds))
                return false;
        }

        // Calculate current health percentage
        // Health % = (MaxHealth - CurrentDamage) / MaxHealth
        // Since damage increases and threshold is the "max health", we use:
        // Health % = (Threshold - CurrentDamage) / Threshold
        var maxHealth = criticalThreshold.Value;
        var currentDamage = damageable.TotalDamage;
        
        if (maxHealth <= FixedPoint2.Zero)
            return false;

        var healthPercent = (float)((maxHealth - currentDamage) / maxHealth);
        
        // Store health percent in blackboard for use by operators
        blackboard.SetValue(NPCBlackboard.OwnerHealthPercent, healthPercent);

        // Check if precondition is met
        var isMet = IsBelow ? healthPercent < Threshold : healthPercent > Threshold;
        
        Logger.Debug($"HealthPercentPrecondition: Entity {_entManager.ToPrettyString(owner)} health={healthPercent:P1} (damage={currentDamage}/{maxHealth}), threshold={Threshold:P0}, isBelow={IsBelow}, met={isMet}");
        
        return isMet;
    }
}
