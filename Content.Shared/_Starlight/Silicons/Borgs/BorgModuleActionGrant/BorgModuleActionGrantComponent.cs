// 🌟Starlight🌟
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Starlight.Silicons.Borgs.BorgModuleActionGrant;

/// <summary>
/// Placed on a borg module entity. Grants the listed action prototypes to the chassis
/// when the module is installed and revokes them when uninstalled.
/// Entities stored in <see cref="ActionEntities"/> are managed by
/// <see cref="BorgModuleActionGrantSystem"/> and should not be set in YAML.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BorgModuleActionGrantComponent : Component
{
    /// <summary>Action prototype IDs to grant on install.</summary>
    [DataField(required: true)]
    public List<EntProtoId> Actions = new();

    /// <summary>Spawned action entity UIDs; populated at runtime.</summary>
    [DataField, AutoNetworkedField]
    public List<EntityUid> ActionEntities = new();
}
