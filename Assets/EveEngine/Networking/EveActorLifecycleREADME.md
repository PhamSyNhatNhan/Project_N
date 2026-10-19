# Actor lifecycle runtime

> P6 (2026-10-19): the byte-stream actor replication prototype (`EveActorHostConnection`, `EveActorClientConnection`,
> `EveActorSnapshotScheduler`, `EveLifecycle*` models/codec, optional-replication admission) and `SpawnHost/ApplyHostSpawn`
> were removed. Entity creation, ordered publication and cleanup below remain the runtime contract; per-entity state travels
> through the typed Mirror adapters in `Assets/Coop`. Mod API is now 7.

## Prepare and publish

`EveEntityLoader.BeginPrepare(hub)` reserves identity/generation, collects dependency-
ordered steps and creates a one-use context. `PrepareNext()` performs one synchronous
module step; `Prepare(hub)` is convenience for running them all. No asynchronous
I/O step support. `Load(hub)` still performs Prepare+Commit for host/solo callers;
calling Load on an existing Prepared loader does not implicitly commit it.

Context has IsValid, IsPublished, Generation, LoadId and a shared publication gate.
CanUse additionally requires active session and no global publication turn. CanMutate
requires authority. IsInitialized is dependency initialization readiness; IsReady
is published gameplay readiness. The loader grants narrow AllowsModuleAccess while
initializing/unloading components. Update/input/gameplay must never use that flag
as a fallback. Prepared module collections cannot mutate Buff/control gameplay.
Passive Ability initialization inside the loader scope is allowed; it executes no
Actions. Bare stat/skill initialization without a load context stays standalone.

`CommitBatch(loaders, stagedStateSwap, publicationSession)` closes publication for
the session, runs essential EveIEntityCommitStep hooks and staged retained-state
swaps, then publishes all contexts/directory entries before notifications. Empty
new-loader batches are valid when reconciling retained actors and session is supplied.
Failing essential work invalidates every new context and a client connection before
dispatch resumes; Unload handles partial commit, not business rollback. Published
notifications isolate observer failures. Heavy/failing initialization belongs in
Prepare; essential Commit is expected cheap. Optional ConfigureCommit takes explicit
soft/hard/consecutive limits. Soft overrun warns and still publishes; hard/repeated
overrun fails. No default commit limit is picked.

Directory tracks `(EntityId,generation)` and retains failed cleanup records. Lookup
returns only the newest published presence, never falls back to an older one while
a newer generation is preparing. Older objects are invalidated/hidden when the new
one publishes. Removing the last tracked presence removes its directory generation
record; host ID allocation remains monotonically non-reusing. Logical dormant state
is held by the actor scope, not an object retained in the Unity directory.

## Object cleanup

ConfigureCleanup requires an explicit CleanupPolicy, monotonic clock and remaining-
frame-budget function. Track each prepared/ready network object before publish,
with a caller-owned return-to-pool/destroy callback. No new parallel pool is created.
Retire invalidates context and disables the object immediately (including render),
then schedules reversed module cleanup and final directory removal. Renderer authored
enabled flags are preserved for pool reuse. Return happens only after successful
cleanup; reset identity before returning a pooled actor. Idle pool memory/caps remain
the owning pool/admission policy's responsibility, not inferred from CountActive.

Queued/retrying/quarantined objects retain resident accounting. One cleanup ticket
per resident guarantees full retirement can be tracked; backpressure pauses new
preparation. Unity pump runs independently of Ready/authority, including Stopping;
duplicate pump calls in one Play frame do not multiply the configured budget.
Stopping retains code mods until entity cleanup finishes. Exhausted quarantine keeps
Stopping/LastError; explicit RetryCleanup is available. Never abandon resources to
force a new session. Unconfigured legacy host/solo End remains synchronous.

## Presentation and limits

Buff snapshots stage without creating VFX. Presenter/UI notifications flush after
global publication; same-generation resync preserves renderer groups. Host/solo also
use the same lifecycle application path. Configure PNBuffDetachedEffects explicitly
to offer presenter-owned end effects with independent resources, count cap (drop new),
absolute lifetime, real clock and session/disconnect cleanup. Its factory must use
captured pose/config/state, never actor references or a presenter lease. Silent
Unload/SessionEnded/snapshot reconciliation creates no effect. Headless render=false
creates no presenters/effects. Existing DOT presenters have no added ending artwork.

This is not all-world replication. Factory selects prefab; initial/snapshot pose is
applied from the descriptor. Continuous Move/pose streaming, resources/HP and gameplay
event contracts are separate.
The infrastructure accepts explicit test/profile policies and has no measured
production cap/deadline/goodput default. Snapshot count/bytes are bounded, but arbitrary
mod gameplay payload growth still requires each owning subsystem's admission/schema
limits. Trusted code mods may retain raw references or block a synchronous hook;
these lifecycle guards are not a sandbox. SHA-256 checks consistency, not authentication.

## Verification

Native PNLifecycleRuntimeEditorChecks: unpublished module APIs, whole-batch Ready,
generation replacement, detached presentation effects, Unity pooling, bounded
cleanup/quarantine/retry and commit failure. Real Play helper
PNLifecycleRuntimePlayReview checks publication across frames and the independent
Stopping pump. Use disposable scenes; preserve original scene/editor settings.
Smoke, relevant native Ability/Buff/Control/shared Dash and rendered elemental frames
provide regression evidence, not a new Player performance profile. See newest HANDOFF.


## Published presence

TryGet(id) and TryGet(id,generation) still expose only the newest Ready generation.
TryGetPublishedPresence(id,generation) checks exactly an already-published tracked
presence without resolving a newer/older object. Combat uses this during replacement
preparation; commit invalidates/hides old presence and makes the check fail.
