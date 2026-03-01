using Robust.Shared.Prototypes;

namespace Content.Shared._Starlight.Weapons.AmmoCycler;

/// <summary>
/// Raised on a gun entity by <see cref="BorgAmmoCyclerSystem"/> to request
/// that <see cref="Content.Shared.Weapons.Ranged.Systems.SharedGunSystem"/> flush
/// the current ballistic ammo and reload with a new prototype.
/// Handled inside SharedGunSystem to satisfy its access restrictions.
/// </summary>
public sealed class BorgAmmoSwapEvent : EntityEventArgs
{
    /// <summary>The new ammo prototype to load at full capacity.</summary>
    public EntProtoId NewProto;

    public BorgAmmoSwapEvent(EntProtoId newProto) => NewProto = newProto;
}
