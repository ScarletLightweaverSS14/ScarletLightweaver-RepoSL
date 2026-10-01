using Content.Shared.Damage;
using Robust.Shared.Map;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Starlight.Voidwalker;

/// <summary>Server-only damage tuning and channel bookkeeping.</summary>
[RegisterComponent]
public sealed partial class CrystalBeamDamageComponent : Component
{
    [DataField(required: true)] public DamageSpecifier Damage = new();
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))] public TimeSpan NextPulse;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))] public TimeSpan NextAim;
    public MapCoordinates Target;
    public EntityUid? Audio;
    public readonly Dictionary<EntityUid, float> Targets = new();
}
