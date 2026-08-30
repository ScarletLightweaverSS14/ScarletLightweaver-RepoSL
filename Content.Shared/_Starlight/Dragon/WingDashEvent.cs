using Content.Shared.Actions;

namespace Content.Shared._Starlight.Dragon;

public sealed partial class WingDashEvent : InstantActionEvent
{
    [DataField]
    public float DashDistance = 4f;
    [DataField]
    public float DashSpeed = 3.5f;
}
