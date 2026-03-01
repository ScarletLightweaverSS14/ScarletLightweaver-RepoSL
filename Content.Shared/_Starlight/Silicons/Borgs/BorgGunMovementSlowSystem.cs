using Content.Shared._Starlight.Silicons.Borgs.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Silicons.Borgs.Components;

namespace Content.Shared._Starlight.Silicons.Borgs;

/// <summary>
/// Triggers a movement speed refresh on the borg chassis when a module carrying
/// <see cref="BorgGunMovementSlowComponent"/> is installed or uninstalled.
/// The actual speed modifier is applied inside
/// <see cref="SharedBorgSystem.OnRefreshMovementSpeedModifiers"/> to avoid
/// a duplicate subscription on (BorgChassisComponent, RefreshMovementSpeedModifiersEvent).
/// </summary>
public sealed partial class BorgGunMovementSlowSystem : EntitySystem
{
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeedModifier = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Refresh borg movement when a slowing module is installed or uninstalled.
        SubscribeLocalEvent<BorgGunMovementSlowComponent, BorgModuleInstalledEvent>(OnModuleInstalled);
        SubscribeLocalEvent<BorgGunMovementSlowComponent, BorgModuleUninstalledEvent>(OnModuleUninstalled);
    }

    private void OnModuleInstalled(Entity<BorgGunMovementSlowComponent> module, ref BorgModuleInstalledEvent args)
    {
        _movementSpeedModifier.RefreshMovementSpeedModifiers(args.ChassisEnt);
    }

    private void OnModuleUninstalled(Entity<BorgGunMovementSlowComponent> module, ref BorgModuleUninstalledEvent args)
    {
        _movementSpeedModifier.RefreshMovementSpeedModifiers(args.ChassisEnt);
    }
}
