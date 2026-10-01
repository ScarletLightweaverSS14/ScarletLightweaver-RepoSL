using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.Voidwalker;

/// <summary>Grants Ground Spikes; eruption tuning lives on the fault line prototype.</summary>
[RegisterComponent]
public sealed partial class CrystalGroundSpikesComponent : Component
{
    [DataField]
    public EntProtoId Action = "ActionVoidwalkerGroundSpikes";

    [DataField]
    public EntProtoId FaultLinePrototype = "VoidwalkerCrystalFaultLine";

    [DataField]
    public EntProtoId? StompEffect = "VoidwalkerCrystalFlash";

    [DataField]
    public SoundSpecifier StompSound = new SoundPathSpecifier("/Audio/Effects/Footsteps/largethud.ogg");

    [DataField]
    public LocId Callout = "voidwalker-ground-spikes-callout";

    [DataField]
    public EntityUid? ActionEntity;

    public EntityUid? ActiveFaultLine;
}
