using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Starlight.AlloySmeltery;

/// <summary>
/// Marks an entity as freshly smelted from the alloy smeltery.
/// While this component is present, the entity is dangerously hot and will
/// deal burn damage to anyone holding or touching it.
/// The component is removed once the entity has been cooled sufficiently
/// (via gas atmosphere, water, or simply time).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class HotAlloyComponent : Component
{
    /// <summary>
    /// The current temperature of the alloy in Kelvin.
    /// Starts very hot (around 1500 K), and cools towards room temperature (293 K).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Temperature = 1500f;

    /// <summary>
    /// The temperature below which the alloy is considered safe to hold (no burn damage).
    /// Approximately 50°C (323 K).
    /// </summary>
    [DataField]
    public float SafeTemperature = 323f;

    /// <summary>
    /// Passive cooling rate in Kelvin per second when in normal atmosphere.
    /// Accelerated by cold gas or water contact.
    /// </summary>
    [DataField]
    public float CoolingRatePerSecond = 8f;

    /// <summary>
    /// Next time the cooling tick fires.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextCoolTick = TimeSpan.Zero;
}
