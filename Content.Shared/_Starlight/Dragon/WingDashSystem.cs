using Content.Shared.Actions;
using Content.Shared.Throwing;

namespace Content.Shared._Starlight.Dragon;

public sealed partial class WingDashSystem : EntitySystem
{
    [Dependency] private readonly ThrowingSystem _throwing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WingDashEvent>(OnWingDash);
    }

    private void OnWingDash(WingDashEvent ev)
    {
        if (ev.Handled)
            return;

        var user = ev.Performer;
        var dir = Transform(user).LocalRotation.ToWorldVec().Normalized();
        _throwing.TryThrow(user, dir * ev.DashDistance, ev.DashSpeed, animated: false);
        ev.Handled = true;
    }
}
