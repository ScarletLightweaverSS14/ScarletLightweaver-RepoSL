using Content.Shared._Starlight.Dragon;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Shared.Weapons.Ranged.Systems;

public sealed partial class ActionGunSystem
{
    private void OnWesternDragonShot(EntityUid user, ActionGunShootEvent args, bool fired)
    {
        if (fired && HasComp<WesternDragonFireBreathComponent>(user))
            args.Handled = true;
    }
}
