using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Starlight.Dragon;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WesternDragonFireWaveComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextPulse;

    [DataField]
    public int Radius;
}
