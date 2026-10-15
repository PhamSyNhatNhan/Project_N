# Lifecycle / Buff policy measurement — 120 FPS target

Opt-in Windows Mono Player fixture, RulesVersion67 / Mod API6 unchanged. This
adds no production defaults. Existing actor runtime is measured, not replaced.
Build with `PNLifecyclePolicyPlayerBuild.Build()` in clean idle Edit. Only a new
disposable scene and shader dependency materials are saved and subsequently removed;
authored scenes, build-scene list, Game/Simulator and Play Maximized are preserved.

The Player needs `-pn-lifecycle-policy -pn-output ABSOLUTE_FOLDER`. Use a fresh
output directory for each run; Samples.jsonl is append-only. Options:

| Option | Contract |
| --- | --- |
| `-pn-mode validate` | Short real host capture/client replay, retained snapshot and cleanup correctness gate |
| `-pn-mode baseline` | Empty instrumented Player, 3 × at least10 seconds/300 frames, last capped at120 |
| `-pn-mode micro` | 32/64/128/256 actors; 10 warmup + 300 measured Prepare/Commit/Unload cycles on reusable inactive objects |
| `-pn-mode screen` | Six content mixes × stable/timed/mixed × 0.5/1/2 ms; each steady case stops at 600 frames or 15 seconds |
| `-pn-mode full` | Selected case/pattern, 3 × 60-second steady runs; last run capped at 120 FPS, first two uncapped |
| `-pn-mode endurance` | 30 minutes, 64 actors, periodic snapshot and complete wave replacement/cleanup using new IDs |
| `-pn-mode combined` | 64/128/256 actors, 300/500/1000 synthetic projectiles, homing and boss sine, trigger/sweep; max 300 frames or 15 seconds each |
| `-pn-mode presentation` | Existing PNUiRuntime and registered elemental presenters, separate HUD/world targets, explicit render submission and pixel-validated captures |
| `-pn-case NAME` | `all`, `wave32`, `wave64`, `wave128`, `overload256`, `player60`, `one100` |
| `-pn-pattern NAME` | `all`, `stable`, `timed`, `mixed`; combined/endurance/validate use mixed |
| `-pn-peers N` | 0..3 host outboxes; default 3. Zero-peer control still runs local authoritative Buff/presentation code |

Four player actors carry 30 instances each (60 in player60, 100 in the isolated
one100 case). Remaining actors carry 5/10/20 instances as count rises. No Buff
gameplay cap is selected. Stable profiles use scoped attack_power modifiers and no
duration. Timed profiles derive the registered stat template; mixed profiles use
one tiny-damage Burn-derived DOT per three instances, half-second ticks, countdown,
stack and remove/apply bursts. Fixture profiles are registered only in the fixture
content module, preserve selected validators/factories, and have their own IDs.
Duration is 7200 seconds; DOT amount is 0.000001 to avoid retiring targets during
measurement. Profiles differ from final character builds; the content mix is explicit.

Actors include Stat, TimeScale, Control and Buff, plus Ability on the four players.
No enemy AI, Move/Skill runtime, full character prefab rendering or replicated HP
is implied. Production projectile networking remains deferred. Combined mode reuses
the existing collision World, fixed simulation 1/60 per tick, 2 collision steps,
16 motion pieces, Enter-only trigger callbacks, two glow quads and its finite life-reference buffer. The earlier collision fixture keeps Enter+Stay by default; this opt-in policy fixture switches its public test option off. Render at
120 FPS is not a 120-Hz physics promise. Trigger physics/query/motion/render costs
are synthetic and do not establish complete production projectile accuracy.

The shared snapshot freeze and serialization scheduler is actual runtime. Three
same-scope peers must share one freeze. Accepted transport bytes are released
immediately by a recorder, not measured reliable transport. Sequence and packets
are recorded then replayed in a separate Client session because session state is
singleton. Two snapshots verify retained actor data and full Buff count; client
ack is required. Prepare/cleanup budgeting is real, but separate runtime schedulers
are not represented as a newly implemented common production budget controller.
Atomic commit and capture remain synchronous and can exceed a soft trial budget.

## Measurement validity

Run on Player, not Editor. `-batchmode` without `-nographics` can remove desktop
presentation interference; record that flag and never call its throughput full-game
FPS. Rendered cases explicitly render to textures and require non-background pixels.
Do not compare a hidden-window wall-time result with visible rendering as if pacing
were the same. First-touch load samples remain separate; they are not cold disk/JIT
proof. Warmup ends after 120 frames or 10 seconds (short validate uses 10/2).
Screening cutoffs intentionally allow too few samples for p99 when a case is slow.

Stopwatch measures original dependency-ordered load/unload steps through a test-only
loader wrapper; normal commit hooks are retained. Whole Prepare includes recording
overhead; per-step timers end before appending records. No timed filesystem writes
occur inside a measured step; file flushes occur between steady phases/micro groups.
Instrumentation/record retention still costs CPU/memory and is not subtracted away.

FrameTiming Stats must already be enabled. Raw `cpuMainThreadFrameTime` is the
documented active-main-thread field; inclusive Main Thread and wall counters are
retained separately, never subtracted to invent an active baseline. See
[Unity counter reference](https://docs.unity3d.com/6000.0/Documentation/Manual/frame-timing-manager-counter-reference.html).
FrameTiming is delayed; summary discards the first8 transition samples and deduplicates frameStartTimestamp. GPU timing zero
in a scene without rendering is not evidence of an empty GPU budget in the game.
ProfilerRecorder GC Allocated In Frame is recorded only when valid, otherwise -1.
Per-thread allocation counter is calibrated; unsupported values are not reported as
zero. Normal Player may not expose the GC marker, requiring a separate diagnostic
build/trace before production GC limits can be validated. CoreCLR needs new runs.

## Artifacts and interpretation

Each run writes Provenance.json, Progress.json, Samples.jsonl, Result.json and Player
log. Failure.txt/Errors.txt are separate failure evidence; aborted attempts do not
count as successful measurements. Current Result success requires zero logged runtime
errors; completed/cleanup is separate. Summary also detects legacy Errors.txt and
refuses to call legacy completion a clean PASS. ErrorEvents.jsonl records phase,
frame and case. Raw phases separate create, load steps, Prepare,
commit essential/total, activation, freeze, serialize/final chunk work, peer wire
traffic, client replay and reverse cleanup. Later schema records native allocated
memory and Game deltaTime alongside managed heap/resident count; these are not a
complete native/GPU footprint or a proven producer bound. Real-time and game-time
byte rates are distinct when slow frames cause Unity deltaTime clamping.

The mixed generation replacement case exposes a runtime fault: old source IsReady
can remain true after a newer generation reserves the directory entry, while combat
requires the directory's current published binding. DOT source capture then throws.
Keep this as negative lifecycle evidence, not a clean PASS or ordinary steady cost.
Endurance uses new IDs for new waves and presentation measures rendering without
generation replacement, so those independent measurements do not hide this fault.
Screen/full still exercise the failing replacement path; an error-aware runner will
reject their overall result even when steady timing rows are individually usable.

Run `python Tests/EveEngine/SummarizeLifecyclePolicy.py RUN... --output REPORT_DIR`.
Nearest-rank p95 needs at least100 samples; p99 needs200. Smaller groups report only
the statistics their sample count supports. Frame distributions deduplicate timing
timestamps; no false zero for an unavailable marker. No observed maximum is a
mathematical upper bound. Do not choose world/payload/timeout production caps from
these test capacities (which are deliberately generous and written in the fixture).

120 FPS means 8.333 ms for the complete frame. Passing this scoped synthetic actor
fixture does not reserve the remaining budget for AI/shaders/projectiles. If current
actor replication already exceeds that frame budget, report the bottleneck before
changing gameplay Buff limits or choosing a production network policy.


## Optimization follow-up / fixture2 (RulesVersion68)

Follow-up output lives in .utmp/BuffHotPath. Fixture2 churn is tied to game-time:
stack changes every0.5s and one remove/apply per actor every1s. Earlier fixture1
used60/120 frames and its uncapped workload rate differs; do not call the two a
strict paired performance comparison. RulesVersion68 fixes the discovered source
and receiver presence fault. Errors still invalidate a run, even after completion.

Runner additions: -HotPaths $false disables inclusive opt-in EveHotPathMetrics
for confirmation; enabled samples report per-frame call counts and inclusive
Stopwatch durations (do not add nested timings as frame cost). -Stagger advances
initial actor clocks by0..0.49s for an explicitly labelled diagnostic comparison,
never for the mandatory aligned case. -Seconds selects duration for short diagnostic
runs; default full remains3x60s. -Popups $false uses a test-only PNEnemyStat subclass
to disable popup presentation, leaving damage/HP/Buff gameplay intact. Default keeps
real enemy damage popups. Popup-off evidence is isolation, not a full-frame PASS.

Mandatory target:64 actors/720 Buff instances at120FPS, host0/1/3 outboxes. Expected
scaling:wave128matched has128 actors/1360 instances (4x30+124x10); original wave128
has2600 instances and is a separate heavier case. Production
transport, AI, full renderer and projectile networking remain separate. Preserve
20-30 player Buff design load. Profiled/intermediate runs may overlap build/editor
activity; final confirmations must run one Player at a time with editor idle.

## Burst investigation / fixture3 (RulesVersion68 unchanged)

User authorized matched64 actor measurement, allocation tracing and a fresh30minute
endurance run; no production policy selection or gameplay optimization in this step.
RunSequence.ps1 under .utmp/BuffBurstReview documents the executed order. FrameCap120
applies to every repeat, rather than only repeat2. Four conditions are0/3 outboxes
x popup off/on. Labelled and reversed unlabelled cohorts remain separate.

Runner -Events enables scope counts without Stopwatch timing. Events.jsonl records
actual Unity frame IDs, game/real times, Update/Late timestamp bounds, calls and
bytes/messages. Count-only labels allocate and are diagnostic. Native FrameTiming
is delayed; its observer frame must not be treated as its measured frame. Mono
Stopwatch has a different origin despite equal frequency. The later native QPC
labels support joins only when exactly one event frame lies between adjacent native
timing starts; ambiguous intervals are excluded. Initial labels without native QPC
remain explicitly unjoined. AnalyzeBuffBurst.py preserves this distinction.

BuildDevelopment creates a separate Development executable. -Development
-AllocationTrace records AllocationTrace.raw with GC.Alloc callstacks and optional
inclusive scope allocation counters. Unsupported per-thread allocation probes are
unavailable, not zero. Development timings do not establish normal Player FPS.
PNBuffAllocationTraceReview.Extract performs opt-in offline main-thread callstack
analysis and restores existing editor profiler data. Loaded frame bounds and missing
callstacks are reported. Binary logs retain fuller evidence than aggregated groups.

Endurance still defaults to>=30minutes wall time, minute snapshots/new-ID waves,
with final lifecycle cleanup. FrameCap120 may be supplied. Report managed/native
memory observations, resident/backlog and latency trend; no whole-game/GPU guarantee.
Full matrix, trace, endurance and the latest actual checks go in HANDOFF.md.

RulesVersion69 measurements also record cumulative GC.CollectionCount per supported
generation in frame samples and event labels. Unsupported generations are-1. Counts
indicate collection activity, not GC duration or stop-the-world attribution. Native
timing joins still require QPC interval matching. Optional timed scopes distinguish
source capture, snapshot packet creation, incoming calculation and combat observer
notification; nested scopes are inclusive and must not be summed as exclusive cost.
Normal controls keep timed scopes/event labels off. The0/1/2/3-peer matrix shares
the same64actor720Buff workload; producer-only outboxes are not real network peers.

Fixture4 aligns its runtime/initial compatibility labels to policy_fixture:69 and
records SHA256 of loaded EveEngine/Core/Game assemblies before warmup. Older fixture3
BuffOptimize69 datasets retain their stale rules68 labels and are diagnostic history,
not the final provenance; raw files are not rewritten. Final-* and FinalScopes-*
carry corrected provenance. Intentionally interrupted Confirm-128 is excluded.
# Current: Fixture5 measurement diagnostics (2026-10-12)

RulesVersion69 / Mod API6 remain unchanged. Fixture5 adds nested exclusive metrics,
Capture origins and materialized-state Updated reasons, with per-frame ordered
event records. See `.utmp/MeasureStep1/MeasurementContract.md` and `Report.md` for
the actual matrix, source/assembly provenance, fresh evidence and limitations.
Diagnostic-off normal Player controls establish acceptance; timed/CountOnly and
Development allocation runs explain behavior and are not FPS acceptance evidence.
Exclusive scope values remove only instrumented children; parent residual includes
unmeasured work and instrumentation overhead. TraceAllocations alone does not
enable scopes; normal Mono per-thread allocation counters may be unavailable.

The original64/128 workload remains. `normal20` adds4players x30Buff and16enemies
x5DOT with200active projectiles; `normal6` adds4players x30Buff and2enemies x5DOT
with500active projectiles. Both require full/mixed,3peers,popupON. Player30 uses
the existing10DOT+20timed mix. Enemy reapply retains five DOTs. Projectile World
uses Trigger/Enter-only,two steps and independent8collision targets+wall, not
the Buff actors; no projectile damage/replication or full-game cost is established.
Model time advances1/60s per fixture frame as in the existing experiment.
Long additions use128history slots per projectile; existing World/State defaults
remain8. This increases contact-history storage, not active workload or lifetime.

