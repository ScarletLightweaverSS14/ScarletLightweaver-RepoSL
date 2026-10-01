using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Mobs.Components;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._Starlight.Voidwalker;

[AdminCommand(AdminFlags.Fun)]
public sealed partial class MakeVoidwalkerCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _players = default!;

    public string Command => "Makervoidwalker";
    public string Description => Loc.GetString("cmd-makervoidwalker-description");
    public string Help => Loc.GetString("cmd-makervoidwalker-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 1)
        {
            shell.WriteError(Help);
            return;
        }

        var player = shell.Player;
        if (args.Length == 1 && !_players.TryGetSessionByUsername(args[0], out player))
        {
            shell.WriteError(Loc.GetString("shell-target-player-does-not-exist"));
            return;
        }

        if (player?.AttachedEntity is not { } target || !_entities.HasComponent<MobStateComponent>(target))
        {
            shell.WriteError(Loc.GetString("cmd-makervoidwalker-no-body"));
            return;
        }

        // Repeating the command must not duplicate actions or reset an active volley/cooldown.
        _entities.EnsureComponent<CrystalVolleyComponent>(target);
        _entities.EnsureComponent<CrystalGroundSpikesComponent>(target);
        _entities.EnsureComponent<CrystalBurstComponent>(target);
        _entities.EnsureComponent<CrystalPrisonComponent>(target);
        _entities.EnsureComponent<CrystalLanceComponent>(target);
        _entities.EnsureComponent<CrystalBeamAbilityComponent>(target);
        shell.WriteLine(Loc.GetString("cmd-makervoidwalker-success", ("player", player.Name)));
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _players),
                Loc.GetString("cmd-makervoidwalker-player"))
            : CompletionResult.Empty;
    }
}
