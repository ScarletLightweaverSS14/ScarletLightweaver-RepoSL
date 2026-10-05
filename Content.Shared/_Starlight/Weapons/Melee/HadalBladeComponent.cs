using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Starlight.Weapons.Melee;

/// <summary>
/// Grants a pressure-wave action while the blade is held. The action requires wielding.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(HadalBladeSystem)), AutoGenerateComponentState]
public sealed partial class HadalBladeComponent : Component
{
    [DataField]
    public EntProtoId<WorldTargetActionComponent> Action = "ActionHadalCrush";

    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;

    [DataField]
    public EntProtoId Projectile = "ProjectileHadalPressure";

    [DataField]
    public int ProjectileCount = 3;

    /// <summary>Total angle covered by the fan, in degrees.</summary>
    [DataField]
    public float Spread = 36f;

    [DataField]
    public float ProjectileSpeed = 18f;
}

public sealed partial class HadalCrushEvent : WorldTargetActionEvent;
