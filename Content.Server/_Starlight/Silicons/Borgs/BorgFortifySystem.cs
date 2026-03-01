// 🌟Starlight🌟
using Content.Shared._Starlight.Silicons.Borgs.BorgFortify;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Tools.Systems;

namespace Content.Server._Starlight.Silicons.Borgs;

/// <summary>
/// Handles the Fortify stance ability on assault borg modules:
/// — Toggle brace on/off via action (roots borg, absorbs 50% damage into shield HP)
/// — Shield breaks at 0 HP; must be welded shut before reuse
/// — InteractUsing with a lit welder triggers a timed repair DoAfter
/// </summary>
public sealed partial class BorgFortifySystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly SharedToolSystem _tool = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BorgFortifyActionEvent>(OnFortifyAction);
        SubscribeLocalEvent<BorgChassisComponent, BeforeDamageChangedEvent>(OnChassisBeforeDamage);
        SubscribeLocalEvent<BorgChassisComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<BorgChassisComponent, BorgFortifyRepairDoAfterEvent>(OnRepairDoAfter);
    }

    // ── Toggle ───────────────────────────────────────────────────────────────

    private void OnFortifyAction(BorgFortifyActionEvent ev)
    {
        if (ev.Handled)
            return;

        if (!TryComp<BorgChassisComponent>(ev.Performer, out var chassis))
            return;

        if (chassis.ModuleContainer == null)
            return;

        foreach (var moduleEnt in chassis.ModuleContainer.ContainedEntities)
        {
            if (!TryComp<BorgFortifyComponent>(moduleEnt, out var fortify))
                continue;

            if (fortify.ShieldBroken)
            {
                _popup.PopupEntity(
                    Loc.GetString("borg-fortify-broken"),
                    ev.Performer, ev.Performer, PopupType.MediumCaution);
                ev.Handled = true;
                return;
            }

            fortify.Fortified = !fortify.Fortified;
            Dirty(moduleEnt, fortify);
            _movement.RefreshMovementSpeedModifiers(ev.Performer);

            var msg = fortify.Fortified
                ? Loc.GetString("borg-fortify-raised", ("hp", (int) fortify.CurrentShieldHp))
                : Loc.GetString("borg-fortify-lowered");
            _popup.PopupEntity(msg, ev.Performer, ev.Performer, PopupType.Medium);

            ev.Handled = true;
            return;
        }
    }

    // ── Damage absorption ────────────────────────────────────────────────────

    private void OnChassisBeforeDamage(Entity<BorgChassisComponent> chassis, ref BeforeDamageChangedEvent args)
    {
        if (chassis.Comp.ModuleContainer == null)
            return;

        foreach (var moduleEnt in chassis.Comp.ModuleContainer.ContainedEntities)
        {
            if (!TryComp<BorgFortifyComponent>(moduleEnt, out var fortify))
                continue;

            if (!fortify.Fortified || fortify.ShieldBroken)
                return;

            // Reduce every damage value by DamageAbsorption; drain shield by the absorbed portion.
            float totalAbsorbed = 0f;
            foreach (var key in new List<string>(args.Damage.DamageDict.Keys))
            {
                var raw = args.Damage.DamageDict[key];
                if (raw <= 0)
                    continue;
                var absorbed = raw * (float) fortify.DamageAbsorption;
                args.Damage.DamageDict[key] = raw - absorbed;
                totalAbsorbed += (float) absorbed;
            }

            fortify.CurrentShieldHp -= totalAbsorbed;

            if (fortify.CurrentShieldHp <= 0f)
            {
                fortify.CurrentShieldHp = 0f;
                fortify.Fortified = false;
                fortify.ShieldBroken = true;
                _movement.RefreshMovementSpeedModifiers(chassis);
                _popup.PopupEntity(
                    Loc.GetString("borg-fortify-shield-broken"),
                    chassis, PopupType.LargeCaution);
            }

            Dirty(moduleEnt, fortify);
            return;
        }
    }

    // ── Welder repair ────────────────────────────────────────────────────────

    private void OnInteractUsing(Entity<BorgChassisComponent> chassis, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (chassis.Comp.ModuleContainer == null)
            return;

        foreach (var moduleEnt in chassis.Comp.ModuleContainer.ContainedEntities)
        {
            if (!TryComp<BorgFortifyComponent>(moduleEnt, out var fortify))
                continue;

            if (!fortify.ShieldBroken)
                return;

            args.Handled = _tool.UseTool(
                args.Used, args.User, chassis.Owner,
                fortify.RepairTime, "Welding",
                new BorgFortifyRepairDoAfterEvent(),
                fortify.RepairFuelCost);
            return;
        }
    }

    private void OnRepairDoAfter(Entity<BorgChassisComponent> chassis, ref BorgFortifyRepairDoAfterEvent args)
    {
        if (args.Cancelled || chassis.Comp.ModuleContainer == null)
            return;

        foreach (var moduleEnt in chassis.Comp.ModuleContainer.ContainedEntities)
        {
            if (!TryComp<BorgFortifyComponent>(moduleEnt, out var fortify))
                continue;

            fortify.CurrentShieldHp = fortify.MaxShieldHp;
            fortify.ShieldBroken = false;
            Dirty(moduleEnt, fortify);

            _popup.PopupEntity(
                Loc.GetString("borg-fortify-repaired", ("hp", (int) fortify.MaxShieldHp)),
                chassis, args.Args.User, PopupType.Medium);
            return;
        }
    }
}
