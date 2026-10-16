using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Starlight.TowerDefense;

[AdminCommand(AdminFlags.Fun)]
public sealed partial class TowerDefenseCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;
    public string Command => "tdsetup";
    public string Description => "Create and enter an isolated tower defense test arena.";
    public string Help => "tdsetup: creates a new arena and teleports you to its console. Requires an attached entity.";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0 || shell.Player?.AttachedEntity is not { } player)
        {
            shell.WriteError(Help);
            return;
        }

        _entities.System<TowerDefenseSystem>().CreateArena(player, out var map);
        shell.WriteLine($"Tower defense arena created on map {map}. Close the UI to view the arena; activate the console to reopen it.");
    }
}
