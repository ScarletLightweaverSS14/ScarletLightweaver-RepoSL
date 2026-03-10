using Content.Server.Chat.Systems;
using Content.Server.NPC.Components;
using Content.Shared._Starlight.NPC;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Damage.ForceSay;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.NPC;

/// <summary>
/// Handles NPC flavour callouts:
///  • Spot callout  — when the NPC first becomes active in combat (has a ranged or melee combat component).
///  • Hurt callout  — when the NPC takes a single hit above <see cref="NpcCalloutComponent.HurtThreshold"/>.
///  • Crit callout  — last words when the NPC enters critical condition.
/// </summary>
public sealed partial class NpcCalloutSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NpcCalloutComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<NpcCalloutComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    // ── Spot ────────────────────────────────────────────────────────────────────
    // Polled every frame: fires once when the NPC first has an active combat component.

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<NpcCalloutComponent>();
        while (query.MoveNext(out var uid, out var callout))
        {
            // Already called out once this engagement.
            if (callout.NextSpotCallout != TimeSpan.Zero)
                continue;

            // Only shout when actively engaging an enemy.
            if (!HasComp<NPCRangedCombatComponent>(uid) && !HasComp<NPCMeleeCombatComponent>(uid))
                continue;

            TrySaySpot((uid, callout));
        }
    }

    private void TrySaySpot(Entity<NpcCalloutComponent> ent)
    {
        if (ent.Comp.SpotPhrases.Count == 0)
            return;

        var phrase = _random.Pick(ent.Comp.SpotPhrases);
        _chat.TrySendInGameICMessage(ent, phrase, InGameICChatType.Speak, ChatTransmitRange.Normal, hideLog: true);
        // Set cooldown so it plays again after SpotCooldown seconds, not every frame.
        ent.Comp.NextSpotCallout = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.SpotCooldown);
    }

    // ── Hurt ────────────────────────────────────────────────────────────────────

    private void OnDamageChanged(Entity<NpcCalloutComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageDelta == null || !args.DamageIncreased)
            return;

        var total = (float) args.DamageDelta.GetTotal();
        if (total < ent.Comp.HurtThreshold)
            return;

        var now = _timing.CurTime;
        if (now < ent.Comp.NextHurtCallout)
            return;

        if (ent.Comp.HurtPhrases.Count == 0)
            return;

        var phrase = _random.Pick(ent.Comp.HurtPhrases);
        _chat.TrySendInGameICMessage(ent, phrase, InGameICChatType.Speak, ChatTransmitRange.Normal, hideLog: true);
        ent.Comp.NextHurtCallout = now + TimeSpan.FromSeconds(ent.Comp.HurtCooldown);
    }

    // ── Crit (last words) ───────────────────────────────────────────────────────

    private void OnMobStateChanged(Entity<NpcCalloutComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Critical)
            return;

        if (ent.Comp.CritPhrases.Count == 0)
            return;

        // Critical mobs normally can't speak — grant one-time speech permission.
        EnsureComp<AllowNextCritSpeechComponent>(ent);

        var phrase = _random.Pick(ent.Comp.CritPhrases);
        _chat.TrySendInGameICMessage(ent, phrase, InGameICChatType.Speak, ChatTransmitRange.Normal, hideLog: true);
        // Set a long cooldown so last words don't spam if mob flickers in/out of crit.
        ent.Comp.NextHurtCallout = _timing.CurTime + TimeSpan.FromSeconds(30);
    }
}
