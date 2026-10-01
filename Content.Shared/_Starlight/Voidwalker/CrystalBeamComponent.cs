using Content.Shared.Actions;
using Content.Shared.Physics;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Starlight.Voidwalker;

/// <summary>Configuration and replicated channel state; behavior belongs to the beam systems.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CrystalBeamComponent : Component
{
    [DataField, AutoNetworkedField] public EntityUid? Caster;
    [DataField, AutoNetworkedField] public Angle Direction;
    [DataField, AutoNetworkedField] public float Length;
    [DataField, AutoNetworkedField] public bool Firing;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField]
    public TimeSpan StartedAt;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField]
    public TimeSpan FireAt;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField]
    public TimeSpan EndAt;

    [DataField] public TimeSpan ChargeTime = TimeSpan.FromSeconds(1);
    [DataField] public TimeSpan Duration = TimeSpan.FromSeconds(3);
    [DataField] public TimeSpan PulseInterval = TimeSpan.FromSeconds(0.25);
    [DataField] public TimeSpan AimInterval = TimeSpan.FromSeconds(0.1);
    [DataField] public float Range = 12;
    [DataField, AutoNetworkedField] public float Width = 0.3f;
    /// <summary>Maximum beam turn speed in degrees per second, independent of body facing.</summary>
    [DataField] public double RotationSpeed = 90;
    /// <summary>Parallel rays across the damaging width. Includes the center and both edges.</summary>
    [DataField] public int RayCount = 5;
    [DataField] public CollisionGroup CollisionMask = CollisionGroup.Impassable | CollisionGroup.BulletImpassable | CollisionGroup.InteractImpassable;

    [DataField] public int FocusCount = 5;
    [DataField] public EntProtoId FocusPrototype = "VoidwalkerBeamFocus";
    [DataField] public float FocusRadius = 0.9f;
    [DataField] public float ChargedFocusRadius = 0.45f;
    [DataField] public float ParticleSpacing = 0.6f;
    [DataField] public float ParticleSpeed = 9f;
    [DataField] public EntProtoId? ImpactPrototype = "VoidwalkerBeamImpact";
    [DataField] public TimeSpan ImpactInterval = TimeSpan.FromSeconds(0.2);
    [DataField] public EntProtoId? GrowthPrototype = "VoidwalkerBeamGrowth";
    [DataField] public TimeSpan GrowthInterval = TimeSpan.FromSeconds(0.45);
    [DataField] public float Flicker = 0.15f;
    [DataField] public Color OuterColor = Color.FromHex("#8939ff70");
    [DataField] public Color InnerColor = Color.FromHex("#c988ffdc");
    [DataField] public Color CoreColor = Color.FromHex("#fff1ff");
    [DataField] public SoundSpecifier? ChargeSound;
    [DataField] public SoundSpecifier? SustainSound;
    [DataField] public LocId Callout = "voidwalker-crystal-beam-callout";
}

public sealed partial class CrystalBeamEvent : WorldTargetActionEvent;

[Serializable, NetSerializable]
public sealed class CrystalBeamAimEvent(NetEntity channel, MapCoordinates target) : EntityEventArgs
{
    public NetEntity Channel = channel;
    public MapCoordinates Target = target;
}
