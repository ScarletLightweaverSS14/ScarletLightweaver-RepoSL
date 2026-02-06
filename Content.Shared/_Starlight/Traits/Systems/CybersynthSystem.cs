using Content.Shared._Starlight.Traits.Components;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Humanoid;
using Content.Shared.Speech;
using Content.Shared.Speech.Components;
using Content.Shared.Chat.TypingIndicator;
using Robust.Shared.Prototypes;
using Robust.Shared.Log;

namespace Content.Shared._Starlight.Traits.Systems;

/// <summary>
/// Starlight: System for the Cybersynth trait.
/// Modifies the Speech and Vocal components to use robotic speech patterns, sounds, and emotes.
/// </summary>
public sealed class CybersynthSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        
        // Starlight: Listen for when Cybersynth component is added 
        SubscribeLocalEvent<CybersynthComponent, ComponentInit>(OnCybersynthInit);
    }
    
    /// <summary>
    /// Starlight: When the Cybersynth component initializes, modify the Speech and Vocal components.
    /// Sets robotic speech verb, borg sound effects, and silicon vocal emotes.
    /// </summary>
    private void OnCybersynthInit(EntityUid uid, CybersynthComponent component, ComponentInit args)
    {
        Log.Debug($"Cybersynth component initializing on {ToPrettyString(uid)}");
        
        // Starlight: Get or ensure the Speech component exists
        var speech = EnsureComp<SpeechComponent>(uid);
        ModifySpeech(uid, speech);
    }
    
    private void ModifySpeech(EntityUid uid, SpeechComponent speech)
    {
        Log.Debug($"Modifying speech for {ToPrettyString(uid)}");
        
        // Starlight: Set the speech verb to Robotic for borg-like speech patterns
        speech.SpeechVerb = new ProtoId<SpeechVerbPrototype>("Robotic");
        
        // Starlight: Set the speech sounds to Borg for beep/boop sound effects
        speech.SpeechSounds = new ProtoId<SpeechSoundsPrototype>("Borg");
        
        Log.Debug($"Set speech verb to {speech.SpeechVerb} and sounds to {speech.SpeechSounds}");
        
        // Starlight: Mark the component as dirty to network the change
        Dirty(uid, speech);
        
        // Starlight: Get or ensure the Vocal component for emotes (screams, beeps, etc.)
        var vocal = EnsureComp<VocalComponent>(uid);
        
        Log.Debug($"Vocal component before modification - Sounds: {vocal.Sounds?.Count ?? 0}, WilhelmProb: {vocal.WilhelmProbability}");
        
        // Starlight: Set vocal sounds to UnisexSilicon for borg-like emotes (screams, beeps, chimes)
        vocal.Sounds = new Dictionary<Sex, ProtoId<EmoteSoundsPrototype>>
        {
            { Sex.Unsexed, new ProtoId<EmoteSoundsPrototype>("UnisexSilicon") },
            { Sex.Male, new ProtoId<EmoteSoundsPrototype>("UnisexSilicon") },
            { Sex.Female, new ProtoId<EmoteSoundsPrototype>("UnisexSilicon") }
        };
        
        // Starlight: Disable wilhelm scream for silicon entities
        vocal.WilhelmProbability = 0;
        
        // Starlight: Mark the vocal component as dirty to network the change
        Dirty(uid, vocal);
        
        Log.Debug($"Vocal component after modification - Sounds: {vocal.Sounds?.Count ?? 0} entries set to UnisexSilicon");
        Log.Debug($"Cybersynth trait fully applied to {ToPrettyString(uid)}");
    }
}
