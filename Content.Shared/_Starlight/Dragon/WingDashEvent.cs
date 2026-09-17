using Content.Shared.Actions;

namespace Content.Shared._Starlight.Dragon;

public sealed partial class WingDashEvent : WorldTargetActionEvent
{
    [DataField]
    public float DashDistance = 5.5f;
    [DataField]
    public float DashSpeed = 6f;
}
