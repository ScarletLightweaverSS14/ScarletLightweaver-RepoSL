using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Localizations;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Robust.Shared.Prototypes;

namespace Content.Shared.EntityEffects.Effects;

/// <summary>
/// For entities with SlimeAntitoxinComponent, converts Poison/Radiation damage into healing based on damage tiers.
/// Requires at least 10 units of total toxin reagents in bloodstream to activate.
/// When multiple toxins are present, only the toxin with the highest quantity will provide healing.
/// Low toxin damage (0-5): heals brute
/// Medium toxin damage (5-10): heals brute and burn
/// High toxin damage (10+): heals all damage types like omnizine
/// For entities without the component, applies normal toxin damage.
/// </summary>
public sealed partial class SlimeToxinHealingEntityEffectSystem : EntityEffectSystem<DamageableComponent, SlimeToxinHealing>
{
    [Dependency] private readonly Damage.Systems.DamageableSystem _damageable = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;

    protected override void Effect(Entity<DamageableComponent> entity, ref EntityEffectEvent<SlimeToxinHealing> args)
    {
        // If the entity doesn't have SlimeAntitoxinComponent, apply damage normally
        if (!HasComp<SlimeAntitoxinComponent>(entity))
        {
            _damageable.TryChangeDamage(
                entity.AsNullable(),
                args.Effect.ToxinDamage * args.Scale,
                args.Effect.IgnoreResistances,
                interruptsDoAfters: false);
            return;
        }

        // Entity is a slime - check bloodstream for toxin quantities
        // Get the bloodstream solution
        if (!_solutionContainer.TryGetSolution(entity.Owner, "Bloodstream", out var bloodstreamEnt, out var bloodstream))
            return;

        // Calculate this reagent's toxin damage amount per unit
        var currentToxinDamage = FixedPoint2.Zero;
        foreach (var (damageType, amount) in args.Effect.ToxinDamage.DamageDict)
        {
            if (damageType == "Poison" || damageType == "Radiation")
            {
                currentToxinDamage += amount;
            }
        }

        // Find all toxin reagents in the bloodstream and track quantities
        var totalToxinUnits = FixedPoint2.Zero;
        var currentReagentQuantity = FixedPoint2.Zero;
        var isCurrentReagentHighest = true;
        
        foreach (var reagentQuantity in bloodstream.Contents)
        {
            var reagentId = reagentQuantity.Reagent.Prototype;
            
            // Check if this reagent has SlimeToxinHealing metabolism effects
            if (!_proto.TryIndex<ReagentPrototype>(reagentId, out var reagentProto))
                continue;

            // Check Poison metabolisms for SlimeToxinHealing
            if (reagentProto.Metabolisms != null && 
                reagentProto.Metabolisms.TryGetValue("Poison", out var poisonMetabolism))
            {
                foreach (var effect in poisonMetabolism.Effects)
                {
                    if (effect is SlimeToxinHealing slimeToxinEffect)
                    {
                        // This is a toxin reagent, count it
                        var quantity = reagentQuantity.Quantity;
                        totalToxinUnits += quantity;
                        
                        // Check if this is the current reagent being metabolized
                        if (slimeToxinEffect.ToxinDamage == args.Effect.ToxinDamage)
                        {
                            currentReagentQuantity = quantity;
                        }
                        // If another toxin has MORE quantity, current one isn't highest
                        else if (quantity > currentReagentQuantity)
                        {
                            isCurrentReagentHighest = false;
                        }
                        break;
                    }
                }
            }
        }

        // Check if total toxin reagent quantity meets the 10 unit minimum
        if (totalToxinUnits < 10)
            return; // Not enough toxin to activate healing

        // Only apply healing if this is the highest-quantity toxin
        if (!isCurrentReagentHighest)
            return;

        // Use the toxin damage from this reagent to determine healing tier
        // High-end toxins (3+ damage per unit): Weaker omnizine (all damage types)
        // Toxins dealing 2 damage: Heal brute, burn, and caustic
        // Low-end toxins (under 2 damage): Heal brute only
        var toxinDamagePerUnit = currentToxinDamage;
        var isHighEndToxin = toxinDamagePerUnit >= 3;
        var isMediumToxin = toxinDamagePerUnit >= 1.9f && toxinDamagePerUnit <= 2.1f; // 2 damage toxins (with tolerance)
        
        var toxinAmount = currentToxinDamage;
        toxinAmount *= args.Scale;

        var healSpec = new DamageSpecifier();

        // High-end toxins (3+ damage/unit): Weaker omnizine-like healing (all damage types)
        if (isHighEndToxin)
        {
            var totalDamage = entity.Comp.TotalDamage;
            if (totalDamage > 0)
            {
                var healAmount = -toxinAmount * 1.5f; // High-end toxins heal for 1.5x their damage value (weaker omnizine)
                
                // Heal all damage groups proportionally
                foreach (var (damageType, damage) in entity.Comp.Damage.DamageDict)
                {
                    if (damage > 0)
                    {
                        var proportion = damage / totalDamage;
                        healSpec.DamageDict[damageType] = healAmount * proportion;
                    }
                }
            }
        }
        // Medium toxins (2 damage): heal brute, burn, and caustic
        else if (isMediumToxin)
        {
            var healAmount = -toxinAmount * 1.25f; // Medium toxins heal for 1.25x their damage value
            
            // Heal brute damage
            if (_proto.TryIndex<DamageGroupPrototype>("Brute", out var bruteGroup))
            {
                var bruteTypes = 0;
                foreach (var damageType in bruteGroup.DamageTypes)
                {
                    if (entity.Comp.Damage.DamageDict.TryGetValue(damageType, out var damage) && damage > 0)
                        bruteTypes++;
                }
                
                if (bruteTypes > 0)
                {
                    foreach (var damageType in bruteGroup.DamageTypes)
                    {
                        if (entity.Comp.Damage.DamageDict.TryGetValue(damageType, out var damage) && damage > 0)
                        {
                            healSpec.DamageDict[damageType] = healAmount / 3 / bruteTypes; // Split between brute, burn, caustic
                        }
                    }
                }
            }
            
            // Heal burn damage
            if (_proto.TryIndex<DamageGroupPrototype>("Burn", out var burnGroup))
            {
                var burnTypes = 0;
                foreach (var damageType in burnGroup.DamageTypes)
                {
                    if (entity.Comp.Damage.DamageDict.TryGetValue(damageType, out var damage) && damage > 0)
                        burnTypes++;
                }
                
                if (burnTypes > 0)
                {
                    foreach (var damageType in burnGroup.DamageTypes)
                    {
                        if (entity.Comp.Damage.DamageDict.TryGetValue(damageType, out var damage) && damage > 0)
                        {
                            healSpec.DamageDict[damageType] = healAmount / 3 / burnTypes; // Split between brute, burn, caustic
                        }
                    }
                }
            }
            
            // Heal caustic damage
            if (entity.Comp.Damage.DamageDict.TryGetValue("Caustic", out var causticDamage) && causticDamage > 0)
            {
                healSpec.DamageDict["Caustic"] = healAmount / 3; // Split between brute, burn, caustic
            }
        }
        // Low-end toxins (under 2 damage): heal brute only
        else if (toxinAmount > 0)
        {
            var healAmount = -toxinAmount * 1.0f; // Low toxins heal for 1x their damage value
            
            if (_proto.TryIndex<DamageGroupPrototype>("Brute", out var bruteGroup))
            {
                var bruteTypes = 0;
                foreach (var damageType in bruteGroup.DamageTypes)
                {
                    if (entity.Comp.Damage.DamageDict.TryGetValue(damageType, out var damage) && damage > 0)
                        bruteTypes++;
                }
                
                if (bruteTypes > 0)
                {
                    foreach (var damageType in bruteGroup.DamageTypes)
                    {
                        if (entity.Comp.Damage.DamageDict.TryGetValue(damageType, out var damage) && damage > 0)
                        {
                            healSpec.DamageDict[damageType] = healAmount / bruteTypes;
                        }
                    }
                }
            }
        }

        // Apply the healing
        _damageable.TryChangeDamage(
            entity.AsNullable(),
            healSpec,
            args.Effect.IgnoreResistances,
            interruptsDoAfters: false);
    }
}

/// <inheritdoc cref="EntityEffect"/>
public sealed partial class SlimeToxinHealing : EntityEffectBase<SlimeToxinHealing>
{
    /// <summary>
    /// Toxin damage that would be dealt to non-slimes.
    /// For slimes, this is converted to healing based on amount.
    /// </summary>
    [DataField(required: true)]
    public DamageSpecifier ToxinDamage = default!;

    [DataField]
    public bool IgnoreResistances = true;

    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        var damages = new List<string>();
        var toxinAmount = FixedPoint2.Zero;

        var damageableSystem = entSys.GetEntitySystem<Damage.Systems.DamageableSystem>();
        var universalReagentDamageModifier = damageableSystem.UniversalReagentDamageModifier;

        foreach (var (damageType, amount) in ToxinDamage.DamageDict)
        {
            if (damageType == "Poison" || damageType == "Radiation")
            {
                toxinAmount += amount;
                var adjustedAmount = amount * universalReagentDamageModifier;
                var typeProto = prototype.Index<DamageTypePrototype>(damageType);
                var typeStr = $"{typeProto.LocalizedName}: {adjustedAmount}";
                damages.Add(typeStr);
            }
        }

        var healingTier = "none";
        if (toxinAmount >= 10)
            healingTier = "omnizine-like (all damage types)";
        else if (toxinAmount >= 5)
            healingTier = "brute and burn";
        else if (toxinAmount > 0)
            healingTier = "brute";

        var damageStr = string.Join(", ", damages);
        return Loc.GetString("entity-effect-slime-toxin-healing",
            ("chance", Probability),
            ("damage", damageStr),
            ("healing", healingTier));
    }
}
