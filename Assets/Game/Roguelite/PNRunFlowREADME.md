# Run flow / Luồng run (F3-F4)

EN: How a run is played on the machine with authority. Design and what is still to do: `Docs/RogueliteFramePlan.md`. The data and the pure logic are in `Assets/Core/Roguelite/`.

VI: Một run được chơi thế nào ở máy có quyền. Dữ liệu và logic thuần ở `Assets/Core/Roguelite/`.

## Parts

| Type | Role |
|---|---|
| `PNRunDirector` | One per play (`PNGameSession.Runs`, advanced by `PNGameSession.Update`). `Begin(runId, fromWorld, seed)` takes the players of `fromWorld`, draws the first room and changes room; `Advance(gate)` takes a gate of the cleared room; `Exits()`, `Abandon()`. Phases `None, CreatingRoom, Transferring, Playing, Ended`; `Changed` event for views. |
| `PNRoomDirector` | One per room played (`Room`). Carries out the `PNRoomFlow` commands in the room's world: spawns waves through `PNEntityHosts` (places from `PNRoomSpawnGroup` with the run's Spawn stream, projected on the navigation mesh), puts the floor's enemy Buffs on them, counts deaths, watches the players, pays the reward, and opens and closes the room's interactables (start switch when the room waits for it, gates after a clear with the kind of room behind each, the objects the room names). `PressStart()` is what the `room_start` handler calls. |
| `PNRunActorStateProvider` | `project_n:run_actor_state`: all a run keeps of a player between rooms (health ratio, dead flag). Named by the transfer request, never by a world policy. |
| `PNWorldTransferRequest.Characters` / `.StateProviders` | Replacement transfers only: the character each owner is rebuilt as, and the state providers used to capture and restore. |

## A room change

draw the room - create its map world - wait for Ready - **replacement transfer** of every player (rebuilt from scratch as its recorded character: Buffs, cooldowns and resources are new; health is restored as a ratio, the dead come back with the run's `reviveHealth`) - the director takes the new world's room - the old world is unloaded (a world with a Discard policy discards itself). A failure before the cut-over leaves the team where it was and ends the run `Failed`.

## Start

Every start goes lobby -> run. A solo start loads the lobby, waits for the player and starts by itself (`PNGameFlow.Launch`/`StartRun`); a host starts when the room is ready. `-pn-legacy-run` keeps the old solo start straight into the ErMode arena (the mob Player reviews use it); `-pn-run-seed=<n>` fixes the seed. Map worlds report the `Run` phase.

## End of run (E, 2026-11-04)

All players dead -> `OnDefeat` -> 1.5 s -> `Defeat`; the last room's gate -> `Complete` -> `Victory`. `PNRunEndView` (`Assets/Game/UI/PNRunViews.cs`) shows the summary and one button to `PNGameFlow.ReturnToHall`; the director stops ticking the room once ended. Review: `PNRunFlowPlayReview.Scenario = "defeat"` / `"victory"`.

## Interaction and HUD (D, 2026-11-04)

The start switch, the gates (`Advance`, or `Complete` on the last room), the fountain and the shop heal are `PNInteractable`s of the map (`Assets/Game/Interaction/`, `Assets/Core/Interaction/PNInteractionREADME.md`). `PNRunHudView` and `PNInteractionListView` (`Assets/Game/UI/PNRunViews.cs`, hosted by `PNUiRuntime`) draw the room kind, the wallet, the 3-2-1 countdown and the list; F uses the selected entry, the wheel or D-pad down/left moves it.

## Not done

In-world labels over the gates (the list carries the kind), mobile touch and gamepad checks, late join and simulated latency in coop. The client's view of the run and of the interactables goes through the sync layer (`PNRunStateSync`, `Assets/Coop/PNSyncTransport.cs`) and was checked with a real host + client run (2026-11-04).

## Checks

Play Mode or Player: `PNRunFlowPlayReview` (`Assets/Tests/Core`; `PNRunFlowPlayReview.Begin(seed)` in Play Mode, flag `-pn-run-flow-review` in a Player; seed 1 = shop behind the first gate, 35 = rest room): walks to each object and uses it through the real command, checks the HUD data and writes frames. Native: `PNWorldTransferChecks.Run`, `PNSyncWorldChecks.Run/RunMap`.
