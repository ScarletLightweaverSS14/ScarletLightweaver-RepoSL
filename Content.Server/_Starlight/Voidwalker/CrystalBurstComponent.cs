using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Starlight.Voidwalker;

/// <summary>Forms an expanding ring of crystals, then releases a short-range radial burst.</summary>
[RegisterComponent]
public sealed partial class CrystalBurstComponent : Component
{
    [DataField]
    public EntProtoId Action = "ActionVoidwalkerCrystalBurst";

    [DataField]
    public EntProtoId HoverPrototype = "VoidwalkerBurstHover";

    /// <summary>Damage and impact effects are configured on this projectile prototype.</summary>
    [DataField]
    public EntProtoId ProjectilePrototype = "VoidwalkerBurstProjectile";

    [DataField]
    public EntProtoId? BurstEffect = "VoidwalkerBurstFlash";

    [DataField]
    public int CrystalCount = 24;

    [DataField]
    public TimeSpan WarningDuration = TimeSpan.FromSeconds(0.4);

    [DataField]
    public float InitialRadius = 0.35f;

    [DataField]
    public float FormationRadius = 1f;

    [DataField]
    public float ProjectileSpeed = 24f;

    /// <summary>Flight distance from each crystal's launch position, enforced by TimedDespawn.</summary>
    [DataField]
    public float ProjectileRange = 5f;

    [DataField]
    public SoundSpecifier FormationSound = new SoundPathSpecifier("/Audio/Effects/glass_crack1.ogg");

    [DataField]
    public SoundSpecifier BurstSound = new SoundPathSpecifier("/Audio/Effects/glass_break1.ogg");

    [DataField]
    public LocId Callout = "voidwalker-crystal-burst-callout";

    [DataField]
    public EntityUid? ActionEntity;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan LaunchAt;

    public readonly List<EntityUid> Crystals = new();
}
