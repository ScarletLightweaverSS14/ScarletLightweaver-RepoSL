# Nullstar — Scarlet's sealed star

## Identity and current stage

Nullstar is an ancient bastard sword / greatsword hybrid associated with Scarlet and the Void Tree. Its blade looks formed in sacred black stone and torn free, with a quiet point of distant light imprisoned inside an amethyst inclusion. It has a clean taper, a restrained root-like guard and a practical two-handed grip. It is smaller and more elegant than the Hadal Blade. Its cosmic influence comes from fractures, unnatural geometry and resonance; it does not copy cosmic-cult sprites or attacks.

**Completed here:** static item, both-hand/four-direction and belt sprites; the separate spawnable `ScarletNullstar` weapon; and its basic combat/audio pass. Baseline damage is 32 slash + 8 piercing, with a 1.33-second attack interval, 1.65-tile edge reach and a 70-degree wide attack. Both hands are required. Click attacks thrust; wide attacks alternate blade sweeps, with rotation corrected for the upright artwork. Seven short custom audio cues provide swing/impact variants, a configured glance fallback and wield/unwield sounds. The standalone Nullstar Unbound release now adds a seven-tile expanding wave, one outward throw per visible mob, custom four-layer effects and a layered release cue. A one-use preview variant exposes it separately. Rift Cleave now charges for 0.8 seconds, physically dashes up to 3.5 tiles at 18 tiles/second, and immediately cuts at the actual stopping point for 60 base damage. Custom converging-core, directional-wake and crescent layers accompany a louder CC0-based swoosh. A 2.5-second lighter fracture remains, with one displacement per creature. Remaining boss abilities and encounter balance are later work.

## Development order

1. Sword sprites and spawnable visual review — complete.
2. Basic melee damage, timing, attack presentation and distinct audio — complete.
3. Nullstar Unbound — implemented as an isolated release effect; encounter attachment remains later.
4. Rift Cleave — charged dash revision implemented and verified.
5. Nullstar Pulse.
6. Starfall Fracture.
7. Gravitic Drag.
8. Voidstep.
9. Severance.
10. Abyssal Wake.
11. Animation and audiovisual polish: core heartbeat, ability anticipation, closing rifts, debris, distortion and layered sound. Earlier ability stages still need visible functional telegraphs.
12. Sword-in-stone structure, arena generation, extraction, boss conversion, boss bar and server announcement. Integrate and verify the whole encounter only after the individual stages work.

Finish and verify each stage before developing the next. The arena and activation logic must not be introduced by spawning the ordinary review sword.

## Combat and ability concepts

The table below retains the encounter design targets. Implemented Rift Cleave values are documented in README.md; the other future abilities remain proposals awaiting implementation and balance. Visible warnings must match the actual damaging area. Knockback and pulling respect collisions. A shared casting/recovery rule should prevent stacking control abilities into unavoidable Severance hits.

| Ability | Tell and effect | Gameplay starting point | Audio character |
| --- | --- | --- | --- |
| Nullstar Unbound | The trapped star flashes; the seal splits outward; a large expanding violet-black ring pushes dust and cold mist away. A thin rift remains at the socket. | Intro only, once per successful extraction. Strong outward displacement with little or no damage. Establish room to fight, not an opening instant kill. | Stone fracture, low pressure burst, crystal splitting, a brief vacuum gap and a ringing tail. |
| Rift Cleave | Core contracts, a directional wake follows a physical dash, then a broad crescent cuts immediately at the stopping point. The fracture narrows closed. | 0.8 s charge, up to 3.5-tile dash at 18 tiles/s, 60 base arrival damage, 2.5 s lighter residual hazard with one sideways push per creature. 8 s cooldown. | Brief rushing swish, loud layered heavy cut, pressure transient and damped spatial reflections. |
| Nullstar Pulse | Light gathers inward, veins brighten in sequence, then an expanding circular wave releases. | About 1.5 s warning, moderate damage and strong outward knockback over roughly 6 tiles. One hit per target per cast. About 18 s cooldown. | Rising bass and heartbeat, then a broad impact and resonant tail. |
| Starfall Fracture | Ground strike sends branching cracks outward. Each branch lights before crystals erupt. | Roughly 1.2 s crack warning. Pierce/slash eruptions with a brief stagger or displacement. Leave traversable gaps; use a bounded pattern rather than filling every tile. About 22 s cooldown. | Stone impact, splitting cracks, staggered crystal bursts with pitch variation. |
| Gravitic Drag | The core dims; dust and small shards bend inward; a restrained circular distortion forms. | Telegraph before a 2 s gradual pull within roughly 6 tiles. No direct damage. No teleporting, wall crossing or unavoidable follow-up. About 20 s cooldown. | Inward suction, low wobble, a final short pressure snap. |
| Voidstep | Light contracts into a short tear; a dark streak traces the movement; the arrival flares. | Short warned relocation, roughly 5 tiles, with valid destination and obstruction checks. About 12 s cooldown. Start without arrival damage; consider a small slash only after testing. | Compressed snap, faint void whisper and short arrival accent. |
| Severance | A long conspicuous charge illuminates the whole edge, followed by a downward/diagonal cut and a linear wound through space. | About 2.5 s warning. Very high direct damage; lower delayed damage along the line. Clear escape space, recovery after use, roughly 30 s cooldown. Initial version does not permanently destroy the arena. | Long crystal vibration, heavy descent, violent tear, deep ringing aftershock. |
| Abyssal Wake | A deliberate movement/drag leaves cracked crystal-stone behind. Fragments briefly hover and then collapse inward. | A short-lived trail that lightly damages or slows pursuers. Hazards expire after roughly 4 s; no permanent terrain replacement. About 24 s cooldown. | Scraping stone, crystal clicks, pressure hiss, soft collapse. |

Damage, timing, range and cooldowns should be prototype data where existing systems support it. Use standard SS14 damage, movement, collision, actions and DoAfter APIs; avoid bespoke parallel combat systems.

## Later animation and sound

The passive core uses an irregular, slow heartbeat with light moving inside existing veins. A tiny star becomes clearer at stronger pulses. Condensation and drifting particles are occasional and bounded; the blade itself never shifts shape between idle frames. Ability anticipation should be stronger than the idle pulse. Favor weapon layers and effect entities over custom character animations.

Build a small sound vocabulary from existing stone, crystal, pressure and spatial effects. Different attacks get different envelopes and accents. Limit concurrent layered voices, apply restrained pitch variation, and reserve the largest low-frequency impact for Unbound and Severance. Any reversed or stretched files must retain source license and attribution. Existing cosmic-cult sound language is inspiration, not a reason to copy its unique attacks.

## Arena and extraction contract — later stage

- Spawn the sealed sword structure first. It generates an approximately 100 x 100 tile arena centered on the seal. Spawning it does not turn anyone into Scarlet.
- Clear obstructing content within the exact footprint: terrain, structures, objects and decals, using the appropriate systems. Preserve the seal, required encounter entities and intended boss player; protect connected players and their contained inventory from blanket deletion. Handle overlapping grids and contained entities deliberately.
- Replace the footprint with dark/violet terrain, purple grass, sparse crystal clusters, twisted purple-leaved trees, ancient roots, broken rings and restrained ruins. Keep broad combat lanes and open central space. Sacred radial patterns focus attention on the seal without covering hazard telegraphs.
- Generate in bounded batches to avoid a single 10,000-tile server hitch. Clean up prior encounter-owned temporary entities on teardown.
- The player interacts with the sealed sword to start a visible **90-second DoAfter**. Moving out of reach, cancellation, damage/interruption, losing interaction ability, deletion of the seal or a competing successful pull invalidates the attempt. No partial progress carries over. Only one claimant can succeed.
- During the attempt, a few scheduled stages increase cracks, violet light, dust and low resonance. Cap emitted particles and sounds instead of attaching heavy per-frame work to every tile.
- Revalidate the claimant and required hands/equipment space on completion. Then remove the sealed blade, equip Nullstar, prepare that player as Scarlet, trigger Unbound, grant abilities and show the boss bar. Guard against duplicate completion and replayed intro effects.
- The boss bar and survival changes belong to the transformed player, not everyone in the arena. Define cleanup for death, deletion and encounter reset, and how an interrupted transformation rolls back.

### Confirmed repository entry points

`Content.Server/_Starlight/Atmos/AtmosCommand.cs` implements `FixGridAtmos` through `AtmosphereSystem.RebuildGridAtmosphere((uid, atmos, grid))`, after obtaining `GridAtmosphereComponent` and `MapGridComponent`. Arena code should use that underlying system behavior after tile replacement on every affected grid, rather than issuing a console command. A topology rebuild alone must not be assumed to supply a breathable gas mixture: atmospheric initialization needs its own explicit check.

Actual survival components present here include `RespiratorComponent` (file under `Content.Server/_Starlight/Medical/Body/Components`, namespace `Content.Server.Body.Components`), `BarotraumaComponent` and `TemperatureDamageComponent`. Trace their systems and status cleanup during the conversion stage before removing/disabling them on the boss. Also inspect temperature-driven movement/survival effects. Do not indiscriminately remove `TemperatureComponent`, body organs, health, damage, movement or interaction components. Environmental immunity must not accidentally become immunity to ordinary combat cold/heat damage.

## Alert and announcement concept — later stage

The existing `theta` alert in `Resources/Prototypes/AlertLevels/alert_levels.yml` sets `dimmedLightMultiplier: 0.2` and forces emergency lights. Calling it unchanged would violate the requested lighting behavior. Plan an encounter-specific alert configuration or scoped handling that preserves normal lighting, instead of globally changing Theta for unrelated events. Treat the Theta connection as optional until that stage; the encounter can still send its own announcement.

Send the release announcement server-wide exactly once, after successful extraction and conversion. Do not announce a boss on spawn, failed attempts, or retries. Use the existing announcement routing, with a custom localized title/body and a restrained resonance cue.

**Title:** NULLSTAR — THE SEAL IS BROKEN

**Draft announcement:**

> An old silence has ended.
>
> Beneath the Void Tree, a sealed star has answered a hand. Nullstar is free. Scarlet bears it.
>
> Keep your distance from the violet fractures. What was held beneath the roots is no longer contained.

Optional preceding alert line: “Theta-level anomaly confirmed. The lights will remain. Their reach may not.” No blackout or global inversion is required. Any later distortion must be brief, localized and preserve readable combat telegraphs.

## Current files and verification

`Resources/Prototypes/_Starlight/Admeme/scarlet/nullstar.yml` is the loaded prototype. Static sprites are in `Nullstar.rsi`, `NullstarInhands.rsi` and `NullstarBelt.rsi` under `Resources/Textures/_Starlight/Admeme/Scarlet/`.

Generate/export via the built-in imagegen tool, then `Export-Sprites.ps1`. Prompts and source images are retained here. `Validate-Sprites.ps1` checks RSI schema, frame counts, palette, alpha and margins. `Build-Preview.ps1` makes the native-scale contact sheet, including a same-zoom Hadal comparison.

Unbound is implemented in isolation through the preview sword; its stone-breaking context and extraction trigger remain later. The abilities after Rift Cleave, arena generation, survival conversion, boss bar, alert changes and announcements are not implemented.


Optional move callouts were requested as a later addition. Finish the functional moves first; add localized, rate-limited callouts in the presentation pass.



The next requested separate stage is Hadal Blade’s held-right-click charge UI, strong charging slowdown, two short dashes each ending in a Hadal slash, then a finishing stomp. Finish the Nullstar dash revision first.


