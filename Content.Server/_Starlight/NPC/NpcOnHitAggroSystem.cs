using System.Numerics;
using Content.Server.NPC.HTN;
using Content.Shared._Starlight.NPC;
using Content.Shared.Damage.Systems;
using Robust.Shared.Map;

namespace Content.Server._Starlight.NPC;

/// <summary>
/// Handles <see cref="NpcOnHitAggroComponent"/>: when an NPC takes damage from an identifiable
/// attacker, instantly writes that attacker into the HTN blackboard as the current combat target
/// and forces a replan. This prevents players from cheesing NPCs by attacking from outside
/// their normal awareness range.
/// </summary>
public sealed partial class NpcOnHitAggroSystem : EntitySystem
{
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly SharedTransformSystem _xforms = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NpcOnHitAggroComponent, DamageChangedEvent>(OnDamageChanged);
    }

    private void OnDamageChanged(Entity<NpcOnHitAggroComponent> ent, ref DamageChangedEvent args)
    {
        // Only react to actual incoming damage, not healing.
        if (!args.DamageIncreased || args.Origin == null)
            return;

        var attacker = args.Origin.Value;

        // Ignore self-damage (e.g. fire ticks from the NPC's own fire).
        if (attacker == ent.Owner)
            return;

        if (!TryComp<HTNComponent>(ent.Owner, out var htn))
            return;

        var blackboard = htn.Blackboard;

        // Optionally skip if a target is already set (OnlyIfNoTarget mode).
        if (ent.Comp.OnlyIfNoTarget &&
            blackboard.TryGetValue<EntityUid>("Target", out var existing, EntityManager) &&
            existing.IsValid())
        {
            return;
        }

        // Pin the attacker as the new combat target.
        blackboard.SetValue("Target", attacker);
        blackboard.SetValue("TargetCoordinates", new EntityCoordinates(attacker, Vector2.Zero));

        // Only force an immediate replan when the NPC is idle (no active plan).
        // If a plan is already running (healing, reloading, fighting) we leave it alone;
        // the constant-replan cadence (~0.45 s) will pick up the updated target naturally.
        if (htn.Plan == null)
            _htn.Replan(htn);
    }
}
