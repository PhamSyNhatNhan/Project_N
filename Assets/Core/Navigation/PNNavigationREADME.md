# World navigation

Rules92 adds independent full-path probes for the optional
[agent toolkit](../AI/PNAgentToolkitREADME.md). They do not replace the follower's
Seeker request. Projection and probes use the exact actor load/world graph lease;
pending callbacks retain that lease until drained. One probe per actor load and
the shared four-starts/frame budget bound this work; no direct-movement fallback.

Installed backend is A* Pro5.3.3, pinned and checked at owner creation. Since G14 (2026-11-13, RulesVersion 150) the ONLY
navigation is the grid one below: the baked Recast cache (PNNavigationCache, PNNavigationBaker, PNWorldNavigationSource and
PNNavigatedSceneWorldProvider) is removed. Stock Grid local-physics scanning is not supported by this integration.

PNWorldNavigation owns one AstarPath per authoritative play. Graph leases are private
to world object/incarnation + navigation generation. Every nearest/path query is masked
to its lease and validates returned nodes. No cross-world connections are allowed.
Every world builds its own graph from its source.
No relocation/rotation/3D flight/off-mesh links/RVO is provided by this first release.

Retirement first closes admission and rejects late results. Pending callbacks still drain,
even if Seeker has replaced/retired its request; the direct Path callback is appended after
Seeker's modifier callback and copies vectorPath before pooled lifetime ends. Graph removal
waits for owned callbacks. Failed/pending cleanup keeps its owner and can be retried.
Session cleanup removes all graph leases before destroying its singleton.

A*5.3.3 masks are32-bit; code refuses an unrepresentable graph/index. The existing world
directory limits resident worlds to8, so one-profile worlds currently reach that cap
before the32-graph defense. The integration does not raise the world cap. Runtime physics
and LOS/sweep queries use the actor world's PhysicsScene; graph masks alone do not isolate
physics or mesh gathering.

PNEnemyMove validates the capsule against the profile radius/height on request.
Reconfigure an actor's footprint only together with its compatible navigation profile;
the small-agent profile is not suitable for a larger boss. Defaults in PNNavigationSettings
are starting values for the example, not measured production tuning/capacity.

## Grid navigation (2026-11-12, Docs/SandboxPlan.md G2; RulesVersion 139)

A world built from cell data (`Core/GridWorld/PNGridWorldREADME.md`) declares its navigation in the build context (`PNWorldBuildContext.SetNavigation(IPNNavigationSource)`) (a world without a declaration has no navigation). `PNWorldNavigation.Prepare` then calls `LoadGrid`: ONE `GridGraph` is built from the source (`Walkable` per cell, no physics scan: the grid's own collision scan reads the default physics scene, not the world's), and the lease keeps its API (`TryProject`, `Start`, `StartProbe`, `ProfileId`, `Radius`, `Height`) so enemies, toolkit and `PNRoomDirector` run unchanged. For a grid lease `Start` refines the funnel path before the enemy follows it (`PNGridPathRefiner`): inner waypoints at least radius + the enemy's `CornerTolerance` + 0.02 from blocked cells and every segment at least a body radius from them; a repath with two points is refined too. Limits of the first release: flat floor (`LoadGrid` refuses heights), the scan runs on the calling frame, one profile per world, a world has at most 32 graphs in all.

## Locomotion (G12b)
Graph nodes carry a terrain tag (`PNTerrainLayers.Tags`: land 0, others in barrier-layer order); a node is walkable when `Passable` (in bounds, not solid, clearance). Each request carries the tag mask of the body's effective locomotion (`PNWorldNavigation.MaskOf(body)` -> `Seeker.traversableTags`, `enabledTags`, `NNConstraint.tags`); path refinement uses `PNNavigationView(source, mask)`. `PNEnemyMove` re-paths when `IPNLocomotionSource.LocomotionRevision` changes and its obstacle cast ignores `Body.excludeLayers`. `solid` blocks every locomotion. The spawner service still prepares with the ground mask.
