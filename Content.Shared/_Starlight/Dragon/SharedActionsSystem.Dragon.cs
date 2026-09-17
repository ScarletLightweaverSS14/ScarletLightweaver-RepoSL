using Content.Shared._Starlight.Dragon;
using Robust.Shared.Map;

namespace Content.Shared.Actions;

public abstract partial class SharedActionsSystem
{
    public bool TryPerformDragonAction(EntityUid performer, EntityUid action, EntityUid target)
    {
        if (!HasComp<WesternDragonFireBreathComponent>(performer))
            return false;
        return TryPerformAction(new RequestPerformActionEvent(GetNetEntity(action), GetNetEntity(target)), performer)
            && GetEvent(action)?.Handled == true;
    }

    /// <summary>NPC dragon actions use the same ownership, blockers, validation and cooldowns as player input.</summary>
    public bool TryPerformDragonAction(EntityUid performer, EntityUid action, EntityCoordinates target)
    {
        if (!HasComp<WesternDragonFireBreathComponent>(performer))
            return false;

        return TryPerformAction(new RequestPerformActionEvent(GetNetEntity(action), GetNetCoordinates(target)), performer)
            && GetEvent(action)?.Handled == true;
    }
}
