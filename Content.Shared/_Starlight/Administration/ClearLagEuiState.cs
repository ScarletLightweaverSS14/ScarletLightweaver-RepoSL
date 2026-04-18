using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.Administration;

/// <summary>
/// Categories of entities that the Clear Lag tool can remove.
/// </summary>
public enum ClearLagCategory
{
    /// <summary>Entities tagged as "Trash" or otherwise identified as litter.</summary>
    Trash,
    /// <summary>Fired/spent bullet casings (CartridgeAmmoComponent.Spent == true).</summary>
    SpentCasings,
    /// <summary>SKB/Soviet armaments crates (CrateSovietArmaments). Contents are ejected before deletion.</summary>
    SovietArmsCrates,
}

[Serializable, NetSerializable]
public sealed class ClearLagEuiState : EuiStateBase
{
    public int TrashCount;
    public int SpentCasingsCount;
    public int SovietArmsCratesCount;
}

public static class ClearLagEuiMsg
{
    /// <summary>Sent by the client to clear all entities in the given category.</summary>
    [Serializable, NetSerializable]
    public sealed class DoClear : EuiMessageBase
    {
        public ClearLagCategory Category;
    }

    /// <summary>Sent by the client to request a fresh count of each category.</summary>
    [Serializable, NetSerializable]
    public sealed class RequestRefresh : EuiMessageBase { }
}
