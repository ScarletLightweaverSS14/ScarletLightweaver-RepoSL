using Content.Shared._Starlight.FiringPins;
using Content.Shared._Starlight.FiringPins.Functionality;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;

namespace Content.Server._Starlight.FiringPins.Functionality;

/// <summary>
/// Server-side system for <see cref="FiringPinCrystalComponent"/>.
/// Heat only accumulates during sustained fire; cooling is delayed until the player stops.
/// Overheat shattering is fully deterministic via a durability counter — no RNG.
/// </summary>
public sealed partial class FiringPinCrystalSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FiringPinCrystalComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<FiringPinCrystalComponent, FiringPinFireAttemptEvent>(OnFireAttempt);
        SubscribeLocalEvent<FiringPinCrystalComponent, ExaminedEvent>(OnExamined);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<FiringPinCrystalComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Enabled)
                continue;

            if (comp.CurrentHeat <= 0f && comp.OverheatLockoutTimer <= 0f)
                continue;

            comp.TimeSinceLastShot += frameTime;

            // Post-overheat firing lockout: freeze heat so a single shot can't re-trigger instantly.
            if (comp.OverheatLockoutTimer > 0f)
            {
                comp.OverheatLockoutTimer -= frameTime;
                comp.CurrentHeat = MathF.Max(comp.CurrentHeat, comp.MaxHeat);
                Dirty(uid, comp);
                continue;
            }

            // Only cool after CooldownDelay seconds of not firing.
            if (comp.TimeSinceLastShot >= comp.CooldownDelay)
            {
                comp.CurrentHeat = MathF.Max(0f, comp.CurrentHeat - comp.CooldownRate * frameTime);

                if (comp.IsOverheated && comp.CurrentHeat <= 0f)
                    comp.IsOverheated = false;
            }

            Dirty(uid, comp);
        }
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnMapInit(Entity<FiringPinCrystalComponent> ent, ref MapInitEvent args)
    {
        // Sync CurrentDurability to MaxDurability so YAML-overridden maxDurability is respected.
        ent.Comp.CurrentDurability = ent.Comp.MaxDurability;
    }

    private void OnExamined(Entity<FiringPinCrystalComponent> ent, ref ExaminedEvent args)
    {
        if (!ent.Comp.Enabled || !args.IsInDetailsRange)
            return;

        var heatPct = ent.Comp.MaxHeat > 0f ? ent.Comp.CurrentHeat / ent.Comp.MaxHeat : 0f;

        string heatKey;
        if (heatPct >= 1.0f || ent.Comp.OverheatLockoutTimer > 0f)
            heatKey = "firing-pin-crystal-heat-critical";
        else if (heatPct >= 0.66f)
            heatKey = "firing-pin-crystal-heat-high";
        else if (heatPct >= 0.33f)
            heatKey = "firing-pin-crystal-heat-medium";
        else
            heatKey = "firing-pin-crystal-heat-low";

        args.PushMarkup(Loc.GetString(heatKey));

        // Durability — show crystal condition if it has taken any overheat damage.
        if (ent.Comp.CurrentDurability < ent.Comp.MaxDurability)
        {
            var durPct = ent.Comp.MaxDurability > 0
                ? (float)ent.Comp.CurrentDurability / ent.Comp.MaxDurability
                : 0f;

            string durKey;
            if (durPct <= 0.2f)
                durKey = "firing-pin-crystal-degraded-high";
            else if (durPct <= 0.6f)
                durKey = "firing-pin-crystal-degraded-medium";
            else
                durKey = "firing-pin-crystal-degraded-low";

            args.PushMarkup(Loc.GetString(durKey));
        }

        // While overheated, tell the player exactly how many shots remain before it shatters.
        if (ent.Comp.IsOverheated && ent.Comp.CurrentDurability > 0)
            args.PushMarkup(Loc.GetString("firing-pin-crystal-shots-until-break", ("shots", ent.Comp.CurrentDurability)));
    }

    private void OnFireAttempt(Entity<FiringPinCrystalComponent> ent, ref FiringPinFireAttemptEvent args)
    {
        if (!ent.Comp.Enabled || args.Cancelled)
            return;

        // Post-overheat lockout.
        if (ent.Comp.OverheatLockoutTimer > 0f)
        {
            args.Cancelled = true;
            return;
        }

        ent.Comp.TimeSinceLastShot = 0f;
        ent.Comp.CurrentHeat += ent.Comp.HeatPerShot;

        // Transition into overheated state.
        if (!ent.Comp.IsOverheated && ent.Comp.CurrentHeat >= ent.Comp.MaxHeat)
        {
            ent.Comp.IsOverheated = true;
            ent.Comp.OverheatLockoutTimer = ent.Comp.LockoutDuration;
            _audio.PlayPvs(ent.Comp.OverheatSound, args.Gun);
            _popup.PopupEntity(
                Loc.GetString("firing-pin-crystal-overheat"),
                args.Gun,
                args.User,
                PopupType.MediumCaution);
        }

        // Deterministic durability: each shot while overheated costs one point.
        if (ent.Comp.IsOverheated)
        {
            ent.Comp.CurrentDurability--;

            if (ent.Comp.CurrentDurability <= 0)
                BreakCrystal(ent, ref args);
        }

        Dirty(ent);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void BreakCrystal(Entity<FiringPinCrystalComponent> ent, ref FiringPinFireAttemptEvent args)
    {
        _audio.PlayPvs(ent.Comp.BreakSound, args.Gun);
        _popup.PopupEntity(
            Loc.GetString("firing-pin-crystal-broke"),
            args.Gun,
            args.User,
            PopupType.LargeCaution);

        _container.TryRemoveFromContainer(ent.Owner);
        QueueDel(ent.Owner);
    }
}
