# Buff presentation coop — RulesVersion76 / Mod API6

P4 integration is Rules77/network4: Stat/Buff/Move now share fixed catch-up targets
and pending accounting. Buff semantics below remain unchanged; see
`Docs/MirrorP4Implementation.md` for Move ownership and current acceptance.

## Current Mirror path (P3)

See `Docs/MirrorP3Implementation.md` for implementation/acceptance status and limits.
Mirror uses `PNBuffManager.TryCaptureState`, `StateChanged`, `OptionalCuePublished`,
`StageState` and `ApplyChange`. `PNBuffFrame` includes immutable selected states,
publisher revision, presentation sample and canonical group order. `PNBuffChange`
preserves final removal state/reason/required cues. Empty baseline is explicit at
revision0; it is different from missing required state.

`PNBuffWireState` remains the public presenter DTO for compatibility. Mirror serializes
its fields with bounded binary writers and JSON only for custom JToken data; it does
not use PNBuffWireCodec or the legacy per-target ACK/resync protocol. The sections
below about actor codecs/standalone Receive remain legacy APIs, not Mirror setup.
Never connect both transport facades to the same entity binding.

Host/offline owns behavior and one publisher. Clients own presentation only. UI uses
the stable `Presentation` source; callers of the low-level `Replica` property must
read the current replica after a transaction refresh. Prepared candidate transactions
create no presenter or cue before publication, restore the old owner on rollback,
and transfer surviving renderer resources on refresh. Initial publication flushes
before EntityReady. Required ending cues run before Removed/resource retirement.

Mirror initial state uses observer-authenticated stream nonce and fragmented baseline
assembly before Ready. Baseline1MiB, required record64KiB, chunk16KiB, instances4096,
custom payload16KiB/depth32/nodes4096 are initial hard transport guards. Per-presenter
default4KiB still applies separately. Limits must fit the configured transport; there
is no promise that all4096 instances with maximum payloads fit the total baseline.
Overflow fails clearly rather than truncating gameplay. Payload DOM construction is
preflighted and charged with retained transfer/stage ownership. Charges bound pending
work; they are not an exact managed-heap measurement.

Baseline, required lifecycle and admitted optional cues share reliable FIFO. Optional
may be dropped before enqueue, on payload rejection or freshness expiry; it never
updates required revision or clock mapping. Malformed transport framing remains a
protocol error. Presenter schema/factory/Update failures isolate that presenter while
keeping Buff state. Generic required-state failure closes the affected connection or
authoritative session through the adapter failure path.

Countdown derivation is supported **only when definition opts in**; the historical
statements below denying all replica-derived countdowns predate that contract.
Clock samples map host presentation time to network time, separate from per-Buff Rate.
Grace freezes extrapolation and resumes from coherent anchors, without client gameplay
ticks or a replay of all absent time. Stat+Buff fixed targets protect their own adapters;
this does not complete Skill binding/cooldown/execution or Move replication.

Summon and general presenter entity-reference resolution are separate integrations.
SourceId is attribution, not a stable Unity object reference across generation/reuse.

## Optional cue classification — RulesVersion74 / Mod API6

PNBuffManager implements EveIReplicationDeltaClassifier: validated prefix kind1 is Optional, kind0 lifecycle is Required.
All messages still use ReplicationDelta; local presentation and legacy subscribers remain unchanged. No Engine code reads
Buff payloads. Explicit per-connection OptionalReplicationPolicy enables admission; old connection constructors retain
delivery without filtering. Guaranteed cues inside lifecycle Updated/Removed are Required. Snapshot does not replay cues.

Optional cues can be refused before enqueue without allocating actor sequence positions. Accepted cues keep the same FIFO
ordering as lifecycle; queued cues precede a later Despawn. Despawn before client Ready cancels the waiting actor/cue queue;
invalid actor Updated-after-Despawn still recovers. Drop counters describe host admission, not client presentation or network
delivery. Body encoding occurs once before fan-out; admission does not promise elimination of all encoding cost.

Current actor path uses EveActorHostConnection/EveActorClientConnection and
EveIEntityReplicationPart on PNBuffManager. Actor generation is the Buff epoch;
ApplyLifecycle consumes outer ordered dispatch without a second Buff recovery
stream. Prepare builds collection only, Commit stages replica state, publication
flushes UI/VFX. See Assets/EveEngine/Networking/EveActorLifecycleREADME.md.
The per-target adapter sequence below documents the earlier standalone proxy API;
do not combine it with the current actor path for one replica. No transport is installed.

This contract is implemented without a production transport. Do not claim a
two-machine game, movement/HP/Skill/Area replication or prediction from these APIs.

## Authority and layers

Offline/Host PNBuffCollection owns Apply/remove/stacks/timers/RNG/damage/modifiers.
Client PNBuffReplica owns immutable presentation state only; it never constructs
PNBuffBehavior or runs its gameplay callbacks. PNBuffManager.Presentation exposes
PNIBuffPresentationSource for UI on both host and client. Live collection UI APIs
remain available. Replica UI snapshots use InstanceId; their Handle is not a local
gameplay handle and must not be used for mutation.

PNBuffPublisher reads committed instances and exports Message/OptionalCue events.
Host/solo feeds those events directly into the same PNBuffReplica used by clients.
Game registers PNBuffPresenterDefinition factories in buff_presenters; Core owns
wire contracts, scheduling/recovery and presenter lifecycle. RenderPresentation=false
must be set before load for a headless authoritative entity: no presenters are made.

## Adapter integration

1. Admit host/peer using existing compatibility checks. Authenticate transport sender.
2. Spawn/map the target using authoritative entity identity. Client manager then calls
   BindClientPresentation(hostSessionId, targetId, policy).
3. Supply AuthoritativePresentationTime as the clock estimator mapping local time to
   publisher's target clock. Default host presentation time starts at load and advances
   with Game delta. A network adapter must map each target anchor correctly; it must
   not assume client load time equals host load time.
4. Subscribe host publisher Message/OptionalCue; send an initial Publisher.Snapshot().
   Feed trusted messages to client Replica.Receive(message, authoritativeNow).
5. Bind SnapshotRequested to a rate-bounded authenticated host snapshot request.
   Buffering/reconcile is internal. Capture occurs on committed changes and anchors
   at a .25 second policy in the current publisher, plus clock/rate changes.
6. Observe replica Failed and session ConnectionFailed. Client manager invalidates
   the session on fatal replica failure; transport must close the connection and
   UI must present the code. Call RejectSession(ContentMismatch, reason) for a fatal
   compatibility/protocol failure found by an adapter. Malformed recoverable envelopes
   can trigger snapshot recovery by Receive(null, now) with the wire-message overload.
7. Unload/disconnect disposes proxies; end the registry session through its existing
   cleanup path. No gameplay client commands are added by this feature.

Wire DTOs are immutable; PNBuffWireCodec provides bounded JSON UTF8 envelopes,
protocol1, explicit IDs/numbers converters, depth32, required fields, no CLR type
resolution and a default1MiB byte limit. Transport must enforce a byte limit before
accepting the payload, authenticate sender, and limit request rates. JSON serialization
is an adapter convenience, not an implicit network event bus.

## Identity, order and recovery

Envelope=(authoritative session,target,epoch,sequence). Epoch and instance IDs are
authoritative, process-monotonic and checked for overflow; no reuse within a session.
Sequence belongs to replication, not collection UI Sequence. Per-instance Revision
adds semantic checking; target Sequence detects gaps. Only Added/snapshot create a
state; Updated never creates an unknown instance. Old session/epoch/sequence cannot
resurrect a removed instance. Bounded tombstones plus instance high-water protect ID
reuse after tombstone eviction. Call all proxy APIs on the owning Unity/main thread;
reentrant input is queued and cycle-bounded, not executed inside observer callbacks.

Replica waits for contiguous deltas, buffers during recovery, applies a validated
snapshot at its sequence, drops buffered packets through that sequence and drains
newer contiguous packets. Only Opened/snapshot can change epoch. Opened requests a
snapshot; publisher initial snapshot can establish the epoch directly. Duplicate/old
snapshots do not restart presenters or retry errors. Missing packets are given a
reorder window before snapshot request. Snapshot replacement reconciles by identity
and profile; existing renderer groups survive. Missing states retire silently: no
invented end reason or historical cue. Reset on the live collection means destruction
and subscription retirement; it is not reused as a snapshot operation.

Default PNBuffReplicaPolicy:256 buffered packets,4096 active instances,16KiB dynamic
payload/cue,3 recovery attempts,.1s reorder window,1s request interval,5s timeout.
These are initial bounded defaults, not measured transport guarantees. Buffer overflow
drops uncertain buffered input and requests a newer snapshot. Exhaustion closes the
replica with ResyncExhausted; content/profile mismatch closes with ContentMismatch.
Failed proxies clean presentation and signal their owner, rather than silently logging.

## UI versus VFX clocks

Numbers are exactly four nullable Int64 values. Null hides,0 is visible. Authoritative
behavior chooses visibility/position and publishes updates, including countdown via
WatchRemainingSeconds. Client UI never derives values from clock or reads InitialInput.
If input affects visible values, behavior explicitly exports Numbers or dynamic data.

VFX state has BuffTime, AnchorTime, Rate, Clock, Suspended and optional Expiration.
Presenter interpolates BuffTime+(now-AnchorTime)*Rate, with suspension stopping it.
This clock is cosmetic and cannot expire or mutate gameplay on client. Static colors,
sizes, icons and visual schema are resolved from ProfileId and matched registry content.
Dynamic values (effective stack cap, runtime phase etc.) are exported by behavior with
Presentation.PublishVisual(data, version); custom payload has registered version/schema
and limits. Unsupported presentation payload disables its presenter, not Buff state.

## Presenters and cues

Presenter constructor receives target, selected profile and owned resource scope.
Own partial initialization resources immediately. Mod presenters must not run gameplay.
Factories return fresh objects. GroupByProfile=true aggregates states ordered by network
ID; otherwise the presenter is per instance. Built-in elemental effects group by
(target,profile), retain one renderer and one visual lease per active source. Density
uses exported stacks/maximum; group clock uses maximum interpolated instance time
(oldest active clock), deterministic across host/proxy. LateUpdate draws shape/camera;
its shader clock is supplied by presenter, not inferred from client entity timescale.

Presentation.Cue(name,guaranteed:false,payload) is queued until the mutation boundary.
Optional cues may be lost; dedup/age/epoch/instance checks discard duplicates and late
cues. Important cues use guaranteed=true and travel with ordered Updated. OnEnding
uses context.EndingCue(name,payload), attached to Removed before resource release.
Removed also carries PNBuffEndReason. A presenter can react through Removed even if
there is no named cue. Damage numbers belong to a separate combat stream.

Snapshot has sufficient current state/phase to restore appearance and never replays
history. Ordered cue delivery is exactly once for accepted contiguous events; recovery
by a newer snapshot deliberately skips historical cues. Do not represent lasting
appearance only as a cue. Durable delivery through reconnect requires a separate
journal/ack mechanism and is not provided. Optional cue freshness has default1s in
the publisher; custom DTOs may specify another bounded age.

## Resource and trust boundaries

Remove/profile change/unload/epoch close/disconnect/fatal recovery retire owned resources.
Snapshot preserves surviving groups, retires disappeared contributions silently and
may retry a failed group up to the bounded failure allowance. Presenter exception
faults that slot first, stops calls, disposes presenter and each scoped resource,
reports once per attempt and preserves Buff state. One failed group does not retire
others. Core observer exceptions are isolated. Factories must scope resources acquired
before returning; the runtime cannot dispose an object a throwing factory never returned.

Code mods run with process rights; exception isolation is not a sandbox. Infinite loops,
unsafe code and arbitrary process mutation can still hang/crash the client.

## Migration and evidence

PNBuffDefinition now requires explicit ImplementationId after its ID. Derive retains
it, reference callback and PresenterId. Add validateReferences for every custom reference
field. Both Buff and Area use PNContentReferenceContext with staged definitions,
registry/alias and document dependency checking; there is no property-name inference.
Nested registered Action/Operation payloads delegate to their selected definition's
ValidateReferences callback. Rebuild code mods against API5.

Tests: PNBuffReplicationTests (seeded shuffle/duplicate/loss recovery, codec, epochs,
cue/renderer parity), PNBuffCoopEditorChecks (real scopes/fault/renderer reconcile),
PNElementalBuffEditorChecks (native rendering/clock/cleanup), PNBuffUIPlayReview.
Fresh evidence for this implementation is .utmp/BuffCoopReview, not older PASS entries.


## Efficient publication (RulesVersion68 / Mod API6)

Host/solo already applies typed messages directly to the same replica application
path as clients. Wire encoding is now skipped when no ReplicationDelta subscriber
exists; with subscribers it happens once per message, not once per peer. Optional
cues follow the same rule. UI numbers remain behavior-owned; no client countdown
is inferred from VFX clock data.

An unchanged clock extrapolates from BuffTime/AnchorTime/Rate. Capture publishes
when fields, rate, suspension, profile, payload version or the extrapolated clock
change, rather than issuing an unconditional quarter-second heartbeat. Clock
comparison tolerates double rounding (1e-8 relative); it does not change gameplay
timers. Manager-owned scheduled callbacks use the actual fraction within Advance
as their anchor/cue time. Direct collection Advance remains a supported separate
clock path; manager time is not subtracted from that direct call. The optional
PNBuffPublisher authoritativeTime provider lets standalone callers supply their
chosen clock; existing callers without it retain explicitly supplied Capture time.

Automatic host advancement no longer repeats the manager's final Capture/replica
Advance. Client replicas still advance presentation locally. Ordered/guaranteed
cues, snapshots, cleanup and grouped presenters retain their existing contracts.

Stock behaviors explicitly opt into EventDrivenReplication. When every live behavior
in a collection opts in, unchanged mutation revision and aligned clock progression
allow Capture to skip its state scan. Apply/reapply/stacks/scheduled callbacks, context
clock/duration writes and numeric/visual publication mark changes. A custom behavior
defaults to false and retains scanning, including a time-dependent StackCount getter.
Opt in only when all replicated changes follow these mutation/publication paths;
external state-dependent getters must keep the default. This is additive API6.

Scheduled callbacks compare StackCount before/after even on the event-driven path;
unchanged stock DOT ticks do not force a full replication state scan. Context and
presentation mutations independently invalidate the revision. Buff wire encoding
streams the existing protocol1 JSON schema directly; decoding and field validation
are unchanged. Legacy JSON parity includes null/zero, Int64 extremes, Unicode,
nested dynamic payloads and cues. This is not a binary protocol or transport change.
## StackCount failures during publication (RulesVersion71 / Mod API6)

An exception that escapes a `PNBuffBehavior.StackCount` call made by the publisher scan is counted as a failure of
that behavior incarnation (instance + behavior object; an override starts a new incarnation). This marks a boundary,
not a proven root cause: an infrastructure exception thrown through the getter is counted the same way. The scan
isolates it: the original exception is reported through `PNBuffCollection.Failed`, that instance keeps its last
published state, and the other instances are still compared and published in the same scan. The next outermost
Capture (any origin, even without a mutation) re-evaluates it once. If that retry succeeds the failure episode is
closed; if it fails, the instance is ended with `RuntimeError` through the coordinator after the scan (Removed with
RuntimeError on the wire; no schema change). A later failure after a successful retry starts a new episode, so an
intermittently failing getter can repeat fail -> retry -> fail without being ended. Each episode reports the first failure
and the policy retry; cleanup (the final snapshot read in `EndCore`) and nested evaluations that were already running can
report more. The end of a failed retry is submitted after an outermost scan that completed normally; if another
exception (outside the StackCount call) escapes the scan it reaches the caller, the instance stays marked and is ended
by a later outermost Capture that completes normally.

Observable changes: such exceptions no longer leave `PNBuffPublisher.Capture` (External, Changed, Cue, Advance), so
`PNBuffManager.AdvanceAutomatically` no longer tears the manager down for them; direct callers no longer receive them.
A retry evaluation of an incarnation is guarded: a Capture started from inside its getter skips that incarnation.
Healthy evaluations are unguarded (baseline re-entry) and keep the baseline StackCount read count/order. Ending runs
the normal cleanup (contributions, immunities, overrides) like a failing behavior callback. `EndCore` builds a final
snapshot that reads StackCount again; with a persistently failing getter `OnEnding` is skipped and reported.

Out of scope (previous propagation kept): `EventDrivenReplication` (read before the scan), snapshot/membership/runtime
exceptions and every other seam: direct Capture throws, an exception reaching automatic Advance tears the manager
down, Changed-capture exceptions are reported by Notify. Public signatures are unchanged (Mod API6); only the throwing
behavior of Capture for StackCount failures changed.

# Opt-in measurement diagnostics (RulesVersion69 / Mod API6)

Fixture5 instruments existing publication without changing payloads, tick ordering,
cache contracts or production policy. Diagnostic Capture origins distinguish
Changed/Cue/Advance-pre/Advance-post/external calls. Scan/inspection counts refer
to replication-state snapshot/comparison, not the event-driven eligibility check:
HasOnlyEventDrivenReplication still walks live behaviors on eligible skip paths.
Publisher residual is not a separately measured gate/codec cost.

Updated reasons diff old versus the newly materialized PNBuffWireState; no extra
behavior getter reads or public DynamicData clone is needed. Multiple reasons
are allowed, and guaranteed-cue revision is independent of state-field differences.
Collection provenance is a pending cause set with Unknown for unclassified paths;
it is not one-to-one attribution to each emitted message. Nested Notify restores
the prior active diagnostic cause and retains pending causes on observer failure.
Events carry session/target/epoch/instance and sequence in ordered per-frame records;
buffers are bounded with explicit dropped-record counters. All diagnostics are
off unless EveHotPathMetrics.Enabled or CountOnly is enabled. No coalescing,
binary actor payload, deferred encode or dirty-instance algorithm was implemented.

