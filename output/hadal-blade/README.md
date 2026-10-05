# Scarlet's Hadal Blade

Spawnable prototype: `ScarletHadalBlade`, installed in `Resources/Prototypes/_Starlight/Admeme/scarlet/hadal_blade.yml` alongside Scarlet's other prototypes.

Rebuild the client and server, restart them with these resources, and spawn a fresh blade:

```text
self spawn:in "belt" ScarletHadalBlade
```

Take it into a hand and use the normal wield control (Z by default) to grip it with both hands. It is a physical crystalline sword: there is no energy-sword activation, retraction, hum, or damage replacement. It remains visible when unwielded. Belt storage has a dedicated four-direction character overlay.

## Combat

- Melee: 5 piercing, 15 slash, 10 cold; 20 structural damage to structures.
- One swing every 1.25 seconds, with 1.5-tile reach and a 60-degree wide attack.
- 12.5% movement penalty while held.
- Energy-sword reflection probabilities, available while held; Central Command restricted classification.
- Melee attacks and Hadal Crush require both hands. Direct `MeleeWeapon.Damage` VV edits remain in effect when wielding changes.

**Hadal Crush** appears in the action bar while holding the blade. Wield it, select the action, and click a direction. Stand still through the 0.7-second windup to release three pressure crescents across a 36-degree fan. Each crescent deals 8 cold + 3 blunt and slows a struck target by 35% for 2.5 seconds. The projectiles travel about 5 tiles and collide normally with obstacles. No knockdown is configured. Movement, damage, and changing hands interrupt the windup. Cooldown: 25 seconds, retained through dropping or passing the blade.

The ability uses a small shared component/system plus existing action, DoAfter, projectile and status systems. There are no permanent test systems. The component exposes the projectile prototype, count, spread and speed as data fields; action timing and projectile damage/slow are in the YAML.

## Art and sound

The eight-frame idle animation runs over 3.2 seconds. Its silhouette, grip and guard remain fixed; the amethyst heart changes brightness inside the blade. There are no drifting condensation pixels. The item uses a separate unshaded core layer. The attack crescent has four frames over 335 ms and a -90-degree layer correction to match the engine's south-facing effect convention.

Audio comes from existing resources: pitched-down `slash` / `bladeslice` / `block_metal1` for melee, `metal_scrape3` / `metal_thud1` for wielding, `Magic/rumble` for Hadal Crush and `glass_crack2` for pressure impacts. No new audio files are needed.

Open **preview.html** for the offline animated sprite preview, with background, hand and zoom controls. **contact-sheet.png** is the static overview. These show the exported layers; character occlusion and audio balance still need an in-game visual/listening check.

| RSI folder | Frame size | Usage |
| --- | --- | --- |
| `HadalBlade.rsi` | 32 x 64 | Static icon, eight-frame idle and core overlay |
| `HadalBladeInhands.rsi` | 64 x 64 | Both hands, normal/wielded, four directions |
| `HadalBladeBelt.rsi` | 32 x 32 | `equipped-BELT`, four directions |
| `HadalBladeEffects.rsi` | 64 x 64 | Pressure-cut melee effect and ability crescents |

These folders are under `Resources/Textures/_Starlight/Admeme/Scarlet/`. Directions use the engine order South, North, East, West. Animated in-hand sheets have eight frames per row, one direction per row. Legacy `off`/`on` texture aliases remain for compatibility with earlier character scripts; the loaded weapon does not use a toggle. Do not apply black `EnergyColor` modulation to the authored purple sprites.

## Validation and reproduction

`Validate-Sprites.ps1` checks the repository RSI schema, PNG frame counts, direction timing, transparent margins, at most 16 colors, binary alpha and fixed idle silhouette/grip/guard. It validates 4 RSI folders, 15 states and 290 packed frames. `validation.txt` contains the results; build and engine-check logs are separate.

The client/server/test build passed with warnings and zero errors. Both the existing prototype-component check and a temporary ability smoke check passed. The latter verified two-handed gating, projectile direction/count/damage, action grant/removal and cooldown retention. `verification.txt` describes the checks and limits. The temporary fixture is archived here as `HadalBladeSmokeTest.cs`, outside the test project; no permanent test-project changes remain.

```powershell
./output/hadal-blade/Export-Sprites.ps1
./output/hadal-blade/Validate-Sprites.ps1
./output/hadal-blade/Build-Preview.ps1
./output/hadal-blade/Package.ps1
```

The export scripts target this Windows/.NET workspace. The generated source art is in `source/`: `approved-concept.png`, `item-atlas-v2.png`, `belt-source.png`, and `swing-atlas.png`. The earlier `item-atlas.png` is retained as the previous version. OpenAI image generation produced the artwork; the exporter performs native-size sampling, palette conversion, registration, directional placement and RSI packing. No existing weapon sprite pixels were copied. RSI metadata records the repository's CC-BY-SA-3.0 non-code asset license and generated-art provenance.

`HadalBlade.example.yml` is a reference copy only. Edit the YAML under `Resources/Prototypes/` for live changes. The ZIP includes the loaded prototype, shared C# implementation, localization, four RSI folders and these supporting assets; it requires a rebuild when installed in another checkout.
