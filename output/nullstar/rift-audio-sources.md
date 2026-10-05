# Rift Cleave audio sources

Downloaded 2026-10-04 for the charged dash revision.

## New recorded source

- Author: **artisticdude**.
- Work: [Swishes Sound Pack](https://opengameart.org/content/swishes-sound-pack).
- License shown on the author's upload: **[CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/)**. Free to use and modify, including commercially; no account or purchase required for this download.
- Original archive: https://opengameart.org/sites/default/files/swishes.zip
- Local original: `source/audio/swishes.zip`. Only its named `swish-N.wav` audio entries were extracted; no executable content was used.
- Source format: 44.1 kHz, stereo, 24-bit PCM WAV.

## Mix recipes

`Build-Audio.ps1` decodes the original PCM chunks, downmixes to mono, trims silence, pitch-shifts, filters, layers, synthesizes a short falling pressure transient and adds damped reflections. All finished files are 44.1 kHz, mono, 16-bit PCM WAV with faded endpoints and at least 3 dB peak headroom.

| Cue | Recorded layers | Treatment |
| --- | --- | --- |
| `rift-charge.wav` — 0.8 s | CC0 swish-9; repository glass_crack1 | Reverse low swish, rising pressure, brief reversed crystal accent. |
| `rift-dash.wav` — 0.36 s | CC0 swish-7 and swish-2 | Short forward air cut with an inward undertone. CC0 derivative. |
| `rift-cleave.wav` — 2.4 s | CC0 swish-9, swish-7, swish-5; repository glass_crack2 | Lowered heavy swishes, controlled saturation, sharp crystal accent, brief falling pressure, filtered spatial reflections. |

The repository crystal recordings originate from **Baystation 12**, revision `23c0d851246ebbaeb0df647318ce9874da895d3d`, under **CC-BY-SA-3.0**. Their provenance is retained in `Resources/Audio/Effects/attributions.yml`; upstream: https://github.com/Baystation12/Baystation12. The two combined derivatives retain CC-BY-SA-3.0, with full attribution in the runtime Nullstar audio folder. The pure swish dash cue is CC0.

Playback trim: charge -3 dB; dash 0 dB; cut +1 dB with small pitch variation. The cut is deliberately more prominent than ordinary swings; it remains positional. Full mix loudness and subjective character still require in-game listening.

The existing seven basic weapon sounds and the approved 8.5-second `unbound.wav` are preserved byte-for-byte. `rift-existing-audio-hashes.json` records those checksums.
