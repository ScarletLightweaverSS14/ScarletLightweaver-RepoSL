using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Random;

namespace Content.Server._Starlight.Dragon;

public sealed partial class WesternDragonBossSystem
{
    [Dependency] private ChatSystem _chat = default!;

    private void CheckTaunts(EntityUid uid, WesternDragonBossComponent boss, float health)
    {
        if (!boss.Engaged)
            return;
        var phase = health < 0.33f ? 2 : health < 0.66f ? 1 : 0;
        if (phase > boss.HealthTaunts && TryTaunt(uid, boss, "western-dragon-taunt-wounded"))
            boss.HealthTaunts = phase;
        else if (boss.Target is { } target && TryComp<MobStateComponent>(target, out var mob) && mob.CurrentState == MobState.Dead)
            TryTaunt(uid, boss, "western-dragon-taunt-kill");
        else if (boss.CloseEnemies >= 3)
            TryTaunt(uid, boss, "western-dragon-taunt-surrounded");
        else if (boss.RecentDamage > 80)
            TryTaunt(uid, boss, "western-dragon-taunt-hurt");
    }

    private bool TryTaunt(EntityUid uid, WesternDragonBossComponent boss, string message)
    {
        if (!boss.Taunts || _timing.CurTime < boss.NextTaunt)
            return false;
        boss.NextTaunt = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(18, 28));
        boss.TauntsSpoken++;
        _chat.TrySendInGameICMessage(uid, Loc.GetString(message), InGameICChatType.Speak, hideChat: false);
        return true;
    }
}
