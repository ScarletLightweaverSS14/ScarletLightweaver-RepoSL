// 🌟Starlight🌟
using Content.Shared.Actions;
using Content.Shared.Silicons.Borgs.Components;

namespace Content.Shared._Starlight.Silicons.Borgs.BorgModuleActionGrant;

/// <summary>
/// Grants/revokes actions on a borg chassis when a module carrying
/// <see cref="BorgModuleActionGrantComponent"/> is installed or uninstalled.
/// </summary>
public sealed partial class BorgModuleActionGrantSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BorgModuleActionGrantComponent, BorgModuleInstalledEvent>(OnInstalled);
        SubscribeLocalEvent<BorgModuleActionGrantComponent, BorgModuleUninstalledEvent>(OnUninstalled);
    }

    private void OnInstalled(Entity<BorgModuleActionGrantComponent> module, ref BorgModuleInstalledEvent args)
    {
        foreach (var proto in module.Comp.Actions)
        {
            EntityUid? actionEnt = null;
            _actions.AddAction(args.ChassisEnt, ref actionEnt, proto, module.Owner);
            if (actionEnt != null)
                module.Comp.ActionEntities.Add(actionEnt.Value);
        }
        Dirty(module);
    }

    private void OnUninstalled(Entity<BorgModuleActionGrantComponent> module, ref BorgModuleUninstalledEvent args)
    {
        foreach (var actionEnt in module.Comp.ActionEntities)
        {
            _actions.RemoveAction(args.ChassisEnt, actionEnt);
            _actionContainer.RemoveAction(actionEnt);
        }
        module.Comp.ActionEntities.Clear();
        Dirty(module);
    }
}
