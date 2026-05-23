using Content.Server.Body.Components;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Zombies;
using Content.Shared._Starlight.Medical.Body.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Zombies;

/// <summary>
/// Rotting zombie corpses exhale a miasma gas (ZombieMiasma reagent) that builds up
/// in the bloodstreams of nearby living entities. Wearing a face mask greatly reduces
/// exposure; full internals (mask + gas tank) nearly eliminates it.
/// Many corpses in a confined space are required to reach infectious levels.
/// </summary>
public sealed class ZombieCorpseMiasmaSystem : EntitySystem
{
    [Dependency] private readonly SharedRottingSystem _rotting = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedBloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    // How far the miasma spreads from each corpse (in tiles)
    private const float MiasmaRange = 6f;

    // How often we update (seconds)
    private const float UpdateRate = 5f;

    // Base dose per corpse per update tick, multiplied by (RotStage + 1)
    // Stage 0 (bloated) = 0.25u, Stage 1 (extremely bloated) = 0.5u, Stage 2+ = 0.75u
    private const float BaseDosePerCorpse = 0.25f;

    private static readonly ProtoId<ReagentPrototype> MiasmaReagent = "ZombieMiasma";

    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateRate)
            return;

        _accumulator -= UpdateRate;

        // Gather all rotting zombie corpses
        var rottingQuery = EntityQueryEnumerator<RottingComponent, ZombifyOnDeathComponent>();

        // We collect corpse positions and their dose amounts first
        // to avoid double-counting adjacent corpses in a single pass.
        var corpseData = new List<(MapCoordinates Coords, float Dose)>();

        while (rottingQuery.MoveNext(out var uid, out var rotting, out _))
        {
            // Only dead corpses
            if (TryComp<MobStateComponent>(uid, out var mobState) && !_mobState.IsDead(uid, mobState))
                continue;

            var stage = _rotting.RotStage(uid, rotting);
            var dose = BaseDosePerCorpse * (stage + 1);
            var coords = Transform(uid).MapPosition;
            corpseData.Add((coords, dose));
        }

        // Also scan active zombie corpses (ZombieComponent) when dead and rotting
        var zombieRottingQuery = EntityQueryEnumerator<RottingComponent, ZombieComponent>();
        while (zombieRottingQuery.MoveNext(out var uid, out var rotting, out _))
        {
            if (TryComp<MobStateComponent>(uid, out var mobState) && !_mobState.IsDead(uid, mobState))
                continue;

            var stage = _rotting.RotStage(uid, rotting);
            var dose = BaseDosePerCorpse * (stage + 1);
            var coords = Transform(uid).MapPosition;
            corpseData.Add((coords, dose));
        }

        if (corpseData.Count == 0)
            return;

        // For each living entity with a bloodstream, accumulate miasma from nearby corpses
        var bloodstreamQuery = EntityQueryEnumerator<BloodstreamComponent, MobStateComponent>();
        while (bloodstreamQuery.MoveNext(out var target, out var bloodstream, out var mobState))
        {
            // Skip dead entities
            if (_mobState.IsDead(target, mobState))
                continue;

            // Skip zombies and immune entities
            if (HasComp<ZombieComponent>(target) || HasComp<ZombieImmuneComponent>(target))
                continue;

            var targetCoords = Transform(target).MapPosition;

            // Sum up miasma dose from all nearby corpses
            float totalDose = 0f;
            foreach (var (corpseCoords, corpseDose) in corpseData)
            {
                if (corpseCoords.MapId != targetCoords.MapId)
                    continue;

                var dist = (corpseCoords.Position - targetCoords.Position).Length();
                if (dist <= MiasmaRange)
                    totalDose += corpseDose;
            }

            if (totalDose <= 0f)
                continue;

            // Apply protection multiplier based on worn equipment
            var protectionMultiplier = 1.0f;

            if (TryComp<InternalsComponent>(target, out var internals))
            {
                if (internals.GasTankEntity != null)
                {
                    // Full internals (mask + gas tank) — 90% reduction
                    protectionMultiplier *= 0.1f;
                }
                else if (internals.BreathTools.Count > 0)
                {
                    // Mask connected but no gas tank — 60% reduction
                    protectionMultiplier *= 0.4f;
                }
            }

            // Gloves provide additional 30% reduction (necrotic residue on hands, etc.)
            if (_inventory.TryGetSlotEntity(target, "gloves", out _))
                protectionMultiplier *= 0.7f;

            var adjustedDose = totalDose * protectionMultiplier;
            if (adjustedDose < 0.01f)
                continue;

            // Inject the miasma into the bloodstream
            var miasmaAmount = FixedPoint2.New(adjustedDose);
            var solution = new Solution();
            solution.AddReagent(MiasmaReagent, miasmaAmount);
            _bloodstream.TryAddToBloodstream((target, bloodstream), solution);
        }
    }
}
