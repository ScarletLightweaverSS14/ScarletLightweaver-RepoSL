using Robust.Shared.Audio;

namespace Content.Shared._Starlight.TowerDefense;

/// <summary>Short positional cues for arena events; null disables an individual cue.</summary>
[DataDefinition]
public sealed partial class TowerDefenseArenaSounds
{
    [DataField] public SoundSpecifier? WaveStart = new SoundPathSpecifier("/Audio/Effects/Arcade/newgame.ogg")
        { Params = AudioParams.Default.WithVolume(-8) };
    [DataField] public SoundSpecifier? WaveCleared = new SoundPathSpecifier("/Audio/Machines/chime.ogg")
        { Params = AudioParams.Default.WithVolume(-8) };
    [DataField] public SoundSpecifier? BaseHit = new SoundPathSpecifier("/Audio/Machines/warning_buzzer.ogg")
        { Params = AudioParams.Default.WithVolume(-8) };
    [DataField] public SoundSpecifier? Victory = new SoundPathSpecifier("/Audio/Effects/Arcade/win.ogg")
        { Params = AudioParams.Default.WithVolume(-6) };
    [DataField] public SoundSpecifier? Defeat = new SoundPathSpecifier("/Audio/Effects/Arcade/gameover.ogg")
        { Params = AudioParams.Default.WithVolume(-6) };
    [DataField] public SoundSpecifier? BossArrival = new SoundPathSpecifier("/Audio/Animals/space_dragon_roar.ogg")
        { Params = AudioParams.Default.WithVolume(-6) };
    [DataField] public SoundSpecifier? Purchase = new SoundPathSpecifier("/Audio/Machines/high_tech_confirm.ogg")
        { Params = AudioParams.Default.WithVolume(-12) };
    [DataField] public SoundSpecifier? Upgrade = new SoundPathSpecifier("/Audio/Effects/Arcade/player_charge.ogg")
        { Params = AudioParams.Default.WithVolume(-10) };
}
