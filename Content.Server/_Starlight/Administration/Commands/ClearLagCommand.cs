using Content.Server._Starlight.Administration.UI;
using Content.Server.Administration;
using Content.Server.EUI;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Starlight.Administration.Commands;

/// <summary>
/// Opens the Clear Lag admin UI for the executing player.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class ClearLagCommand : LocalizedEntityCommands
{
    [Dependency] private readonly EuiManager _euiManager = default!;

    public override string Command => "clearlag";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var player = shell.Player;
        if (player == null)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        var eui = new ClearLagEui();
        _euiManager.OpenEui(eui, player);
    }
}
