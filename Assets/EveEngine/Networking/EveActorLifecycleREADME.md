# Actor lifecycle runtime — RulesVersion67 / Mod API6

## Optional replication admission — RulesVersion74 / Mod API6

`EveIReplicationDeltaClassifier` is an additive opt-in on the existing `ReplicationDelta` event. Classify solely from the
emitted bytes, without mutation, state reads, callbacks or reentry. Existing parts remain Required. Classify and capture
run in one validated connection listener; reused-buffer/cache ownership contracts remain. Subscribers to the old event
still see every emission. Contract errors close that connection through the existing capture-failure code SnapshotTooLarge;
`ClassifierErrors` and the bounded first `LastReplicationError` distinguish the cause without a new wire code.

The original host/model constructors remain and preserve legacy delivery (no admission policy). The overload taking an
explicit `OptionalReplicationPolicy` enables per-peer filtering; no production defaults exist. Its parameters are optional
queued message/byte caps, required queued message/byte reserves and accepted-cap reserve. Policy bounds are validated against
ModelPolicy. Only Updated deltas may use the optional path; direct Spawn/Despawn/Dormant/World and controls stay Required.
An optional phase rejection occurs before capture; exact-size admission and enqueue are atomic at HostModel. No priority
queue, eviction, batching, codec change or wire class field is added.

At admission, queue caps minus reserve, optional queue budgets and this long-valued bound must hold:
`AcceptedBytes + BufferedBytes + ControlQueuedBytes + newWireBytes <= MaximumAcceptedBytes - ReserveAcceptedBytes`.
Pending is a view: do not add it again. Controls are counted once, survive BeginRecovery, and transfer to accepted bytes on
successful send. Snapshot chunks are sent only outside Live (optional admission disabled); accepted bytes of every kind stay
counted on return to Live. This is a conservative admission bound, not a promise that required overload cannot recover.
Required arriving later may use reserves or cause recovery. Partial aggregate ReleaseAccepted has no FIFO assumption.

Internal queue-entry classification decrements optional occupancy on acceptance or recovery queue-clear. Entity untrack
does not free connection-owned queued/pending/accepted bytes. Close clears occupancy; lifetime offered/admitted/drop-reason,
high-water, recovery and control counters remain diagnostic history. Retry bytes are immutable even when newer deltas arrive.
Future consumers such as Stat2 can reuse this API; this does not install popup traffic or a production transport.

Steps3/4 are integrated without transport or a selected production policy. Scope:
opted-in player/enemy/long-lived actors plus registered component presentation;
Core supplies Buff presentation. Move/HP/Combat/Skill/save/projectile replication
is not implied. Existing actors are not automatically enrolled in a network scope.

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

## Actor stream integration

EveActorHostConnection validates content compatibility, reserves the next host
connection incarnation and explicitly Track()s published actors. ConfigureSnapshots
first with explicit job/step/time limits. Identical pending scopes share one freeze;
one unique scope freezes per scheduler frame. Serialization starts in later frames
and never shares the frame of a freeze. EveIFrozenEntityReplicationPart captures an
immutable serialization thunk; it must not read Unity/live state when executed.
Buff freezes the event-fed publisher cache, not behavior polling. A component lacking
the optional freeze interface returns already-serialized immutable bytes at capture.

Host protocol enters PreparingSnapshot at the freeze cut: pre-cut delta is covered
by state, post-cut delta is held through serialization/transmission/Applied. Each
connection's sequence commits only on whole-batch acceptance; accepted bytes must be
released by the adapter when its reliable pipeline no longer retains them. Outbox
overflow/recovery replaces unsent gameplay, never accepted/in-flight messages.

EveActorClientConnection consumes authenticated ordered bytes, buffers slow-actor
delta independently, prepares objects across frames using EveEntityPreparationBudget,
stages subsystem data, commits the snapshot in one publication turn and emits Applied.
Snapshot replacement retains same-generation actor/presenter resources. Removed,
dormant and replaced actors hide immediately and retire through the cleanup queue.
Factory must return an inactive Idle loader with reset identity; it resolves the
selected registered character/prefab on the caller side. No concrete game factory
is constructed in EveEngine. Source attribution is ID-based, not a live source need.

Drive peers' AdvanceFrame(), shared session.Snapshots.AdvanceFrame(), then Send().
On client drive Receive() and AdvanceFrame(budget); queue TakeAppliedAck() before
new intent. There is no UTP/TCP/Steam adapter. The same process cannot host both
singleton registry sessions; integration tests record host, end it, then replay client.

JSON actor payload has bounded character/owner/initial transform descriptor and explicitly keyed
subsystem byte blobs, no CLR type handling; stream0 wraps them in bounded codec.
Each Updated carries current state plus ordered subsystem delta (including cues).
Buff generation equals entity generation. PNBuffReplica.ApplyLifecycle bypasses
the old per-target gap/recovery path; actor stream owns ordering/recovery. The old
standalone Buff Receive API remains separate for existing proxy checks; do not mix
the two paths for one replica. Optional cues can pass on stream0 and still follow
age/dedup rules. Static presenter configuration comes from matching registry.

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
record/replay, capture sharing/post-cut delta, renderer retention, generation/despawn,
Unity pooling, bounded cleanup/quarantine/retry and commit failure. Real Play helper
PNLifecycleRuntimePlayReview checks publication across frames and the independent
Stopping pump. Use disposable scenes; preserve original scene/editor settings.
Smoke, relevant native Ability/Buff/Control/shared Dash and rendered elemental frames
provide regression evidence, not a new Player performance profile. See newest HANDOFF.


## Actor delta and published presence (RulesVersion68 / Mod API6)

Full actor descriptors/snapshot parts remain wire version1. Live Updated payloads
use version2: character, owner, deltaPart and delta only. They are not loadable actor
snapshots and do not carry pose or unrelated subsystem state. Spawn, dormant and
resync still capture full state at the proper boundary. Client validates descriptor
and selected runtime subsystem before applying a delta, including queued updates
during slow load. Do not mix these rules versions in one compatible session.

The same emitted byte buffer/actor/generation/part shares one encoded immutable
actor delta across peers. Per-peer size validation, sequence/outbox/recovery stay
independent. Reused mutable buffers are compared against owned bytes before reuse;
weak-key cache entries cannot retain a session indefinitely.

TryGet(id) and TryGet(id,generation) still expose only the newest Ready generation.
TryGetPublishedPresence(id,generation) checks exactly an already-published tracked
presence without resolving a newer/older object. Combat uses this during replacement
preparation; commit invalidates/hides old presence and makes the check fail.

## Producer ownership (RulesVersion69 integration)

Freshly encoded actor buffers are privately owned and shared by ActorState, queued
envelopes and sequence-assigned envelopes. Public ActorState/Envelope constructors
and getters still copy caller data. Public Delta also copies; only the internal
actor path transfers private immutable ownership. Transport callbacks receive
defensive copies, including rejected retries. The pending bytes remain unchanged
when callback code mutates its supplied arrays.

ModelCodec.Encode writes the existing protocol1 binary envelope into an exactly
sized buffer, preserving byte order and header/payload format. No actor JSON/base64,
batch framing, stream ordering, transport or recovery policy changes are implied.
