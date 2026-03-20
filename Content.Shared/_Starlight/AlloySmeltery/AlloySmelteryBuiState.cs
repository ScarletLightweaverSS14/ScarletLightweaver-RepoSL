using Robust.Shared.Maths;
using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.AlloySmeltery;

[Serializable, NetSerializable]
public enum AlloySmelteryUiKey : byte
{
    Key,
}

/// <summary>
/// A single material entry for the alloy smeltery material gauge UI.
/// </summary>
[Serializable, NetSerializable]
public sealed class AlloySmelteryMaterialEntry
{
    public string MaterialId;
    public string LocalizedName;
    public int Amount;
    public Color Color;

    public AlloySmelteryMaterialEntry(string materialId, string localizedName, int amount, Color color)
    {
        MaterialId = materialId;
        LocalizedName = localizedName;
        Amount = amount;
        Color = color;
    }
}

/// <summary>
/// BUI state sent to the client containing current material inventory of the alloy smeltery.
/// </summary>
[Serializable, NetSerializable]
public sealed class AlloySmelteryBuiState : BoundUserInterfaceState
{
    public List<AlloySmelteryMaterialEntry> Materials;

    /// <summary>
    /// Display capacity used for bar chart scale (volume units).
    /// </summary>
    public int DisplayCapacity;

    public AlloySmelteryBuiState(List<AlloySmelteryMaterialEntry> materials, int displayCapacity)
    {
        Materials = materials;
        DisplayCapacity = displayCapacity;
    }
}
