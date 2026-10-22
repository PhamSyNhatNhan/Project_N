# World navigation

Rules92 adds independent full-path probes for the optional
[agent toolkit](../AI/PNAgentToolkitREADME.md). They do not replace the follower's
Seeker request. Projection and probes use the exact actor load/world graph lease;
pending callbacks retain that lease until drained. One probe per actor load and
the shared four-starts/frame budget bound this work; no direct-movement fallback.

Installed backend is A* Pro5.3.3, pinned and checked at owner creation. Recast is baked
from static authored mesh roots; runtime loads nodes/settings, never globally scans
physics scenes. Stock Grid local-physics scanning is not supported by this integration.

PNWorldNavigationSource declares a required PNNavigationCache and expected fingerprint.
Configure(cache, cache.Fingerprint), then SetGeometryRoots(authoredRoots) for build
validation. One source/profile/graph per world is supported. No source leaves existing
world preparation behavior intact. A declared missing/stale cache fails preparation.
PNWorldRuntime calls Prepare before CompleteLoading and actor publication, also on Adopt.
Clients validate the declaration but create no Astar worker or graph.

Use PNNavigatedSceneWorldProvider.Definition(id, scenePaths, fingerprint) in a game content
module to include scene selection and the navigation fingerprint in the world content
hash. Its provider validates the loaded declaration against the selected fingerprint.
Custom providers must provide the same compatibility guarantee; a graph index is never
a persistent content/world ID. No current authored map/provider was switched by this feature.

PNNavigationBaker (Editor) takes explicit roots/profile/radius/height/bounds and creates
cache data through Recast's custom mesh collector. It does not save/reload the active
scene or modify original objects. The callback is cleared before serialization because
TinyJson5.3.3 otherwise serializes its delegate. All nodes are already in the cache.
Active vendor Recast modifiers/skinned meshes are unsupported and rejected. The first
baker is static mesh based; collider-only geometry/terrain needs an explicit future input
adapter, not an implicit global scan. Include only geometry whose collider layout matches
the baked meshes, then verify slopes/clearance with actual bodies.

Tools/Project N/Navigation/Bake selected mesh roots saves/updates a cache asset while
preserving an existing GUID. It does not add or configure a scene marker automatically.
The build validator checks a marker's declared roots against its baked geometry hash.
Bounds/settings/profile/version and serialized graph data participate in the cache
fingerprint; matrix/vertex/index data participate in geometry hash. Runtime validates
cache bytes/descriptor against the selected expected fingerprint. It does not rescan
vertices of arbitrary bundled runtime assets; mod builders must validate/bake them too.

PNWorldNavigation owns one AstarPath per authoritative play. Graph leases are private
to world object/incarnation + navigation generation. Every nearest/path query is masked
to its lease and validates returned nodes. No cross-world connections are allowed.
Same-cache instances deserialize separate graphs, with vendor GUID remapping when needed.
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

PNEnemyMove validates the capsule against the baked radius/height/profile on request.
Reconfigure an actor's footprint only together with its compatible navigation profile;
the small-agent cache is not suitable for a larger boss. Defaults in PNNavigationSettings
are starting values for the example, not measured production tuning/capacity.
