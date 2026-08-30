using Content.Shared.Actions;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._Starlight.Dragon;

[RegisterComponent]
public sealed partial class DragonRoarComponent : Component
{
    [DataField]
    public float Range = 8f;

    [DataField]
    public TimeSpan StatusDuration = TimeSpan.FromSeconds(15);

    [DataField]
    public EntProtoId PredatorHuntEffect = "StatusEffectPredatorHunt";

    [DataField]
    public SoundSpecifier RoarSound = new SoundPathSpecifier("/Audio/Animals/space_dragon_roar.ogg");
}

public sealed partial class DragonRoarEvent : InstantActionEvent;
