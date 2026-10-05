using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Starlight.Weapons.Melee;

/// <summary>Scopes pre-cast validation to this action instead of subscribing to every targeted action.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NullstarRiftCleaveActionComponent : Component;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class NullstarRiftCleaveComponent : Component
{
    [DataField] public EntProtoId<WorldTargetActionComponent> Action = "ActionNullstarRiftCleave";
    [DataField, AutoNetworkedField] public EntityUid? ActionEntity;
    [DataField] public EntProtoId Rift = "EffectNullstarRiftCleave";
    [DataField] public TimeSpan Windup = TimeSpan.FromSeconds(0.8);
    [DataField] public TimeSpan Recovery = TimeSpan.FromSeconds(0.4);
    [DataField] public float DashRange = 3.5f;
    [DataField] public float DashSpeed = 18f;
    [DataField] public EntProtoId MotionEffect = "EffectNullstarCleaveMotion";
    [DataField] public SoundSpecifier DashSound = new SoundPathSpecifier("/Audio/_Starlight/Weapons/Nullstar/rift-dash.wav");

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan LockedUntil;

    public DoAfterId? PendingDoAfter;
    public EntityUid? PendingRift;
}

/// <summary>Warns at the projected dash endpoint, then cuts at the actual stopping point.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class NullstarRiftComponent : Component
{
    [DataField, AutoNetworkedField] public bool Active;
    [DataField, AutoNetworkedField] public float Length = 4.4f;
    [DataField, AutoNetworkedField] public float Width = 1.6f;
    [DataField, AutoNetworkedField] public float ResidualWidth = 0.5f;
    [DataField, AutoNetworkedField] public float WindupSeconds = 0.8f;
    [DataField, AutoNetworkedField] public float Duration = 2.5f;
    [DataField, AutoNetworkedField] public float CloseSeconds = 0.4f;
    [DataField] public float ForwardOffset = 0.9f;
    [DataField] public TimeSpan HitInterval = TimeSpan.FromSeconds(0.75);
    [DataField] public float PushDistance = 1f;
    [DataField] public DamageSpecifier DirectDamage = new();
    [DataField] public DamageSpecifier ResidualDamage = new();
    [DataField] public SoundSpecifier ReleaseSound = new SoundPathSpecifier("/Audio/_Starlight/Weapons/Nullstar/rift-cleave.wav");

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan PhaseStarted;

    // Server-only ownership and per-target limits. These effects are transient, not saved encounters.
    public EntityUid Caster;
    public EntityUid Weapon;
    public EntityUid? MotionEffect;
    public EntityCoordinates DashOrigin;
    public float DashDistance;
    // Elapsed phase seconds keep per-target limits stable when the grid is paused.
    public readonly Dictionary<EntityUid, float> LastHit = new();
    public readonly HashSet<EntityUid> Pushed = new();
    public float LastUpdate;
}

/// <summary>Short-lived control lock; the controller moves the body through ordinary physics.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NullstarCleaveDashComponent : Component
{
    public EntityUid Rift;
    public EntityUid Weapon;
    public Vector2 LastPosition;
    public bool FinishAfterStep;
    public float Elapsed;
    public bool Ending;
}

/// <summary>One bounded visual for the converging core, directional wake, and fading afterimage.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class NullstarCleaveMotionComponent : Component
{
    [DataField, AutoNetworkedField] public bool Dashing;
    [DataField, AutoNetworkedField] public bool Finished;
    [DataField, AutoNetworkedField] public float Distance;
    [DataField, AutoNetworkedField] public float Travelled;
    [DataField, AutoNetworkedField] public float WindupSeconds = 0.8f;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan PhaseStarted;
}

public sealed partial class NullstarRiftCleaveEvent : WorldTargetActionEvent;

[Serializable, NetSerializable]
public sealed partial class NullstarRiftCleaveDoAfterEvent : SimpleDoAfterEvent;
