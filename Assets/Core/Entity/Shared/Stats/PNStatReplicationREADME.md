# Current: Mirror Stat + Buff integration (RulesVersion76 / Mod API6)

NetworkVersion3 adds Buff baseline/lifecycle alongside the typed Stat adapter below.
Catch-up coordination lives in Coop/PNStateCatchup.cs with fixed targets keyed by
adapter, actor lifetime, session and observer binding nonce. Stat full revision and
Buff revision are separate; empty Buff baseline at revision0 is still required.
Pending Buff transfers/DOM/journals share the global inbox byte/event budget with
Stat state and life notices. Required adapter absence never opens input.
See `Docs/MirrorP3Implementation.md` for actual evidence and platform limits.

## Mirror P2 Stat adapter foundation (RulesVersion74 / Mod API6)

Mirror network version2 now carries typed full Stat snapshots and required life notices.
PNStatProjection is the transport-neutral host revision/life owner. PNStatReplication
retains its public legacy facade, the client value view and typed CaptureState,
StageState/StageUpdate/ApplyState entry points. PNMoveConnections uses CaptureState
without encoding the old protocol. UseTypedTransport suppresses legacy delta publication
for Mirror actors; there is one source of lifeRevision and no second client Stat set.

CaptureState requires a published, drained authoritative entity. Buff capture readiness
is independent of its legacy publisher; fault is distinct from a temporarily busy
mutation. Full snapshots are immutable and coherent. ObserveLife records combat death
immediately; its resources are not a new numeric snapshot. A following full capture
advances the state revision without counting the same life transition twice. Noncombat
SetCurrent/max changes retain sampled transition semantics. There is no guarantee of
observing every transient transition between samples.

The current wire supports selected Everyone Resource definitions by canonical Stat ID,
up to128 entries,128 UTF8 bytes/ID,32768 bytes/frame, finite double current/max in [0,max].
Per-entity sets are established at load; custom characters/enemies can declare different
resources through content without a new shell field or serializer. Definition None is
preserved, including a selected Health override; an empty numeric baseline still has
life. Number and owner-only replication remain unsupported extensions requiring an
explicit contract/version change. No arbitrary object/JSON payload or automatic UI is added.

Coop PNActorShell serializes cached snapshots, never gameplay getters. Initial decode
reserves pending quota before gameplay binding; PNNetworkManager stages the latest full
snapshot and subsequent life notices before Ready. Typed inbox kinds share the global
byte/event limits. Each full replacement validates the complete selected resource set
before atomic apply. Client PNStat values and gameplay Died remain untouched; PNVitals
reads the view or returns Unknown. FullViewRevision tracks actual numeric replacements,
so a life-only notice cannot satisfy a full-state catch-up target.

Grace uses fixed per-attempt binding/revision fences. Missing required adapters cannot
settle; newer resume floors invalidate old challenges without extending the15s deadline.
P2 certifies lifecycle+Stat only; Buff/Move catch-up joins in P3/P4. Required client errors
close its session; authoritative projection failure ends the shared session. ConnectionFailed
is connected to transport closure/cleanup, with retry-safe teardown. Mirror package code
is unchanged. See local Docs/MirrorP2Plan.md and Docs/MirrorP2Implementation.md for evidence,
profile, limitations and later work. The following is historical legacy-transport context.

# Historical: Stat-1 actor-stream replication (RulesVersion project_n:72)

Host owns Health and gameplay death. PNStatReplication publishes immutable full resource
snapshots through project_n:stat, samples at 20 Hz unscaled LateUpdate and observes
existing Died immediately. Numeric state and life revisions are monotonically host-issued;
newer alive is representable. Non-combat transitions are sampled, not guaranteed immediate.
Capture/Freeze can refresh state but do not swallow pending publication to other peers.

Client holds a staged value view and never writes PNStat or raises Died. PNVitals reads
host/solo Stat or client view; Unknown until baseline. Incarnation/session are authenticated
by actor adapter and verified by the part; frozen bytes cannot read later live state.
Life-only notices avoid max/numeric reads inside a Buff mutation; the next drained
sample publishes the coherent pair. Baseline stages reject commit after dispose/unload.
The old Stat definition constructor is retained; new overloads add policy.
Full frames are version1, bounded to 128 resources, 128 UTF8 bytes per stat ID, 32768 body
bytes, finite double current/max with 0<=current<=max. Unknown sets, conflicts and malformed
payload reject without partial replacement. Snapshot recovery restores latest state.

Selected stat definitions expose Replication None(default)/Everyone/Owner. Owner is
explicitly unsupported and rejected until per-recipient filtering exists; Number only None.
Aliases/selected implementations control metadata. PN Health declares Everyone. JSON key
replication uses none/everyone/owner (owner currently rejected). Compatibility hashes
include the new public metadata. Client owner identity comes from trusted Coop.AdmitHost
host/compatibility/localOwnerId overload; legacy admission leaves identity unknown. UI
cannot supply authority/identity. No transport handshake is implemented here.

No remote popup source, best-effort admission, Move prediction or input rewrite is part
of Stat-1. Existing host popup/combat behavior is preserved. Stat-2 requires separate
per-peer optional-queue admission and direct assignment. Installed after clone/live checks and paired measurement; see HANDOFF and
.utmp/Step6/Stat1/Report.md for scoped evidence and limitations. This document does not claim a production transport,
120FPS, performance gain or verified visuals.
