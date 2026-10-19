# RTS battlefield foundation

The battlefield now includes two headquarters and four command drones per team,
with selection and server-authoritative movement. Combat, victory, economy,
and opponent AI are subsequent stages.

## Try it

On a development server as a local administrator, run:

```text
rtsmap 173 96
```

Both arguments are optional. The default is a random seed and a 96×96 map;
supported sizes are 80–120 tiles. The command prints the map ID, seed, a `tp`
command to enter the blue clearing, and a `gamemap:rmid` command to delete it.
Generation requires the Fun flag; `tp` and `gamemap:rmid` require Admin.
Use an admin ghost to inspect the map. Leave the map before deleting it, as map
deletion also deletes its children. Regenerate with the same seed and size to
reproduce terrain, routes, and tree positions.

## Command a squad

Run `rtsjoin` to command blue on your current or sole battlefield. Use
`rtsjoin <map ID>` when multiple battlefields exist, or `rtsjoin <map ID> Red`
to test red. The command is one word and prints a confirmation after attaching.
Both use the Fun flag and require an in-game session. This temporarily controls
a dedicated camera, with the usual body or observer restored when you exit.

- Left-click a friendly unit to select it; drag a box to select a group.
- Shift-click toggles a unit; Shift-drag adds units to the selection.
- Right-click terrain to move the selected units in a small spaced formation.
- Use Select visible or Select idle to gather friendly units in view.
- Stop cancels selected orders; Clear only clears selection. Exit RTS returns control.
- Controls expands the camera, selection, and movement help.
- WASD pans the camera. Numpad 4/6 uses SS14's existing zoom controls.

Selection rings and a destination marker show commands; the panel reports selected
idle, moving, and blocked units, visible friendlies, battlefield size, and camera
coordinates. It stays above the gameplay viewport and scrolls when space is limited.
An unreachable destination leaves a unit blocked until its
next command. Selection is limited to 32 units. Infantry currently uses tinted
cleanbot sprites as temporary art, and has movement and durability but no weapon.
Red units await manual commands in this stage.

## Generation

`RtsBattlefieldSystem` reuses `BiomeSystem.EnsurePlanet` for the outdoor
environment and the expedition `Grasslands` template for terrain and decals.
It materializes a finite square with the same public sampling APIs used by
dungeon generation, then removes `BiomeComponent` before map initialization.
Ordinary expedition streaming and off-map biome queries cannot extend or unload
this battlefield. Its edges currently meet empty map space; destinations are
validated against battlefield bounds, but match containment remains a later stage.

The `RtsForest` template nests a sparse tree layer inside a broad noise mask to
form groves. Grasslands rock walls and water entities are omitted. Two circular
clearings and three curved dirt routes reserve open space before tree placement.
Trees retain existing collision, damage, chopping, and log drops.

Headquarters reuse static structural physics, damage, destruction, and existing
sprites. `RtsOwnerComponent` networks team ownership; battlefield data and the
HQ marker stay server-side. Nothing registers the map as a station, expedition,
FTL destination, or game rule. Generation has no ongoing RTS update loop.

Movement orders reuse `NPCSteeringSystem`, the native pathfinder, input movement,
and physics. Only units with active orders participate in steering; completion,
failure, and Stop remove that work. There is no autonomous HTN behavior. Network
requests are checked against the session's commander, map, team, bounds, request
size, and command rate. Client selection and HUD state cannot grant ownership.

Adjust terrain, forests, and layout in `Resources/Prototypes/_Starlight/RTS/`.
HQ durability and visuals are also defined there. There are no new art assets.

## Verification

`RtsBattlefieldTests` covers finite terrain, headquarters and clearings,
tree-free connectivity, seed reproducibility and variation, stability without
nearby players, invalid size rejection, and map/entity cleanup. Connectivity
checks obstacle tiles. Additional RTS tests cover ownership and network request
validation, actual pathfinding around headquarters, arrival, cancellation,
camera attachment after observer deletion and team switches, body/observer return
on exit, and battlefield cleanup.
