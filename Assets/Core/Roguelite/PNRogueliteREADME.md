# Roguelite frame / Khung roguelite

EN: The data and the pure logic of the run (steps F0 and F1 of `Docs/RogueliteFramePlan.md`). Nothing here plays a room yet: the run director, the room director, enemy spawning, the
change of room and the end screen are steps F3-F6.

VI: Dữ liệu và logic thuần của run. Chưa có bộ điều khiển run/phòng, sinh quái, chuyển phòng, màn kết thúc (F3-F6).

## JSON (`extensions.project_n`, parsed in `PNBuffContentBatch.ParseRoguelite`)

| Key | Definition | Notes |
|---|---|---|
| `rogueliteRuns` | `PNRunDefinition` | `floors` (1-16 unique), `startMoney`, `reviveHealth` in (0,1], `victory` = `last_room_cleared`, optional `name` (localization key). |
| `rogueliteFloors` | `PNFloorDefinition` | `steps` (1-32): `choices` 1-3 and `rooms` [{room, weight}] (distinct, `choices` <= rooms); optional `enemyBuffs` (<= 8 Buff ids). |
| `rogueliteRooms` | `PNRoomDefinition` | `kind` combat/boss/rest/shop/event, `world` (a registered world = the map), `start` {mode interact/auto/none, `interactable` key, `countdown` 0-10 default 3, `delay` for auto 0-60}, `waves` (<= 16: `delay` 0-30, `spawns` [{character, entity default project_n:enemy, count 1-16, group}], <= 32 enemies), `clear` all_waves/none, `reward` {money}, `exits` (1-3 keys), `interactables` (<= 16 keys). |

Combat and boss rooms start by interact or auto, have at least one wave and clear by `all_waves`; the other kinds start at once (`none`), have no waves and `clear: none`. A key is used once per room
among the start switch, exits and interactables. Ids belong to the pack's namespace. A spawn must resolve to a prefab (the role's or the character's `body.prefab`). Whether a room's world is registered is checked by
`PNGameplayContentModule.ValidateContent`; whether its keys exist in the map, when the world loads. Sample: `StreamingAssets/BaseContent/roguelite/standard_run.json`.

## Logic (no Unity)

`PNRunRandom` (SplitMix64, streams Layout/Spawn/Reward) · `PNRunState` (floor/step/room, options, team wallet, players with health ratio and dead flag, stats, result) · `PNRunPlanner`
(first room, `PrepareExits` draws the gates by weight without repeats, `Choose` moves in) · `PNRoomFlow` (Entering - Waiting - Countdown - Combat - Cleared - Leaving, commands for the director).
Checks: `Tests/EveEngine/PNRogueliteTests.cs`; native `PNSyncWorldChecks.Run()` loads the real content, `RunMap()` checks the sample map.

## Map

`PNRoomSpawnGroup` (Game) names a place enemies may appear (points or a disc, picked from the Spawn stream). The sample map is `Assets/Game/Scenes/Roguelite/Rooms/PNRoomStarTest01/` (world
`project_n:map_star_test_01`, empty policy Continue: a new room world is empty until the team is transferred in, so a Discard policy would remove it before they arrive). A map scene carries no camera, runtime, light, event system or authored entity; its floor is on layer 13, its walls on layer 9.
A scene provider loads with `LoadSceneAsync`, so a map loads as a world only in Play Mode or a Player.
