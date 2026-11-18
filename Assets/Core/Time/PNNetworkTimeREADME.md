# Session network time (T1–T6)

`PNTimeService.For(session)` is attached once per play. Offline `PNGameSession` installs `PNLocalTimeline` with an explicit `Time.unscaledTimeAsDouble` delegate (the same Unity frame clock as Mirror localTime); network composition installs `PNMirrorTimeline`. Sample once at capture/apply boundaries. `Ready=false` prohibits interpreting stamps as a valid baseline. Client epoch is the admitted host play session id; before admission it is only a placeholder and unready. Epoch changes clear history. Stop rejects reads immediately; required session cleanup removes the service and releases history. No source is global gameplay state. Core has no implicit wall-clock fallback; pure tests inject a fake frame clock.

`PNTimeSample` supplies local monotonic seconds, estimated host-now, buffered presentation stamp and uncertainty, all unscaled double seconds. Mirror host uses localTime; observer uses unscaledTime minus the one configured additional buffer (.1s currently). Owner host-now estimate samples predictedTime minus half RTT with uncertainty half RTT under an assumed symmetric midpoint; never difference it for gameplay Advance. `PNOwnerImageAge.TryRead` accepts capture age within uncertainty and at most2s, clamps a small negative age to0; rejection requires baseline/rebase. Local prediction continues to use existing gameplay delta. Asymmetric-network acceptance remains T6 work.

`PNPresentationCursor` is independent of source corrections. Backward source samples yield zero Advance; initial/epoch samples establish baseline without elapsed replay. Explicit Rebase on a coherent resume baseline skips interrupted time. An unready sample does not advance the cursor. The caller must rebase at recovery; it must not feed the entire missed duration as gameplay catch-up.

`PNClockKey`: epoch + world/incarnation + domain + owner/generation for entity/instance domains. Instance owner is the manager's instance id. Anchors contain effective unscaled stamp, authoritative clock value, nonnegative rate and revision. Pause is rate0. Authority exports its already accumulated value; this service never advances authority simulation.

`PNClockHistory.Add(..., baseline:true)` starts/rebases one clock. Other updates require contiguous increasing revisions and nondecreasing effective stamps. Repeated identical revisions are idempotent; older superseded records do not reapply corrections. A missing revision or conflicting duplicate invalidates that clock and increments Recoveries: adapters request a state-at-capture baseline instead of guessing the missing interval. This is the explicit recovery fallback for reordered delivery, not an unordered transport implementation. Same-stamp updates select the highest revision. Queries before baseline return BeforeBaseline; future anchors within the bound remain queued until the requested stamp reaches them. Stamps beyond host-now+uncertainty+2s invalidate/request recovery. Invalid numbers/keys throw before insertion. Projection overflow throws.

History defaults:10s horizon,64anchors/clock,16384/session,2MiB charged retained capacity. Pruning requires the next anchor to be at/before the oldest consumer cursor AND older than horizon. If required history cannot be retained within bounds, invalidate that stream and recover; never merge missing rate segments. ChargedBytes conservatively reserves512bytes per retained Dictionary slot plus128bytes per allocated List element (including unused capacity); it is a reservation bound, not a measurement of GC heap/RSS or transient GC garbage. Reset replaces retained storage; Remove releases a clock list but retains Dictionary reservation until reset. No packet wire encoding is introduced byT1.

`PNTimeRecovery` defines one pending final ACK per epoch/attempt/digest, independent of heartbeat sequence. Begin closes readiness, Acknowledge retains the matching attempt, TryComplete rechecks applied-state readiness without requiring another ACK. A new attempt within the same interruption retains the original grace deadline; stale identities/digests do not set ACK. Reset discards pending ACK. Digest changes require a new attempt. The caller begins recovery at the actual interruption boundary, with epoch reset for a new session; retries cannot reset the interruption deadline. T2 integrates this with the manager: a matching final ACK stays pending while adapters/loaders/fences settle; LateUpdate rechecks it. A changed digest explicitly requests a fresh attempt within the original grace. Cleanup, pause and play reset discard the pending ACK.

## World and Move consumers (T2)

`PNWorldClock` in Coop exports the existing authority world clock at an unscaled capture stamp. Its scoped world ticket includes play, incarnation and subscription; the packet adds a contiguous revision, value, rate and baseline flag. Initial subscription, recovery fence and explicit baseline requests export a baseline. Global rate changes and world execution/pause changes export transitions. `EveWorldContext.ClockRestoreRevision` changes only on RestoreClock and forces a fresh baseline even if the rate did not change. No authority delta or accumulated clock is changed by the mapper.

World clock preparation precedes input readiness. It validates the authenticated batch's host stamp while the session sample is unready; it does not treat the unready sample as gameplay time. Missing history requests a bounded/coalesced baseline and closes world input. `ReadWorldPresentationClock` reads at the common presentation stamp only once ready. Queries before a new baseline return BeforeBaseline, with no backward extrapolation. Unsubscribe removes that clock; stale subscription/play packets cannot recreate it. World lifecycle and execution permission still gate immediately, independently of the buffered drawing clock.

Move pose/control/model timestamps use unscaled double seconds; send cadence uses local unscaled seconds. Observer drawing reads the service presentation stamp, so the configured .1s extra delay is applied once and remains .1 real seconds at any global scale. Owner motion and correction keep their existing gameplay delta and immediate input path. Move schema2 and NetworkVersion25 reject the older timestamp semantics.

## Buff manager consumer (T3)

`PNClockDomain.BuffGame` identifies the manager's game clock by play/world/incarnation and authoritative entity id/generation. Its automatic manager origin is zero at entity load; it stops during actor/world execution holds and component disable. It is distinct from the world's accumulated clock. Authority still accumulates the same gameplay delta/order. Individual Buff `Game/Entity/EntityWithoutMinimum` rates remain in `PNBuffWireState`, applied once after mapping the manager clock.

`PNClockProjection` reads the shared history. One binding owns a projection/key exclusively; other consumers reuse its reader rather than constructing or disposing another view owner. Prepare accepts authenticated anchors independently of input readiness; Read holds its previous value while the source is unready or history is missing. A coherent baseline explicitly seeks to its capture value, skipping interrupted elapsed. A render stamp before that baseline returns BeforeBaseline and holds the image, without backward extrapolation; once the cursor reaches it, mapping resumes. Backward source corrections cannot produce negative presentation advancement. `Prepared` and readable source time are different conditions. Dispose removes the owned clock history.

Buff packet schema2 (NetworkVersion26, fingerprint `buff3-clock2`) separates `Time` (state capture manager clock) and `HostTime` (unscaled capture stamp) from a revisioned `ClockStamp/ClockTime/Rate` anchor. Metadata is sampled before enqueue and repeated identically on every fragment. Unchanged linear anchors are reused; rate/value changes create contiguous anchors. Rate-change full state snapshots preserve observer history, while initial/fence/request baselines explicitly rebase. Owner full snapshots also seek coherently: a late pause/rate boundary must correct old-rate extrapolation instead of leaving Math.Max permanently ahead of authority. Producer SnapshotRate is tracked separately from anchor ClockRate, so a partial change carrying a new anchor cannot suppress the full rate snapshot. Optional cues never install anchors.

T4b selects the live Buff view stamp from the attached pose role: client Move Owner uses Host, Observer uses Presentation; a confirmed no-Move client replica uses Presentation. Client Local/unattached is unresolved, not a predicted-owner default. Authority Local/RemoteOwnerHost keep their existing Advance. Record release, projection ReadAt, history minimum-needed and optional-cue reads use the same binding policy. Owner Ability image/HUD reconciliation uses the separate T4 policy below. Initial/recovery images and required records during interruption prepare without waiting for input, so fence readiness cannot wait on itself. Optional cues are dropped while unready rather than replayed at resume. Required lifecycle/cues remain ordered. Missing history requests a new same-stream baseline using `ClockRequest`; required state continues to be consumed while clock/input hold. A successful baseline restores the binding gate. Its deadline is bounded by preparation timeout/policy grace and any original interruption deadline; retry does not extend it. Cleanup clears the manager delegate, projection and history.

## Owner Ability consumer (T4)

`PNSkillOwnerView` uses `PNOwnerImageAge` and three explicit AbilityGame/AbilityEntity/AbilityRaw keys. An owner image is a complete baseline: capture stamp/values/rates, passive rows, slot bindings and ACK/cast watermark are coherent before enqueue. `PNClockProjection.ReadAt(sample, sample.Host)` projects that bounded image; observer `Read` still selects Presentation. Preparation/Ready/readability remain separate. Each key has exactly one binding owner and is removed on unbind.

Authority Ability budgets retain their own zero origin, component/actor/world holds and existing Advance. Between images the owner authority view advances by local unscaled frame delta times captured authority rates; prediction keeps local gameplay budgets. Corrections happen at reconciliation, never through raw host estimate differences as simulation delta. The apply frame does not tick elapsed a second time. Scope undo/composition uses selected accumulated clocks and a separate authority image. See `Assets/Core/Entity/Shared/Ability/PNAbilityREADME.md` for payload, shadow/overlay, readiness, timeout and cleanup contracts.

NetworkVersion27 / skill_owner schema2 / RulesVersion169 (Mod API8 retained). Owner identity is the session's authenticated admission LocalOwnerId; PNSyncService.LocalOwnerId is a command sender default and is0 in the current game service even on clients. Observers must not start owner-image recovery timers. Ability age >2s or a future stamp outside uncertainty requests a fresh coherent image with a finite preparation/grace deadline; retries never restart it.

`IPNTimeRecoveryBudget` is a separate transport budget supplied by PNMirrorTimeline, read through `PNTimeService.RemainingRecoverySeconds`. It uses the actual selected mode's grace, the actual scheduler timeout and any remaining original interruption grace. The value is an unscaled interval, not a gameplay clock;0 means already expired. The service validates finite/nonnegative values and rejects a missing provider or stopped session. Native fake sources explicitly provide their test budget. A Game consumer must not substitute Config.Network defaults for the selected policy or start a fresh full grace after an earlier interruption.

## Buff view role transitions (T4b)

Coop owns PNBuffViewTimeline on the exact binding life; Core Buff/character authors do not select a network role. Initial publication and authenticated unready recovery prepare required state independently of Move attach/input readiness. Apply live role gates only after publication, or Buff and Move would wait on each other. An unready recovery baseline is a coherent seek that cancels the old cut candidate; it is not elapsed gameplay replay.

A live role change requests a coherent baseline on the existing ClockRequest stream. Older required records drain FIFO under the old view until that candidate reaches the head. The candidate then freezes the reader; Host→Presentation waits until the target reaches both the published unscaled-stamp high-water mark and the candidate capture stamp. A paused manager value is not a source-stamp boundary. Commit the candidate state/anchor and role at one cut; Presentation→Host seeks forward without replaying cues. Math.Max alone is not a role-switch policy. It also cannot reconcile an owner forecast that overshot a late pause: only a full coherent snapshot seeks the owner reader back to truth; partial anchors retain the old image until that snapshot. The binding records the last full-image CapturedRate for uncertainty conversion at rate transitions, rather than multiplying by a new zero rate. Required ending cues precede removal, remaining0 retains presence, and snapshots do not replay one-shots.

Cut/unresolved deadlines use the actual preparation/selected grace/remaining interruption budget, remain fixed across repeated role changes and expire even without queued records. A role cut does not extend admission grace. SetInput and the Move frame pump retain the Buff clock/view hold. Already-baselined peers continue to enqueue required changes while a new baseline is Requested, so the snapshot cannot replace required ending cues. Unhook removes role/candidate/delegate/history/leases. Wire remains Network27/schema2; Rules169/ModAPI8 are unchanged by this client-view policy. Evidence and test limits: Docs/NetworkTimeT4bReport.md. Prediction alloc/GC and asymmetric acceptance remain T6.

Action scheduling remains T5, with the T0 policy budget. T4 does not establish Action–pose alignment, asymmetric-network accuracy or load performance. Evidence: `Docs/NetworkTimeT4Report.md`.


T5.1 adds Action-local driver progress anchors, not a BuffGame/world alias. Host unscaled stamps order the run; progress already includes driver speed and selected gameplay scales. Full producer state remains shadow data alongside the current observer until T5.2 delivery and T5.3 scheduling. See PNSkillREADME's T5.1 section and Docs/NetworkTimeT5_1Report.md. Network28/Rules170, ModAPI8 (additive contracts).

## Action observation and integrated checks (T5.2–T6)

The Move observer selects `PNTimeSample.Presentation` and publishes the stamp
when it applies the pose in that frame. The Action observer reads the committed
model at that exact pose stamp. World and Buff observers also select
Presentation. An attached Move owner continues to read the Buff view at Host
and uses the bounded owner-image path for Ability. Rate changes and progress
anchors retain their own units, including hold at rate zero.

For Eve sweep with ActionReplication enabled, the observed view seeks to the
selected progress once lifecycle and run-progress coverage are sufficient.
Recovery may apply a baseline before the selected pose reaches its cut or before
the model covers the requested time. Record the receiver's apply event and
first covered draw with the same client clock. Separately record the distance
from the cut on Presentation. ActionReplication remains the default Eve sweep
observer path and service-off mode retains legacy replay.

The T6 Windows evidence includes repeat recovery, a same-frame pose/sweep/Buff
timer join, the T0 clock profiles and regression checks. See
`Docs/NetworkTimeT6Report.md` for scope, the resume slope miss, workload evidence
and device results.

## O1 Eve sweep scale

The Eve sweep Action definition declares `melee_scale`. At shape commitment,
the authority captures the scale and damage from the same combat source. The
melee hitbox service owns sampling, owner report checks, deduplication and
mode dispatch. The initial Eve slice used the optional `m` field of `skill_owner`.
The O1 object-spawn follow-up replaces it with the entity state
`project_n:skill_scales` (NetworkVersion 32). The owner freezes the latest
valid factor in its current scope for each prediction. Changes in flight
can leave prediction briefly behind the authority.

Action packet schema 3 adds an optional eighth Start context field for the
captured scale. The producer, optimized required encoder, baseline and
recovery census preserve it. Omission means 1. Observers seek and draw from
the captured value of each run. Legacy service-off replay uses neutral scale.
This slice uses NetworkVersion 31, RulesVersion project_n:171 and ModAPI 8.
