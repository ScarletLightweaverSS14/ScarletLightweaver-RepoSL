using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Starlight.Voidwalker;

/// <summary>
/// Grants a summonable magazine of hovering crystals. Damage, range, cooldowns and
/// projectile lifetime are configured on the referenced action/projectile prototypes.
/// </summary>
[RegisterComponent]
public sealed partial class CrystalVolleyComponent : Component
{
    [DataField]
    public EntProtoId SummonAction = "ActionVoidwalkerCrystalVolley";

    [DataField]
    public EntProtoId FireAction = "ActionVoidwalkerLaunchCrystal";

    [DataField]
    public EntProtoId HoverPrototype = "VoidwalkerHoverCrystal";

    [DataField]
    public EntProtoId ProjectilePrototype = "VoidwalkerCrystalProjectile";

    [DataField]
    public EntProtoId? SummonEffect = "VoidwalkerCrystalFlash";

    [DataField]
    public int CrystalCount = 6;

    [DataField]
    public float ProjectileSpeed = 30f;

    [DataField]
    public TimeSpan VolleyLifetime = TimeSpan.FromSeconds(12);

    [DataField]
    public float OrbitRadius = 0.85f;

    /// <summary>Radians per second. Set to zero for a stationary formation.</summary>
    [DataField]
    public float OrbitSpeed = 0.45f;

    [DataField]
    public float BobHeight = 0.08f;

    [DataField]
    public float BobFrequency = 2f;

    [DataField]
    public LocId Callout = "voidwalker-crystal-volley-callout";

    [DataField]
    public SoundSpecifier SummonSound = new SoundPathSpecifier("/Audio/Effects/glass_crack1.ogg");

    [DataField]
    public SoundSpecifier FireSound = new SoundPathSpecifier("/Audio/Weapons/bladeslice.ogg");

    [DataField]
    public EntityUid? SummonActionEntity;

    [DataField]
    public EntityUid? FireActionEntity;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan ExpiresAt;

    /// <summary>Slots remain empty after firing, so the remaining crystals do not jump.</summary>
    public readonly List<EntityUid> Crystals = new();

    public float OrbitPhase;
}
