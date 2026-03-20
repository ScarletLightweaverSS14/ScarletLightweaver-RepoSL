using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.AlloySmeltery;

/// <summary>
/// Marker component for the <see cref="AlloySmelteryOutputComponent"/> on the Alloy Smeltery machine.
/// This tells the <see cref="AlloySmelteryOutputSystem"/> to add a <see cref="HotAlloyComponent"/>
/// to every item that the lathe on this entity produces.
/// Also drives the alloy smeltery material gauge BUI.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AlloySmelteryOutputComponent : Component
{
    /// <summary>
    /// Display capacity (in volume units) used for the material gauge bar chart.
    /// 1 metal sheet = 100 volume units. Default: 1000 (10 sheets per material).
    /// </summary>
    [DataField]
    public int DisplayCapacity = 1000;
}
