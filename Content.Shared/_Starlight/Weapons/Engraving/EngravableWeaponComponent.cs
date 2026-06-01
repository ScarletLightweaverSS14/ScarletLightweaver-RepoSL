using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.Weapons.Engraving;

/// <summary>
///     Allows a weapon to be engraved with a nickname that gets appended to its name as:
///     "Weapon Name 'nickname'"
///     A welder can then fill the engraving channel with solder to remove the engraving, allowing it to be re-engraved.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class EngravableWeaponComponent : Component
{
    /// <summary>
    ///     The original name of the weapon before any engraving was applied.
    ///     Set automatically on MapInit from the entity's current name.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string BaseName = string.Empty;

    /// <summary>
    ///     The nickname currently engraved on the weapon. Empty means no engraving.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string Nickname = string.Empty;

    /// <summary>
    ///     Time in seconds for the welder DoAfter when removing an engraving with solder.
    /// </summary>
    [DataField]
    public float SolderTime = 3.0f;
}

[Serializable, NetSerializable]
public sealed partial class EngravingRemoveWithSolderDoAfterEvent : SimpleDoAfterEvent { }
