using Content.Server._Starlight.RTS.Components;
using Content.Server.Administration;
using Content.Shared._Starlight.RTS;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Starlight.RTS;

[AdminCommand(AdminFlags.Fun)]
public sealed partial class RtsJoinCommand : LocalizedEntityCommands
{
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private RtsCommandSystem _commands = default!;

    public override string Command => "rtsjoin";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } session)
        {
            shell.WriteError(Loc.GetString("cmd-rtsjoin-in-game-only"));
            return;
        }

        if (args.Length > 2)
        {
            shell.WriteError(Help);
            return;
        }

        var team = RtsTeam.Blue;
        string? mapArgument = args.Length > 0 ? args[0] : null;
        string? teamArgument = args.Length > 1 ? args[1] : null;
        if (args.Length == 1 && Enum.TryParse<RtsTeam>(args[0], true, out var teamOnly) &&
            Enum.IsDefined(teamOnly) && !int.TryParse(args[0], out _))
        {
            team = teamOnly;
            mapArgument = null;
        }

        if (teamArgument != null &&
            (!Enum.TryParse(teamArgument, true, out team) || !Enum.IsDefined(team)))
        {
            shell.WriteError(Help);
            return;
        }

        if (!TryResolveBattlefield(shell, mapArgument, out var battlefield))
            return;

        var camera = _commands.Join(session, battlefield, team);
        if (session.AttachedEntity != camera)
        {
            _commands.Leave(camera);
            shell.WriteError(Loc.GetString("cmd-rtsjoin-failed"));
            return;
        }

        var id = EntityManager.GetComponent<MapComponent>(battlefield).MapId;
        shell.WriteLine(Loc.GetString("cmd-rtsjoin-joined", ("team", team.ToString()), ("map", id.ToString())));
    }

    private bool TryResolveBattlefield(IConsoleShell shell, string? argument, out Entity<RtsBattlefieldComponent> battlefield)
    {
        battlefield = default;
        if (argument != null)
        {
            if (!int.TryParse(argument, out var id) || !_maps.TryGetMap(new MapId(id), out var map) ||
                !EntityManager.TryGetComponent<RtsBattlefieldComponent>(map, out var component))
            {
                shell.WriteError(Loc.GetString("cmd-rtsjoin-invalid-map", ("map", argument)));
                return false;
            }

            battlefield = (map.Value, component);
            return true;
        }

        if (shell.Player?.AttachedEntity is { } player &&
            EntityManager.GetComponent<TransformComponent>(player).MapUid is { } currentMap &&
            EntityManager.TryGetComponent<RtsBattlefieldComponent>(currentMap, out var currentBattlefield))
        {
            battlefield = (currentMap, currentBattlefield);
            return true;
        }

        var candidates = new List<Entity<RtsBattlefieldComponent>>();
        var mapIds = new List<string>();
        var query = EntityManager.EntityQueryEnumerator<RtsBattlefieldComponent, MapComponent>();
        while (query.MoveNext(out var uid, out var component, out var map))
        {
            candidates.Add((uid, component));
            mapIds.Add(map.MapId.ToString());
        }

        if (candidates.Count != 1)
        {
            shell.WriteError(candidates.Count == 0
                ? Loc.GetString("cmd-rtsjoin-no-battlefields")
                : Loc.GetString("cmd-rtsjoin-choose-map", ("maps", string.Join(", ", mapIds))));
            return false;
        }

        battlefield = candidates[0];
        return true;
    }
}
