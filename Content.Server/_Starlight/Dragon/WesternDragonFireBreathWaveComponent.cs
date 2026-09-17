using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Starlight.Dragon;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WesternDragonFireBreathWaveComponent : Component
{
    // Planned once per cast, then consumed one stage at a time. No timer or entity per stage.
    public readonly List<Vector2i>[] Stages = [[], [], [], [], []];
    public int Stage;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextPulse;
}
