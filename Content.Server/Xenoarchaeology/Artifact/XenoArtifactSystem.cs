using Content.Server.Spawners.Components;
using Content.Shared.Cargo;
using Content.Shared.Xenoarchaeology.Artifact;
using Content.Shared.Xenoarchaeology.Artifact.Components;

namespace Content.Server.Xenoarchaeology.Artifact;

/// <inheritdoc cref="SharedXenoArtifactSystem"/>
public sealed partial class XenoArtifactSystem : SharedXenoArtifactSystem
{
    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<XenoArtifactComponent, MapInitEvent>(OnArtifactMapInit);
        SubscribeLocalEvent<XenoArtifactComponent, PriceCalculationEvent>(OnCalculatePrice);
    }

    private void OnArtifactMapInit(Entity<XenoArtifactComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.IsGenerationRequired)
        {
            GenerateArtifactStructure(ent);
            CheckForAnomalySpawners(ent);
        }
    }

    /// <summary>
    /// Checks if the artifact has any nodes that spawn anomalies and marks it as unrepairable if so.
    /// </summary>
    private void CheckForAnomalySpawners(Entity<XenoArtifactComponent> ent)
    {
        foreach (var node in GetAllNodes(ent))
        {
            // Check if this node has an EntityTableSpawner that spawns anomalies
            if (TryComp<EntityTableSpawnerComponent>(node.Owner, out var spawner))
            {
                // Mark the entire artifact as having an anomaly spawner
                ent.Comp.HasAnomalySpawner = true;
                Dirty(ent);
                return;
            }
        }
    }

    private void OnCalculatePrice(Entity<XenoArtifactComponent> ent, ref PriceCalculationEvent args)
    {
        foreach (var node in GetAllNodes(ent))
        {
            if (node.Comp.Locked)
                continue;

            args.Price += node.Comp.ResearchValue * ent.Comp.PriceMultiplier;
        }
    }
}
