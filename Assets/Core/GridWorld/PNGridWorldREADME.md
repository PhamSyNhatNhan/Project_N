# Grid worlds / World lưới (Docs/SandboxPlan.md G1, G2)

EN: A grid world is built from cell data instead of a scene. **The cells are the single truth of the ground**: what is solid, how high the floor is and which ground type lies where. Walkable area, clearance, meshes and colliders are
derived from them, never stored twice. Built in steps G1 (data, provider, chunk objects) and G2 (grid navigation). Not built yet: placing and breaking (G3), enemy life cycle and shared targeting (G4-G6), saving a running world (G7),
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

## Checks

Smoke `Tests/EveEngine/PNGridWorldTests.cs` (descriptors, cells, clearance against a brute-force oracle, chunk versions, the navigation source, the refiner, the maze). Play Mode in the main Editor `Assets/Tests/Core/PNGridNavPlayReview.cs`: the world, its chunk objects and colliders, an edit
and its rebuild, the lease, five Star enemy bodies (production `PNEnemyStat` + `PNEnemyMove`, capsule of the Star prefabs) walking twelve routes, and the cleanup of meshes, materials and the graph. Start it in Play Mode from an EMPTY scene:
`PNGridNavPlayReview.Begin(outputDirectory)` (the Hall scene's own bootstrap would own the session) and read `results.txt`.
