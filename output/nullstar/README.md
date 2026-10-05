# Nullstar — stage 4: Rift Cleave

Nullstar is a separate weapon from the Hadal Blade. Static sprites, basic melee/audio, the standalone Unbound release, and Rift Cleave are implemented. The remaining abilities, passive blade animation and encounter setup follow the stages in **CONCEPT.md**.

Restart with the updated resources and spawn:

```text
self spawn:in "belt" ScarletNullstar
```

Move it into your hand and use the normal wield control (Z by default). Both hands are required for melee. The weapon deals **32 slash + 8 piercing** before resistances, with **1.33 seconds between attacks**, **1.65-tile edge reach** and a **70-degree wide attack**. Single-target and wide attacks use the same base damage. There is no added armor bypass, stun or lingering hazard. These are a starting balance for the later encounter, not a finished boss balance.

Click attacks use the existing blade thrust animation; wide attacks alternate sweeping directions. `wideAnimationRotation: 180` correctly faces the upright sword outward, and `animationOffset: 0.8` keeps its visual reach close to the hit range. The blade remains a physical weapon without activation or energy-sword sounds.

## Rift Cleave — charged dash revision

Rebuild client and server, spawn `ScarletNullstar`, hold it in both hands, activate **Rift Cleave**, then click where you want the cut to land. `ScarletNullstarUnboundPreview` inherits the move as well.

| Phase | Implemented behavior |
| --- | --- |
| Charge | 0.8-second visible DoAfter. A core contracts at the wielder, a faint wake marks the direction, and a 4.4 × 1.6-tile warning marks the projected cut. Turning does not retarget it. Movement beyond 0.1 tile, damage, hand changes, losing the two-handed grip or deleting the sword cancels it. |
| Dash | Physical movement at 18 tiles/second, up to 3.5 tiles. Stops 0.9 tile short of the cursor to put the blade on the aimed point. Close targets get a short dash or a stationary cut. Ordinary movement cannot steer the rush. Body-shaped sweeps and normal collision solving stop at walls/doors, including obstacles appearing after charging. |
| Arrival | Immediately deals 50 slash + 10 piercing in a 4.4 × 1.6-tile lane 0.9 tile in front of the actual stop. A 0.24-second crescent sweep, brief fracture flash and louder spatial swoosh share the release tick. Armor/resistances apply. There is no additional windup on arrival. |
| Residual hazard | 2.5 seconds, 4.4 × 0.5 tiles. 3 slash + 2 piercing per permitted hit, with at least 0.75 seconds between hits per creature. Each target can be pushed sideways once; anchored and buckled targets are not pushed. |
| Closure | 0.4 seconds of narrowing/fading after damage stops. Wake fades in 0.35 seconds. Effects are bounded and despawn automatically. |
| Recovery/cooldown | Ordinary melee blocked through charge/dash and for 0.4 seconds after the cut. Eight-second blade-owned cooldown starts on cast, including interruptions. Dropping/passing the sword does not reset it. |

Walls and closed doors also block damage; the caster, ghosts and contained mobs are excluded. Decorative effects can overlap obstacles, but damage cannot pass through them. Becoming unable to wield/interact/attack, losing the blade or entering a container during the dash cancels the cut and releases the movement lock. Pulling is released through the normal pulling API. The move does not grant invulnerability, break structures or replace terrain. These remain initial encounter balance values.

Open **rift-preview.html** for the updated offline animation, sound, cardinal directions, scrubber and an illustrative obstructed rush. It uses shipped art/timing; it is not a gameplay recording. `cleave-layers.png` shows the additional core, directional wake and crescent art at 4× zoom. All eight earlier sounds, including approved Unbound, remain byte-identical.

The new swoosh uses freely reusable CC0 recordings downloaded from artisticdude’s **Swishes Sound Pack**, with pressure synthesis and a small attributed crystal accent. See **rift-audio-sources.md** for licenses and the original source link. `Build-Audio.ps1` reproduces all eleven cues. The cut plays at +1 dB, compared with the old -4 dB trim, while the WAV retains headroom.

Art lives in `NullstarRift.rsi` (three 128×32 fracture layers) and `NullstarCleave.rsi` (three 128×64 motion layers). Original generated sources, prompts and deterministic palette/RSI conversion scripts are retained here. No continual particle spawning is used for this move.

**Next separate stage requested:** Hadal Blade hold-right-click charging with charge UI and severe slowdown; release into two short advancing slashes, ending in a ground stomp. This revision handles Nullstar first. Callouts and the remaining boss abilities/arena remain later work.

## Nullstar Unbound preview

After rebuilding the client and server, spawn the separate preview variant:

```text
self spawn:in "belt" ScarletNullstarUnboundPreview
```

Hold it in both hands and activate **Nullstar Unbound**. The preview uses a visible one-second DoAfter that cancels on movement, damage or hand changes. Each blade releases once; dropping or passing it does not reset it. Spawn a fresh preview sword to repeat.

The expanding ring reaches **7 tiles in 0.65 seconds**, applying one outward throw per unobstructed mob, configured for approximately **4 tiles** of displacement on normal ground. Walls block the knockback. The wielder, contained mobs, ghosts, anchored entities and buckled mobs are excluded. No direct damage or stun is applied, but ordinary environmental and collision consequences still apply. Crystal debris is decorative; loose items are not launched. The decorative ring itself may be visible through walls.

Four original 128x128 effect layers show an expanding pressure ring, scattering amethyst debris, a brief core flare, and a rift that narrows and fades. One effect entity lasts three seconds. Networked start time aligns the client animation with server wave expansion; entering visibility late does not restart the effect. An 8.5-second mono cue layers a heavy explosion body, distant blast rumble, stone fracture, restrained crystal accents and descending pressure with slight pitch variation. The sound plays at the release position independently of the three-second visual entity, allowing its full tail to finish.

Open **unbound-preview.html** for a standalone animation/audio preview with a scrubber. This is an offline illustration using the shipped assets and timing, not a gameplay recording.

The ordinary `ScarletNullstar` grants no preview action. Its server-side `NullstarUnboundSystem.TryRelease` entry point is ready for later extraction completion after equipping/wielding the blade. This stage does not create a stone, arena, Scarlet transformation, boss bar, survival immunity, announcement or alert change.

Compare the revised and previous release in **unbound-audio-comparison.html**. **audio-search.md** lists verified CC0 alternatives and search phrases.

## Basic audio

Open **audio-preview.html** to audition all eleven clips offline, including random swings and an example landed strike. The clips combine pitch-shifted sword/stone recordings with filtered or reversed crystal accents and a short synthesized resonance. There are two swing variants, two impact variants, a glance fallback, and separate wield/unwield sounds. Playback has restrained pitch variation; there is no idle hum.

All clips are mono, 44.1 kHz, 16-bit PCM WAV. Basic combat clips are 0.45–0.72 seconds; Unbound is 8.5 seconds. Robust supports WAV directly, and pre-mixing the layers keeps each cue to one audio source. Audio lives in `Resources/Audio/_Starlight/Weapons/Nullstar/`, with CC-BY-SA-3.0 and CC0 source attribution in `attributions.yml`. The recipes are in `Build-Audio.ps1`, which reuses the game's VorbisPizza decoder from the local client build.

Existing target material sounds retain their normal precedence over weapon impact sounds. The glance cue is configured through `soundNoDamage`; the current shared sound system can choose a target's sound or the weapon's hit cue first. This stage does not change audio behavior for other weapons.

## Deliverables

- Loaded prototype: `Resources/Prototypes/_Starlight/Admeme/scarlet/nullstar.yml`.
- `Resources/Textures/_Starlight/Admeme/Scarlet/Nullstar.rsi`: static 32 x 64 item icon, with the weapon approximately 50 pixels tall.
- `NullstarInhands.rsi`: 64 x 64 frames for both hands, normal/wielded aliases, South/North/East/West directions.
- `NullstarBelt.rsi`: 32 x 32 belt overlays in all four directions.
- **contact-sheet.png**: exported native sprites and an equal-zoom Hadal comparison.
- **CONCEPT.md**: the complete encounter direction, all eight ability concepts, staged implementation order, extraction/arena constraints, confirmed repository entry points and a draft server-wide release announcement.
- **audio-preview.html**: standalone audition page with waveforms and game-volume trims.
- **nullstar-stage4.zip**: current portable package with repository-relative paths. The earlier stage ZIPs are retained as snapshots.

## Art and reproducibility

Built-in OpenAI image generation produced the sword artwork. `source/nullstar-native.png` is the final export source; the earlier source images retain the concept iterations. The exact initial, proportion-revision and native-sprite prompts are saved in the three `*prompt.txt` files here.

The exporter performs native-resolution sampling, palette conversion, directional placement and RSI packing. It uses coverage sampling to avoid losing thin edges during reduction, then writes at most 16 opaque colors with binary alpha and transparent margins. No existing cosmic-cult weapon pixels were copied. The new art uses the repository's default CC-BY-SA-3.0 non-code license, recorded with provenance in each RSI metadata file.

```powershell
./output/nullstar/Export-Sprites.ps1
./output/nullstar/Export-Unbound.ps1
./output/nullstar/Validate-Sprites.ps1
./output/nullstar/Build-Preview.ps1
./output/nullstar/Build-Audio.ps1
./output/nullstar/Validate-Audio.ps1
./output/nullstar/Build-AudioPreview.ps1
./output/nullstar/Build-UnboundPreview.ps1
./output/nullstar/Build-UnboundComparison.ps1
./output/nullstar/Package.ps1
```

./output/nullstar/Export-Rift.ps1, ./output/nullstar/Export-Cleave.ps1 and ./output/nullstar/Build-RiftPreview.ps1 reproduce the new visual assets and preview.

## Validation

`validation.txt` covers six RSI folders, sixteen states and 31 packed frames. `audio-validation.txt` covers all eleven WAVs: headers, mono format, duration, peak/RMS levels, DC offset and faded endpoints. Build and engine integration results are recorded separately in `unbound-verification.txt`.

`NullstarUnboundTest.cs` is an archived temporary check, outside the test project. It checks the preview/normal separation, wield requirement, outward impulse, wall obstruction, range, no direct damage, caster exclusion and one-use persistence. No runtime test systems were added.

Live visual alignment, spatial audio balance, preview DoAfter interruption and behavior on real map geometry still need an in-game check.

Rift checks and limitations are recorded in **rift-verification.txt**; the temporary source fixture is **NullstarRiftCleaveTest.cs**. Next requested separate stage: **Hadal Blade charged combo**; Nullstar Pulse follows in its ability sequence. The 90-second seal interaction, survival changes, arena clearing/atmosphere rebuild and lighting-preserving Theta-style announcement remain later integration work.





