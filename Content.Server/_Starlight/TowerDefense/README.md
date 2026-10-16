# Tower defense: arena and towers

Scope agreed for this stage: finish the arena/road, both towers and all three levels, verify, then pause for review. Sharkminnows, the dragon and the six-wave campaign are the next stage. The existing five-carp wave remains the test encounter.

## Try it

1. Build and launch the server/client normally. Join with an attached character and admin Fun permission.
2. Run `tdsetup` in the console. This creates a separate, lit, breathable test map, teleports your character to it, and opens its tower defense UI. Each invocation creates another map; use Reset for repeat games on the same arena.
3. Choose **Tider** (50) or **Security Officer** (100), then click an empty `.` tile. The arena has 400 shared starting cash for trying different placements and upgrades. Road tiles are blocked. The 13×9 board has north at the top; `(0, 0)` is the lower left. The shop button cancels placement.
4. Click a placed `T1` or `S1` to inspect its stats, weapon, next upgrade cost and highlighted range. Use **Upgrade** to buy the next level. Tider upgrades cost 75 and 125; Security upgrades cost 125 and 200. Both stop at level three. Security's final upgrade may require a few carp rewards if it is your first purchase.
5. Click **Start wave**. Five carp follow the asphalt arrows from the red portal to the blue base portal. Towers attack automatically. Kills pay 10; escapes cost one of five base HP. Clearing the wave with any base HP wins.
6. Close or move the UI to see the world entities. Activate the arcade console to reopen it. Use **Reset arena** after victory/defeat.
7. To test defeat, reset and start without placing any towers. Test a second arena to check independence.

The UI board is the placement/selection surface. Direct world-click placement, attack animations and an arena exit button remain deferred. Ordinary station combat does not damage minigame enemies. The economy is shared by people using the same arena. Tower damage is applied directly by the minigame: the held guns provide the appearance, with no ammunition handling or normal firearm combat involved.

## Implementation map

- `Content.Shared/_Starlight/TowerDefense/`: arena/wave/tower definitions, route validation and movement math, replicated enemy health, BUI state and requests.
- `Content.Server/_Starlight/TowerDefense/`: arena and tower runtime components, authoritative gameplay system, `tdsetup` command. `TowerDefenseSystem.Arena.cs` builds the arena; `TowerDefenseSystem.Towers.cs` handles purchases, weapon changes, upgrades and targeting.
- `Content.Client/_Starlight/TowerDefense/`: BUI/window and world-space enemy health overlay.
- `Resources/Prototypes/_Starlight/TowerDefense/tower_defense.yml`: arena, wave, enemy, both tower definitions and levels, clothing loadouts, endpoint markers and console entities.
- `Resources/Locale/en-US/_Starlight/tower-defense.ftl`: UI text.
- `Content.Tests/Shared/_Starlight/TowerDefenseRouteTest.cs`: route validation, full road occupancy, corner movement, long frames, timestep consistency and the new winding arena route.
- `Content.IntegrationTests/Tests/_Starlight/TowerDefense/TowerDefenseTest.cs`: invalid purchases, both towers and all tiers, held-weapon replacement/deletion, stale and unaffordable upgrades, cross-map rejection, combat changes after upgrading, targeting, wave defeat, reset and victory.
- `Content.IntegrationTests/Tests/_Starlight/TowerDefense/TowerDefenseUiTest.cs`: attached-client setup, actual shop/placement/upgrade/start button clicks, server feedback, window closing and reopening. Retains coverage of the earlier duplicate window registration assertion.

`TowerDefenseSystem` uses `SharedMapSystem`, `SharedTransformSystem`, `AtmosphereSystem`, `GravitySystem`, `OutfitSystem`, and the existing Bound UI framework. Tower visuals inherit `AppearanceHuman`, the existing human preview dummy with inventory/body/hands, avoiding a living NPC's survival and combat AI. Carp reuse the existing space-carp sprite on a lightweight minigame entity.

The arena owns its enemy/tower collections. A tower retains its target until it dies or leaves range, then chooses the enemy furthest along that arena's route. Damage, cash, placement checks, upgrades, wave advancement and game outcomes run on the server. Upgrade requests include the expected current level, preventing a repeated or stale request from purchasing another level. Weapon replacement uses the hands API; old weapons are deleted and clothing stays equipped. The client sends intent and displays server state. Enemy HP is dirtied on damage; BUI state is sent when relevant values change. Deleting the controller queues its owned enemies and towers for deletion.

## Configuration

`towerDefenseArena.route` is a list of quoted integer vectors, including both endpoints. The current route is `["0,7", "10,7", "10,4", "3,4", "3,1", "12,1"]`. Every segment must be nonzero and cardinal, stay within width/height, and never revisit a road tile. The validator builds the tile occupancy set once. Movement consumes distance through successive corners with the transform API, without pathfinding. `buildFloor`, `roadFloor` and `deckFloor` configure the arena materials. Border rails stay outside purchasable tiles; endpoint portals have no collision or portal behavior.

`waves` holds ordered waves, each with `spawnDelay` and ordered `enemies` groups (`prototype`, `count`). Advancement waits for both the spawn queue and active enemies to empty. `waveBreak` controls subsequent intermissions. There is no hardcoded six-wave limit. This stage supplies exactly one wave.

`TowerDefenseEnemy` fields configure max HP, speed, reward and base damage. The arena's `towers` list is the purchase whitelist. Each `towerDefenseTower` defines its name, symbol, entity, clothing-only starting gear, purchase cost and ordered `levels`. Each level defines its display name, held weapon, damage, cooldown and range; levels after the first also define `upgradeCost`, the price to enter that level. Runtime component `Level` is zero-based; UI/request levels are one-based. Add levels through YAML rather than changing the attack loop.

## Verification commands

Verified for this stage: the integration-test project (including shared, server, client and unit tests) builds with zero errors; all five route tests and all five integration cases pass. The build reports existing repository warnings. The client test exercises actual buttons and network updates, including upgrades and reopening with the upgraded tower still displayed. Window creation remains idempotent because initial replication and component startup can both queue `Open()` for the same BUI. No manual graphical client playtest was performed.

```powershell
dotnet build Content.IntegrationTests/Content.IntegrationTests.csproj --no-restore
bin/Content.Tests/Content.Tests.exe --filter "FullyQualifiedName~TowerDefenseRouteTest"
bin/Content.IntegrationTests/Content.IntegrationTests.exe --filter "FullyQualifiedName~TowerDefense"
```

Visual playtesting should check the held bat and clothes, HP bar positioning, corner movement/interpolation, and window layout. Automated server tests do not establish visual correctness.

## Next stage, after review

1. Add sharkminnows, the dragon and the remaining five waves, then test progression and final victory.
2. Balance the full campaign, including starting cash and upgrade affordability. The current 400 cash is a review budget for the single-wave encounter.
3. Add attack feedback, improve placement UX and provide a return/cleanup flow for test arenas.

Do not add unrelated gamemode, matchmaking or navigation systems.
