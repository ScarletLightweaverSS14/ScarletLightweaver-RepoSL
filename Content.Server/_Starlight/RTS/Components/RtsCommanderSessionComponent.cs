using Robust.Shared.Map;

namespace Content.Server._Starlight.RTS.Components;

/// <summary>Server-only visit and rate-limit data for a commander camera.</summary>
[RegisterComponent]
public sealed partial class RtsCommanderSessionComponent : Component
{
    public EntityUid Mind;
    public EntityCoordinates ReturnCoordinates;
    public TimeSpan NextOrder;
}
