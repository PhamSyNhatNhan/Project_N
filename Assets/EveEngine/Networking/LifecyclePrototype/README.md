# Lifecycle protocol prototype — steps 1/2

Current RulesVersion67 / Mod API6 runtime adapters now consume these tested state
machines; see ../EveActorLifecycleREADME.md. The pure classes still create no Unity
objects. Older step1/2-only statements below are historical when contradicted by
the runtime README; policies remain explicit test/profile inputs, no production defaults.

Experimental, transport-independent C# under `EveEngine.Experimental.Lifecycle`.
This is an executable model for the root `ENTITY_LIFECYCLE_COOP_CONTRACT.md`, not
the current `EveCoopSession` API or a released wire protocol. No runtime constructs
these models automatically. RulesVersion66 / Mod API5 remains current: no gameplay
rule changed and no supported public API was removed. No SDK rebuild in this pass.

## Ownership and entry points

- `WorldModel`: host-side pure identity reservations, monotonically allocated IDs,
  generations, representability admission and explicit game-mode dormant retention.
  Cancelled IDs/generations are not reused. Session end clears retained state.
- `HostModel`: one connection, handshake outcome, recovery rounds/cycle, snapshot
  chunks, delta hold until Applied, control results, atomic batch acceptance and
  immutable retries. `ReleaseAccepted` is a separate adapter acknowledgement of
  freed reliable-pipeline storage. Closing one model does not close another peer.
- `ClientModel`: contiguous ordered envelope dispatch, bounded assembly and load
  queues, snapshot candidate reconciliation, preparation completion tokens,
  explicit one-turn `Commit`, Applied ack before new intent, control results and
  cleanup. Current view remains until snapshot commit. `IsReady` describes a
  retained prepared/published key; dispatch is additionally gated by client phase.
- `ModelCodec`: bounded binary experimental envelope (stream0), opaque actor state
  bytes and SHA-256 snapshot body. Hash checks data consistency, not sender
  authentication or resistance to forgery. `ActorState`/`Envelope` defend byte arrays.
- `ModelPolicy`: mandatory constructor inputs, no production defaults. Unit is ms
  for monotonic clocks, bytes for buffer limits. Clock does not use entity/game time.

The caller supplies authentic session/incarnation and handshake compatibility
results; there is no authentication, policy negotiation parser or network adapter.
Actor state bytes do not execute code and must be validated by their future owning
subsystem. World reserves new slots before publish; rejected payload growth leaves
state intact. Call Cancel on an abandoned reservation. The world model is not the
Unity directory, and a model generation is not the current independent Buff epoch.

## Sending and preparing

`TrySend` callback accepts/rejects an entire batch atomically. It must not reenter
the model. Rejected batch bytes and sequence are retained for retry. Passing a new
maximum batch size does not split a pending batch. Accepted bytes remain accounted
until the adapter reports release; End accepted is not End delivered.

Capture in WaitCapture encodes a bounded immutable candidate synchronously in this
model. Delta before capture is covered by that candidate; after capture it is held
until the current snapshot/end-sequence Applied arrives. IntentResult is a bounded
control queue and can interleave chunks or pass while awaiting Applied/recovering;
it never changes actor data. `TakeIntentResult` returns opaque control to the future
intent owner. Pending intent IDs/idempotency/UI integration are not implemented.

Client `NeedsPreparation` lists keys requiring a future loader/object lease.
`BeginPreparation` allocates a checked, connection-monotonic LoadId and returns
PreparationToken(incarnation, LoadId, key); repeated calls for the same pending
attempt return the same token. `CompletePreparation(token,...)` rejects stale or
duplicate attempts, independently of identity history. Matching attempts/candidates
can survive snapshot replacement; canceled attempts cannot complete into a new
attempt with even the same entity/generation. Failed loads close the
connection. Rejected late objects must be cleaned by their future owner; this model
has no Unity resource to destroy. Commit swaps the complete actor map once. Soft
budget overrun is not an error input; logical failure or hard/repeated overrun is.
`TakeAppliedAck` represents queueing the ack before enabling new intent.

There is no optional-cue or detached-VFX implementation in this pure actor model;
those remain in the Buff implementation/future lifecycle integration. Duplicate
wire messages do not dispatch twice; no claim of end-to-end cue/render parity here.

## Model limits and remaining work

The model uses body-byte caps distinct from per-envelope wire bytes, queue limits
and actor slots. Current/loading/candidate generation floors are mandatory and
separate from DiagnosticHistoryCapacity. Recently closed IDs use a FIFO ring;
eviction increments cumulative HistoryEvictions (saturating diagnostics), never
disconnects. Snapshot commit resets ring contents and rebuilds current generation
floors; it does not reset the connection LoadId counter or cumulative evictions.
An evicted/reset ancient ID reused by a bad host may be accepted as a new Spawn.
The host owns the non-reuse invariant; load attempt tokens still protect resources
from old callbacks. Missing-key non-Spawn gameplay messages are protocol errors,
not inferred from the largest allocated ID. Preparation keys are bounded by snapshot
actor caps; no resource-memory estimator is inferred from key counts.

## Pure cleanup scheduler

`CleanupModel` is explicitly pumped with a monotonic clock and remaining frame
budget; no active-session/authority guard. `TryAcquire` reserves one resident lease
before preparation, with steps supplied in actual unload execution order (normally
reverse load order) and a final return-to-pool callback. `Retire` immediately sets
visibility/gameplay flags false and queues heavy work. Actual Unity hide/unbind is
not integrated yet. Awaiting/retrying/quarantined leases all remain resident.

CleanupPolicy has explicit resident/backlog caps, preparation pause threshold,
step/frame and step/lease caps, soft time budget and bounded retries. Backlog cap
must cover every possible resident: one ticket per resident guarantees a full
snapshot retirement or Despawn can hide/track all existing objects even when new
preparation is paused. No untracked spill queue. Same key may have different leases;
ownership uses unique LeaseId, never the GameObject or entity key alone.

AdvanceFrame runs at least one runnable step with zero remaining budget; additional
steps respect the time/step cap. Synchronous work cannot be preempted. Jobs alternate
steps fairly, continue other cleanup after failure, retain failed steps for retry
and cannot call pool return until preceding cleanup succeeded. Callbacks must be
safe for retry/partial initialization; final pool callback must not release then
throw. Pool failure is also retained. BeginStop blocks admission and retires all;
the owner keeps pumping through Stopping/disconnect. Exhausted failures produce
Blocked plus quarantine, not abandoned objects. Explicit RetryCleanup can recover.

Still absent: production Prepared/Commit/directory guards; common commit-boundary
capture; per-frame/global capture and serializer schedulers; complete world and
subsystem state; byte-complete negotiated WirePolicyProfile; actual inbound-rate
limiter; coalescing; actual Unity object leases/quarantine; handshake wire serialization;
transport; save/reconnect-player policy. The larger contract's frame/goodput/memory
formula validators need measured profiles and belong to later integration. The
test policy checks the bounds needed for this executable model only.

## Verification

Shared `Assets/Tests/EveEngine/EveLifecycleModelChecks.cs` runs in real Unity and
the standalone smoke runner. It includes deterministic ordered simulations with
load delays, send refusal, replacements and final cleanup, plus direct fault cases.
Network reorder/loss is not injected above reliable ordered delivery.

`Assets/Tests/EveEngine/EveLifecycleCleanupChecks.cs` checks pure visibility,
accounting, budgets, fairness/progress, stop/disconnect, partial failures and pool
ordering using a fake clock. These assertions do not establish actual rendering
or Unity pool correctness before adapter integration.

Run `pwsh -NoProfile -File Tests/EveEngine/EveRunSmokeTests.ps1`. Native invoke:
`EveLifecycleModelChecks.Run()`. Read actual outputs in `.utmp/LifecyclePrototype`
and `.utmp/LifecycleSmokeFinal.txt`; historical PASS totals are not new evidence.
