using System.Globalization;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._Starlight.RTS;

[AdminCommand(AdminFlags.Fun)]
public sealed partial class RtsMapCommand : LocalizedEntityCommands
{
    [Dependency] private RtsBattlefieldSystem _battlefields = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override string Command => "rtsmap";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var seed = _random.Next();
        int? size = null;
        if (args.Length > 2 || (args.Length >= 1 && !int.TryParse(args[0], out seed)))
        {
            shell.WriteError(Help);
            return;
        }

        if (args.Length == 2)
        {
            if (!int.TryParse(args[1], out var parsedSize) ||
                parsedSize < RtsBattlefieldSystem.MinimumSize || parsedSize > RtsBattlefieldSystem.MaximumSize)
            {
                shell.WriteError(Loc.GetString("cmd-rtsmap-invalid-size"));
                return;
            }

            size = parsedSize;
        }

        var battlefield = _battlefields.Generate(seed, size);
        var mapId = EntityManager.GetComponent<MapComponent>(battlefield).MapId;
        var headquarters = EntityManager.GetComponent<TransformComponent>(battlefield.Comp.BlueHeadquarters).LocalPosition;
        shell.WriteLine(Loc.GetString("cmd-rtsmap-created",
            ("map", mapId.ToString()), ("seed", seed), ("size", battlefield.Comp.Size),
            ("x", (headquarters.X + 3).ToString(CultureInfo.InvariantCulture)),
            ("y", headquarters.Y.ToString(CultureInfo.InvariantCulture))));
    }
}
