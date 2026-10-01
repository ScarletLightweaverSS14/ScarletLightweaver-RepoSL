using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Physics;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Starlight.Voidwalker;

/// <summary>
/// A travelling eruption attached to the floor grid, independent of the caster's movement.
/// The system resolves targets when each spike erupts, allowing enemies to dodge the wave.
/// </summary>
[RegisterComponent]
public sealed partial class CrystalFaultLineComponent : Component
{
    [DataField]
    public EntProtoId SpikePrototype = "VoidwalkerGroundSpike";

    [DataField]
    public EntProtoId? EruptionEffect = "VoidwalkerCrystalFlash";

    [DataField]
    public SoundSpecifier EruptionSound = new SoundPathSpecifier("/Audio/Effects/glass_crack2.ogg");

    [DataField]
    public int SpikeCount = 6;

    [DataField]
    public float FirstSpikeDistance = 1f;

    [DataField]
    public float SpikeSpacing = 1f;

    [DataField]
    public TimeSpan InitialDelay = TimeSpan.FromSeconds(0.2);

    [DataField]
    public TimeSpan EruptionDelay = TimeSpan.FromSeconds(0.15);

    [DataField]
    public float HitRadius = 0.6f;

    [DataField(required: true)]
    public DamageSpecifier Damage = new();

    [DataField]
    public float StaminaDamage = 125f;

    /// <summary>Prevents adjacent eruptions from multiplying a single victim's damage.</summary>
    [DataField]
    public bool HitOncePerCast = true;

    [DataField]
    public CollisionGroup ObstructionMask = CollisionGroup.Impassable | CollisionGroup.InteractImpassable;

    [DataField]
    public EntityUid? Caster;

    /// <summary>Snapshot of the cast direction, in the floor grid's local space.</summary>
    [DataField]
    public Vector2 Direction;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextEruption;

    public int NextSpike;
    public Vector2 PreviousOffset;
    public readonly HashSet<EntityUid> HitEntities = new();
}
