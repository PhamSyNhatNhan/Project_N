# Grid worlds / World lưới (Docs/SandboxPlan.md G1, G2, G3)

EN: A grid world is built from cell data instead of a scene. **The cells are the single truth of the ground**: what is solid, how high the floor is and which ground type lies where. Walkable area, clearance, meshes and colliders are
derived from them, never stored twice. Built in steps G1 (data, provider, chunk objects), G2 (grid navigation) and G3 (edit gateway, placeable things). Not built yet: enemy life cycle and shared targeting (G4-G6), saving a running world (G7),
the chunk channel for coop (G8), the map tool and the spawner (G9), moving the roguelite rooms and removing Recast (G10). Decisions, measurements and the reasons: `Docs/Sandbox.md`.

VI: World lưới dựng từ dữ liệu ô thay cho scene. **Ô là sự thật duy nhất của nền**; vùng đi được, khoảng thoáng, mesh và collider đều dẫn xuất.

## Parts

| Type | Where | Role |
|---|---|---|
| `PNGridWorldData`, `PNGridWorldDescriptor`, `PNGridRegion`, `PNGridWorldLimits` | `Core/GridWorld/PNGridWorldData.cs` (pure C#, no Unity) | Cells: solid, height (steps of 0.25 m), ground type on a 2 m cell, clearance field (derived), a version per 32x32 chunk. The descriptor (width, depth, cell, map, seed) is strict JSON, bounded, unknown fields refused. |
| `PNGroundDefinition`, `PNGridIds` | `Core/GridWorld/PNGroundDefinition.cs` | A registered ground type (`grounds`: id, walkable, placeholder colour). `PNGridIds` holds the ids (feature `project_n:grid_world`, grounds, maps, the two test worlds). |
| `PNGridTestMaps` | `Core/GridWorld/PNGridTestMaps.cs` | Built-in test maps (`project_n:flat`, `project_n:test_maze`); the map tool of step G9 replaces them. |
| `IPNNavigationSource`, `PNNavigationProfile`, `PNGridNavigationSource` | `Core/Navigation/PNNavigationSource.cs` | What the world gives the navigation instead of a baked cache: the grid's shape, `Walkable` (what the graph gets, may include room for a big body) and `Blocks` (the physical obstacle). |
| `PNGridPathRefiner` | `Core/Navigation/PNGridPathRefiner.cs` | Pure path refinement from the cells: inner waypoints at least `margin` from blocked cells, every segment at least `radius` from them. |
| `PNGridWorldProvider`, `PNGridWorldHost` | `Game/GridWorld/` | The world provider (portable: the descriptor is what a save keeps) and the Unity side: one object per chunk with a ground mesh + collider (layer 13) and, where there are solid cells, a wall child (layer 9) with a mesh and merged box colliders. |

## Rules

- A world is `Width x Depth` cells (32 to 2048 per side, whole chunks of 32) of `CellSize` metres (0.25 to 1, dividing the 2 m ground cell). The edge of the world counts as solid.
- Change cells only through `PNGridWorldData`; every change bumps the version of the chunks it touches (and the neighbours across a border). Users (the mesh, later a save and the network) keep the version they last used per chunk and compare, so nobody has to clear a flag.
- The host rebuilds a chunk whose version changed (2 per frame). It removes the old colliders at once, so a ray in the same frame never sees a wall that was taken away. Chunk objects are made BEFORE the root is registered with the world presentation (registration scans every child once; measured 74 ms for 4096 children, 1.4 ms per later change); a rebuild only swaps meshes, it never adds or removes children of the registered root.
- The provider moves the root into the world's own physics scene, then `AddSlot("main", ...)`. Colliders live in that scene: the default physics scene does not see them.
- Heights are stored and meshed but the first grid navigation supports a flat floor only (`LoadGrid` refuses otherwise).

## Navigation (step G2)

A provider calls `context.SetNavigation(source)`; `PNWorldNavigation.Prepare` then builds ONE `GridGraph` in the lease from the source (never from a physics scan: the grid's own collision scan reads the default physics scene). A world declares its navigation in the
build context OR in a scene (`PNWorldNavigationSource`), never both; the Recast path is unchanged. The lease keeps its API (`TryProject`, `Start`, `StartProbe`); for a grid lease `Start` refines the funnel path before the enemy follows it, with
margin = body radius + the follower's corner tolerance + 0.02 (`PNEnemyMove.CornerToleranceValue`), including a repath that has only two points. First release limits: flat floor, the scan runs on the calling frame (a 2048 x 2048 world takes seconds), one profile (`ground-small`: radius 0.2, height 1.4) per world.

## Editing and placeable things (step G3)

After the world is built, cells change ONLY through `PNGridEditor` (`Core/GridWorld/PNGridEdits.cs`, pure): a `PNGridEditBatch` of solid / ground / height edits is validated whole (inside the world, a palette ground, at most 4096 cells, a 129-cell extent), queued (65536 cells at most) and applied on the authority, at most `PNGridWorldController.CellsPerFrame` (512) cells a frame, in the order queued. The cells that changed come back as CLUSTERS (`PNGridClusters`: cells in touching 8x8 tiles, each cluster the box of its own cells; one box around two far edits measured 411 ms against 15 ms), the clearance field is recomputed for each cluster only, and the chunk versions change so the host rebuilds the chunks. Height edits are refused while the navigation is flat only (`AllowHeight`).

`PNGridWorldController` (`Game/GridWorld/`, one per world on the root of the grid host; `PNGridWorldController.Find(world)`) owns the editor and `PNGridPlacements`, pumps the editor every frame on the authority and hands the changed regions to the navigation lease, at most `NavRegionsPerFrame` (8) a frame. The lease (`PNWorldNavigationLease.ApplyGridChanges`) updates walkability of each region grown by the source's `Influence`, then `RecalculateConnectionsInRegion`, in an A* work item; it numbers the changes (`ChangeSerial`) and `PNEnemyMove` repaths when a change lies near its route or within 16 m of the body (`ChangedNear`), so a wall put in front of a walking enemy makes it go around and a wall taken away gives it the short way again.

Placeable things: kind `placeable` of the `instances` registry (`PNPlaceableDefinition`, payload: `footprint {width,depth}` 1 to 8, `blocksPath`, `blocksSight`, `breakable`, `health`, `spawnBlock`, `spawnRateScale`, `safetyRadius`, `params`; unknown fields refused). A definition names a LOGIC class (`IPNPlaceableLogic.Broken`): `project_n:placeable_vanish` (nothing happens) and `project_n:placeable_leave_ground` (params `ground`: repaints the footprint). Shipped samples: `placeable_vanish` (1x1, health 30), `placeable_leave_ground` (2x2, sand, health 20), `placeable_marker` (1x1, does not block the way, `spawnBlock`, safety radius 6 m; drawn as a small yellow marker with no collider). A thing that blocks the way turns its footprint cells solid THROUGH THE GATEWAY, so the wall mesh, the colliders, the clearance field and the graph all follow from the cells and nothing blocks a body that the grid does not know about. `PNGridPlacements` keeps the list (`TryPlace`, `Damage`, `Break`, `Remove`, `At`, `InRegion`); a direction 0 to 3 turns the footprint a quarter each (an odd one swaps its sides).

Sync commands (`Game/GridWorld/PNGridCommands.cs`), sent to the world object `grid` (the root of the host): `project_n:grid_place` `{def,x,z,dir}` and `project_n:grid_break` `{x,z}` (one hit of `HitDamage` 5). The authority checks a living player of the sender in that world within 6.5 m of the footprint, a registered placeable and free cells; anything else is dropped without a word. The rules are in `PNGridCommands.ApplyPlace` / `ApplyBreak`, callable with a position. Clients see the result only once the chunk channel exists (step G8). Not built: free (off-grid) placement, `blocksSight` effects, health or damage from tools and enemies, saving placed things (G7).

Measured (Play Mode in the Editor, 2048 x 2048 world with a graph and no chunk objects): the world with its graph is ready in about 5 s and costs about 0.5 GB managed memory (Unity reports about 2.2 GB allocated in the Editor); one placement costs 0.04 ms on the cell side and 1.1 to 2.3 ms on the graph side (a constant cost of the A* update, not of the world's size; the plan asked for 1 ms and the cleanest run measured 1.16 to 1.31 ms); 256 things spread over the world are 256 regions and 65 to 100 ms of graph work, so they are spread over 32 frames of at most 3 ms; 256 things in one block are one region and 2.4 ms. Checks: `Tests/EveEngine/PNGridEditTests.cs` (79 checks) and `Assets/Tests/Core/PNGridEditPlayReview.cs` (`Begin(dir)`: 65 checks with a real enemy; `Begin(dir, large: true)`: the 2048 x 2048 timings).

## Checks

Smoke `Tests/EveEngine/PNGridWorldTests.cs` (descriptors, cells, clearance against a brute-force oracle, chunk versions, the navigation source, the refiner, the maze). Play Mode in the main Editor `Assets/Tests/Core/PNGridNavPlayReview.cs`: the world, its chunk objects and colliders, an edit
and its rebuild, the lease, five Star enemy bodies (production `PNEnemyStat` + `PNEnemyMove`, capsule of the Star prefabs) walking twelve routes, and the cleanup of meshes, materials and the graph. Start it in Play Mode from an EMPTY scene:
`PNGridNavPlayReview.Begin(outputDirectory)` (the Hall scene's own bootstrap would own the session) and read `results.txt`.
