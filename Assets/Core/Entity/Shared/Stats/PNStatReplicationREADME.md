# Stat-1 actor-stream replication (RulesVersion project_n:72)

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
