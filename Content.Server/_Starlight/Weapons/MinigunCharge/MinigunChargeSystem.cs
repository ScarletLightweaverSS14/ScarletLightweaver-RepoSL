using Content.Shared._Starlight.Weapons.MinigunCharge;
using Content.Shared.DoAfter;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Weapons.MinigunCharge;

/// <summary>
/// Handles the minigun spin-up mechanic.
/// On first fire attempt, cancels the shot, shows messages, plays the spin-up sound,
/// and starts a DoAfter. Once complete, the gun is "hot" for <c>ChargedDuration</c> seconds.
/// </summary>
public sealed class MinigunChargeSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MinigunChargeComponent, ShotAttemptedEvent>(OnShotAttempted);
        SubscribeLocalEvent<MinigunChargeComponent, MinigunChargeDoAfterEvent>(OnChargeComplete);
    }

    private void OnShotAttempted(Entity<MinigunChargeComponent> ent, ref ShotAttemptedEvent args)
    {
        // Gun is already charged — allow the shot.
        if (_timing.CurTime < ent.Comp.NextReadyTime)
            return;

        // Block the shot.
        args.Cancel();

        // Already spinning up — don't restart or spam messages.
        if (ent.Comp.IsCharging)
            return;

        // Begin spin-up.
        ent.Comp.IsCharging = true;

        // Private floating popup for the shooter.
        _popup.PopupEntity(
            Loc.GetString("minigun-charge-whirr-self"),
            args.User,
            args.User,
            PopupType.Medium);

        // Subtle floating popup visible to nearby players (not the shooter).
        var xform = Transform(ent.Owner);
        _popup.PopupCoordinates(
            Loc.GetString("minigun-charge-whirr-others"),
            xform.Coordinates,
            Filter.Pvs(ent.Owner).RemovePlayerByAttachedEntity(args.User),
            true,
            PopupType.Small);

        // Play the whirr sound at the gun's position, lower pitch.
        _audio.PlayEntity(
            ent.Comp.ChargeSound,
            Filter.Pvs(ent.Owner),
            ent.Owner,
            true,
            AudioParams.Default.WithPitchScale(0.55f));

        // Start the DoAfter spin-up.
        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            args.User,
            ent.Comp.ChargeTime,
            new MinigunChargeDoAfterEvent(),
            ent.Owner,
            used: ent.Owner)
        {
            BreakOnHandChange = true,
            BreakOnMove = true,
            NeedHand = true,
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnChargeComplete(Entity<MinigunChargeComponent> ent, ref MinigunChargeDoAfterEvent args)
    {
        ent.Comp.IsCharging = false;

        if (args.Cancelled)
            return;

        // Mark the gun as hot.
        ent.Comp.NextReadyTime = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.ChargedDuration);

        _popup.PopupEntity(
            Loc.GetString("minigun-charge-ready"),
            args.User,
            args.User,
            PopupType.LargeCaution);
    }
}
