using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Starlight.Weapons.AmmoCycler;

/// <summary>
/// Allows a borg-held weapon to cycle between a fixed list of ammo prototypes
/// via a right-click verb. Each cycle flushes the current virtual ammo and
/// replaces it with the next type at full capacity.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BorgAmmoCyclerComponent : Component
{
    /// <summary>
    /// Ordered list of ammo prototypes to cycle through.
    /// </summary>
    [DataField(required: true)]
    public List<EntProtoId> Protos = new();

    /// <summary>
    /// Index into <see cref="Protos"/> currently loaded.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int CurrentIndex = 0;
}
