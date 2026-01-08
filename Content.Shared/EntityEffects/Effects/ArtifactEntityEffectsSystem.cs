using Content.Shared.Popups;
using Content.Shared.Xenoarchaeology.Artifact;
using Content.Shared.Xenoarchaeology.Artifact.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared.EntityEffects.Effects;

/// <summary>
/// Restores durability on this artifact
/// </summary>
/// <inheritdoc cref="EntityEffectSystem{T,TEffect}"/>
public sealed partial class ArtifactDurabilityRestoreEntityEffectsSystem : EntityEffectSystem<XenoArtifactComponent, ArtifactDurabilityRestore>
{
    [Dependency] private readonly SharedXenoArtifactSystem _xenoArtifact = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    protected override void Effect(Entity<XenoArtifactComponent> entity, ref EntityEffectEvent<ArtifactDurabilityRestore> args)
    {
        // Check if the artifact has anomaly spawning capability - if so, it cannot be repaired at all
        if (entity.Comp.HasAnomalySpawner)
        {
            // Check if this anomaly artifact has already been repaired once
            if (entity.Comp.HasBeenRepaired)
            {
                _popup.PopupEntity(Loc.GetString("artifact-repair-too-broken"), entity, PopupType.Medium);
                return;
            }

            var durability = args.Effect.RestoredDurability;
            var anyRepaired = false;

            foreach (var node in _xenoArtifact.GetActiveNodes(entity))
            {
                _xenoArtifact.AdjustNodeDurability(node.Owner, durability);
                anyRepaired = true;
            }

            if (anyRepaired)
            {
                // Mark anomaly artifact as repaired - can only be repaired once
                entity.Comp.HasBeenRepaired = true;
                _popup.PopupEntity(Loc.GetString("artifact-repair-success"), entity, PopupType.Medium);
            }
            return;
        }

        // Normal artifacts can be repaired without limit
        var normalDurability = args.Effect.RestoredDurability;

        foreach (var node in _xenoArtifact.GetActiveNodes(entity))
        {
            _xenoArtifact.AdjustNodeDurability(node.Owner, normalDurability);
        }

        _popup.PopupEntity(Loc.GetString("artifact-repair-success"), entity, PopupType.Medium);
    }
}

/// <summary>
/// Unlocks a node on this artifact. Only works this effect hasn't been applied before.
/// </summary>
/// <inheritdoc cref="EntityEffectSystem{T,TEffect}"/>
public sealed partial class ArtifactUnlockEntityEffectSystem : EntityEffectSystem<XenoArtifactComponent, ArtifactUnlock>
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedXenoArtifactSystem _xenoArtifact = default!;

    protected override void Effect(Entity<XenoArtifactComponent> entity, ref EntityEffectEvent<ArtifactUnlock> args)
    {
        if (EnsureComp<XenoArtifactUnlockingComponent>(entity, out var unlocking))
        {
            if (unlocking.ArtifexiumApplied)
                return;

            _popup.PopupEntity(Loc.GetString("artifact-activation-artifexium"), entity, PopupType.Medium);
        }
        else
        {
            _xenoArtifact.TriggerXenoArtifact(entity, null, force: true);
        }

        _xenoArtifact.SetArtifexiumApplied((entity, unlocking), true);
    }
}

/// <inheritdoc cref="EntityEffect"/>
public sealed partial class ArtifactDurabilityRestore : EntityEffectBase<ArtifactDurabilityRestore>
{
    /// <summary>
    ///     Amount of durability that will be restored per effect interaction.
    /// </summary>
    [DataField]
    public int RestoredDurability = 1;

    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) =>
        Loc.GetString("entity-effect-guidebook-artifact-durability-restore", ("restored", RestoredDurability));
}

/// <inheritdoc cref="EntityEffect"/>
public sealed partial class ArtifactUnlock : EntityEffectBase<ArtifactUnlock>
{
    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) =>
        Loc.GetString("entity-effect-guidebook-artifact-unlock", ("chance", Probability));
}
