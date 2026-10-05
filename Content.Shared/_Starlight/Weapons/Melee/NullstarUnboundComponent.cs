using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Starlight.Weapons.Melee;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NullstarUnboundComponent : Component
{
    /// <summary>Only the preview prototype grants a manual release action.</summary>
    [DataField]
    public EntProtoId<InstantActionComponent>? PreviewAction;

    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;

    [DataField, AutoNetworkedField]
    public bool Released;

    [DataField]
    public EntProtoId Effect = "EffectNullstarUnbound";
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class NullstarUnboundWaveComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Radius = 7f;

    [DataField, AutoNetworkedField]
    public float ExpansionSeconds = 0.65f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan StartTime;

    [DataField, AutoNetworkedField]
    public EntityUid? Caster;

    // Server-only sweep state: a mob is affected at most once by the advancing ring.
    public float LastRadius;
    public readonly HashSet<EntityUid> HitEntities = new();
}

public sealed partial class NullstarUnboundEvent : InstantActionEvent;
