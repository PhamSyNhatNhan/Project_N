# Projectile architecture experiment

This is a disposable benchmark, not an implemented Projectile gameplay API.
RulesVersion64 / Mod API4 remain unchanged. No Action/Operation/content registration
or existing player/enemy behavior is modified.

## Purpose and compared backends

Every projectile is an independent pooled GameObject with its own body component:
position, direction, local age/clock, captured speed/radius, generation and hit mask.
The same motion and target-hit code runs in every backend.

- `ColliderKinematic`: sphere trigger plus kinematic, gravity-free Rigidbody.
- `Grid`: no projectile Collider/Rigidbody; a bounded XZ grid holds body-reference
  indices and per-body next links. Body state remains owned by the object.
- `ColliderOnlyControl`: sphere trigger without Rigidbody, a diagnostic comparison.

All targets retain native sphere colliders. The common hit algorithm samples the
projectile sphere four times per simulated1/60-second tick, using local PhysX
OverlapSphere. Target subcolliders resolve to a target ID and per-body hit mask.
This benchmark does NOT compare overlap versus sweep or guarantee no tunnelling.

The differing query path is how a moving destruction sphere/wind-wall volume finds
projectiles. Physics backend queries projectile colliders; grid backend retrieves
nearby references. Both run the same final geometric predicate and end behavior.
Per-fixture collider `excludeLayers` suppresses contact generation; query shapes
remain searchable. The global collision matrix/physics settings are not changed.

## Workloads

- Counts300/500/1000. Player profile is90% homing with limited turn rate and10%
  sine; boss profile follows an inward sine trajectory. Player profile targets12
  moving targets; boss profile targets one moving player proxy.
- Four local-time samples per tick. Permanent stopped projectiles and a TimeStop
  volume exercise clocks. A separate correctness case proves that the wind wall
  destroys a stopped projectile.
- Churn workload: lifetimes and moving destruction fields return/reborrow objects.
  Steady workload: retire effects are suppressed while motion/hit/area queries
  still run, isolating sustained movement without registration churn.
- CPU-only then identical VFX: two additive transparent procedural glow quads per
  body, shared mesh/material,1280x720 offscreen URP camera. Original game keeps
  running. This represents additional rendering load, not final projectile VFX.
- Two balanced repeats, reversing A/B and count order.30 warmup and120 measured
  frames per case. Main54 cases include six ColliderOnly controls. Steady32 cases
  include CPU at all counts and VFX at1000, without extra ColliderOnly controls.
- Optimized query-only controls repeat both churn and steady at1000 (16 cases
  each). They call SyncTransforms for each sample without simulating the local
  scene inside Tick. Native geometry/equivalence checks must pass on that path.
  Keep these separate from the initial four-Simulate baseline: the baseline is
  not the best achievable cost of a collider queried directly by gameplay code.

## Run safely

Read AGENTS.md and current HANDOFF first. Launch only from idle Edit with a clean
scene. The launcher refuses to save a dirty scene. No original scene save or
explicit reload, Game/Simulator selection change or Play Maximized modification.

Execute from Unity MCP using reflection if the tool does not reference the fixture
assembly directly:

```csharp
var type = AppDomain.CurrentDomain.GetAssemblies()
    .Select(a => a.GetType("PNProjectileArchitectureBenchmark"))
    .First(t => t != null);
var status = type.GetMethod("LaunchFromEditor").Invoke(null, null).ToString();
// For sustained-lifetime supplement: invoke LaunchSteadyFromEditor instead.
// Optimized1000 controls: LaunchQueryOnlyChurnFromEditor / LaunchQueryOnlySteadyFromEditor.
```

Both launchers enter Play. Native equivalence runs first: three backends are
compared per-body and per-event through240 ticks for both motion profiles, then
stopped destruction and retirement/pool membership are checked. The suite stops
on failure. Scene-local physics prevents queries from touching authored entities.
During correctness, native target overlaps are also compared with direct
sphere/sphere geometry at each sample; tiny threshold-boundary float cases are
excluded from that assertion. This checks sampled hits, not gaps between samples.
Benchmark worlds/cameras/render targets/mesh/material and pooled bodies are cleaned;
launcher exits Play and records scene/settings/object/console audit. Cancelling
Play early records cancellation rather than leaving stale PASS evidence.

`StartSuite(samples, warmup, outputFolder, steady, queryOnly)` is also a runtime entry point.
It can support a future disposable Player benchmark bootstrap. No standalone
Player build is created or verified by these Editor runs.

## Evidence and interpretation

Artifacts: `.utmp/ProjectileBenchmark` and its `Steady` subfolder:
optimized controls additionally use `QueryOnlyChurn` and `QueryOnlySteady`.

- `Correctness.txt`: native equivalence, stopped destruction, cleanup and an
  analytic sine example showing why equal-phase endpoint chords can miss a hit.
- `Environment.json`: CPU/GPU/Unity/configuration and sampling settings.
- `Summary.csv`, individual case CSVs: raw staged CPU timings and frame samples.
- `Result.txt` / `Progress.txt`: completion and current case. Only a final PASS
  means the full suite ran; presence of partial CSVs is not completion.
- `MeasuredParity.txt`: analyzer confirms hit/destroy/reuse/event digests match
  across all measured A/B/render/repeat cases at a given profile/count.
- Captured1000-projectile VFX frames; `RuntimeAudit.txt` and `FinalAudit.txt`.

Staged Tick timings cover movement, grid index, SyncTransforms/local simulation,
common target hit, projectile-volume query and pooling. They exclude rendering and
report writing. Step GC uses current-thread allocations around Tick. Frame GC,
Main Thread, wall-frame and available GPU FrameTiming include other game/editor
work. Unsupported recorder/timing fields use-1. Do not add overlapping metrics
together or treat GPU timing as the extra camera's isolated cost. Memory snapshots
are global process totals, not measured per-backend ownership.

The supplied analyzer under the review directory writes pooled p50/p95/p99 and
parity evidence. Editor/vsync/MCP/thermal noise limit interpretation; repeat on target
Player hardware before guaranteeing a frame budget. Rendering captures verify the
fixture's load, not production appearance. Geometry/index are a flat-arena prototype;
production 3D shapes, unbounded space, broadphase eligibility, stale handles, combat
snapshot/effects, full pooling/session API and replication still need design.
