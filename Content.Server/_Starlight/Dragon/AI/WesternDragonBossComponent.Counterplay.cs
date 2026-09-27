using Robust.Shared.Map;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Starlight.Dragon;

public sealed partial class WesternDragonBossComponent
{
    [DataField] public float CounterplayDelay = 6f;
    [DataField] public float CounterplayDuration = 8f;
    [DataField] public float CounterplayCooldown = 20f;
    [DataField] public float CounterplayRange = 24f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan PursuitAttackedUntil;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan PursuitSampleAt;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan CounterplayUntil;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan CounterplayReadyAt;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextCounterplay;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextPathRetry;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan RecentAttackUntil;

    [ViewVariables] public EntityUid? PursuitTarget;
    [ViewVariables] public float PursuitDistance;
    [ViewVariables] public EntityUid? CounterplayTarget;
    [ViewVariables] public EntityUid? RecentAttacker;
    [ViewVariables] public EntityCoordinates? RecentAttackPosition;
}
