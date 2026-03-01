using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._Starlight.Weapons.GunOverheat;

/// <summary>
/// Manages heat buildup and overheat lockouts for guns with
/// <see cref="GunOverheatComponent"/>.
///
/// Heat is added on every fired projectile via <see cref="AmmoShotEvent"/>.
/// When heat reaches <see cref="GunOverheatComponent.MaxHeat"/> the gun enters
/// an overheat lockout, cancelling all fire attempts until the lockout expires
/// and the gun cools back to zero.
///
/// Cooling runs server-side only; networked fields keep clients in sync.
/// </summary>
public sealed partial class GunOverheatSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GunOverheatComponent, AmmoShotEvent>(OnShot);
        SubscribeLocalEvent<GunOverheatComponent, ShotAttemptedEvent>(OnShotAttempted);
    }

    private void OnShot(Entity<GunOverheatComponent> gun, ref AmmoShotEvent args)
    {
        if (!_net.IsServer)
            return;

        if (gun.Comp.IsOverheated)
            return;

        gun.Comp.Heat = MathF.Min(
            gun.Comp.Heat + gun.Comp.HeatPerShot * args.FiredProjectiles.Count,
            gun.Comp.MaxHeat);

        if (gun.Comp.Heat >= gun.Comp.MaxHeat)
        {
            gun.Comp.IsOverheated = true;
            gun.Comp.OverheatEndsAt = _timing.CurTime + TimeSpan.FromSeconds(gun.Comp.OverheatDuration);

            if (args.Shooter is { } shooter)
            {
                _popup.PopupEntity(
                    Loc.GetString("gun-overheated"),
                    gun.Owner,
                    shooter,
                    PopupType.LargeCaution);
            }
        }

        Dirty(gun);
    }

    private void OnShotAttempted(Entity<GunOverheatComponent> gun, ref ShotAttemptedEvent args)
    {
        if (!gun.Comp.IsOverheated)
            return;

        args.Cancel();
        args.Message = Loc.GetString("gun-overheated-blocked");
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_net.IsServer)
            return;

        var query = EntityQueryEnumerator<GunOverheatComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.IsOverheated && comp.Heat <= 0f)
                continue;

            if (comp.IsOverheated)
            {
                // Still locked out — wait for the overheat duration to expire.
                if (_timing.CurTime < comp.OverheatEndsAt)
                    continue;

                comp.IsOverheated = false;
            }

            // Passive cooling.
            comp.Heat = MathF.Max(0f, comp.Heat - comp.CooldownRate * frameTime);
            Dirty(uid, comp);
        }
    }
}
