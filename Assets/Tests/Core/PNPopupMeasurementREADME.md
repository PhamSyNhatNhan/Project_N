# Popup measurement (Step 4.1)

Opt-in diagnostics only; no popup merging, display cap, cache, shared pool, codec or gameplay change.
Rules70/API6 remain unchanged. Runtime scopes use EveHotPathMetrics; the internal hook is visible
to Assembly-CSharp fixtures through a friend assembly, without adding a public popup API.

Run `Tests/EveEngine/RunLifecyclePolicyPlayer.ps1` with `-PopupDiagnostic` and a fresh Output.
Control: `-HotPaths $false` without Events. Timing: HotPaths true. CountOnly: HotPaths false,
Events true. Allocation: Development + AllocationTrace; profiler captures are separate evidence.
The snapshot/matrix used for this request lives in `.utmp/Step4a`.

Main workload: existing fixture6 natural wave64 (solo/3 outboxes, cohort40x2) and normal20
(3 outboxes, 200 actual projectiles, cohort16x3). Countdown stays enabled. Popup ON/OFF compares
presentation cost. Before/after code compares diagnostic-disabled code overhead, with popup ON.
Normal additions now also accept popup OFF to support this explicitly paired measurement.

Small fixture: Mode popup, 16 enemy receivers and four player entities, through PNStat.ReceiveDamage
and the authored prefab/pool. It schedules combat at .5s; it does not execute Buff DOT behavior or
replication. Low/diverse/repeat damage distributions share the same seeded offsets, target/hit-index
crit and element schedule. Packets explicitly set iframe duration0 to isolate popup presentation.
Each repeat has a16-hit cold-pool burst,120 warmup frames, then20s steady by default.
Pool cold is not necessarily asset/TMP cold after repeat0. Health and actual hit counts are checked.
Small fixture includes a real MainCamera/render target and a focused Player capture outside timing.
Main fixture6 has no MainCamera, so its Bloom path is absent. The idle warmup does not prewarm
all overlap capacity; early steady can instantiate a second popup per owner, recorded explicitly.

Samples.jsonl retains inclusive/exclusive milliseconds and inclusive managed scope bytes, plus
Window labels. Exclusive time removes only instrumented direct children; nested inclusive totals
must not be added. Advance/Render maintenance is outside combat.damage-popup (spawn).
FrameLabels.json derives actual DOT-hit deltas and AoE replacement frames from the audited workload.
The first frame delta lacks a preceding frame sample; whole-window audits remain authoritative.

PopupFrames.jsonl records shows, creates, returns, retire notifications, active/idle counts,
pool count and active peak sampled before/after Show/Advance. Pool destroy removes its gauge entry.
PopupStrings.jsonl is CountOnly diagnostic data in actual observation order, bounded per drain
(8Mi characters/100k rows, drain at10k rows); dropped data is explicit. No observer in Control.
Styles/assets remain authored and fixed during these runs. LRU simulation estimates exact string
reuse at16/64/256/1024 entries; it does not establish mesh reuse or arbitrary mutable-style safety.

Scope allocated bytes are current-thread managed allocations, inclusive of child allocations and
some diagnostic overhead. Unity profiler GC.Alloc stacks are separate Development captures.
Neither covers all native TMP/mesh memory, other threads, deferred Unity work or GPU cost.
On the measured Mono build the allocation probe is unavailable; zero scope bytes mean unavailable.
AllocationSummary estimates use stack ownership and approximate matched capture scope counts;
they include early-window growth and are not a fully warmed late-window allocation rate.
Profiler capture artifacts are labeled cold/steady in the small fixture; the main fixture captures
an early steady interval containing natural ticks and the first AoE alignment. Control is performance
evidence; diagnostic/profiler runs are attribution evidence.

Native PNPopupMeasurementBatch runs disabled/enabled popup assertions and direct parity, actual
URP HDR/Bloom render checks, then normal/Development builds in an isolated project copy.
The old popup native fixture now explicitly uses iframe0 because it tests popup formatting/reuse,
not the separate damage reduction contract. Actual results/counts are recorded, not fixed PASS totals.
