using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Starlight.Voidwalker;

/// <summary>Charge and aiming state. Projectile damage, armor penetration and hit count live in YAML.</summary>
[RegisterComponent]
public sealed partial class CrystalLanceComponent : Component
{
    [DataField]
    public EntProtoId Action = "ActionVoidwalkerCrystalLance";

    [DataField]
    public EntProtoId ChargePrototype = "VoidwalkerChargingLance";

    [DataField]
    public EntProtoId ProjectilePrototype = "VoidwalkerLanceProjectile";

    [DataField]
    public EntProtoId? ReleaseEffect = "VoidwalkerCrystalFlash";

    [DataField]
    public TimeSpan ChargeTime = TimeSpan.FromSeconds(0.8);

    [DataField]
    public float ProjectileSpeed = 60f;

    [DataField]
    public float ProjectileRange = 18f;

    [DataField]
    public float ChargeOffset = 0.45f;

    [DataField]
    public Vector2 InitialScale = new(0.25f, 0.25f);

    [DataField]
    public Vector2 FullScale = new(1.5f, 1.5f);

    [DataField]
    public SoundSpecifier ChargeSound = new SoundPathSpecifier("/Audio/Effects/glass_crack1.ogg");

    [DataField]
    public SoundSpecifier ReleaseSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Gunshots/laser_cannon.ogg");

    [DataField]
    public LocId Callout = "voidwalker-crystal-lance-callout";

    [DataField]
    public EntityUid? ActionEntity;

    public EntityUid? ChargingLance;

    /// <summary>World point captured on click; never a reference to the cursor or target entity.</summary>
    public MapCoordinates Target;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan ReleaseAt;
}
