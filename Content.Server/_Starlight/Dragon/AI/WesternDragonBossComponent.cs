using Robust.Shared.Map;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Starlight.Dragon;

/// <summary>Per-dragon combat memory. HTN operators themselves are shared singletons.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WesternDragonBossComponent : Component
{
    [DataField] public float ThinkInterval = 0.5f;
    [DataField] public float AbilityInterval = 6f;
    [DataField] public float TailSlamHealth = 0.85f;
    [DataField] public float BreathHealth = 0.7f;
    [DataField] public float FireballHealth = 0.5f;
    // Damage unlocks persist through healing and HTN replanning.
    [DataField] public float LowestHealth = 1f;
    [DataField] public float TargetCommitment = 3f;
    [DataField] public float ForgetTargetAfter = 3f;
    [DataField] public bool Taunts = true;
    [DataField] public bool LairTauntSpoken;
    [DataField] public float DevourSafeTime = 30f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextThink;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextAbility;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextPosition;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextReposition;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ComboUntil;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextRetarget;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan LastSeen;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextTaunt;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan RecoverUntil;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan DevourAvailableAt;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextDevourAttempt;

    [ViewVariables] public EntityUid? Target;
    [ViewVariables] public EntityUid? ComboTarget;
    [ViewVariables] public EntityCoordinates? LastKnownPosition;
    [ViewVariables] public DragonAbility LastAbility;
    [ViewVariables] public DragonAbility PreviousAbility;
    [ViewVariables] public DragonPositioning Positioning;
    [ViewVariables] public float RecentDamage;
    [ViewVariables] public int CloseEnemies;
    [ViewVariables] public int VisibleEnemies;
    [ViewVariables] public int HealthTaunts;
    [ViewVariables] public int AbilitiesUsed;
    [ViewVariables] public int TauntsSpoken;
    [ViewVariables] public bool Engaged;
    public float CircleDirection = 1f;
    public readonly Dictionary<EntityUid, float> Threat = new();
    [ViewVariables] public DoAfterId? DevourDoAfter;
    [ViewVariables] public EntityUid? FoodTarget;
}

public enum DragonAbility : byte { None, TailSlam, Breath, Fireball, Roar, Dash }
public enum DragonPositioning : byte { Approach, Circle, Retreat, Recover, Search, Feed }
