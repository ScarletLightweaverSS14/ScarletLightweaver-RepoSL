using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.Voidwalker;

[RegisterComponent]
public sealed partial class CrystalBeamAbilityComponent : Component
{
    [DataField] public EntProtoId Action = "ActionVoidwalkerCrystalBeam";
    [DataField] public EntProtoId ChannelPrototype = "VoidwalkerCrystalBeam";
    [DataField] public EntityUid? ActionEntity;
    public EntityUid? Channel;
}
