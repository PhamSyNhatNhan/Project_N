# Lifecycle baseline fixture

Step 1 only: actual existing loader/module costs plus a pure protocol body sample.
No production loader, scene, Character Visual, iframe, UI or Projectile was changed.

`PNLifecycleBaselineReview.Run()` requires idle Edit and a stopped session. It builds
selected base content without enabled mods, creates a disposable preview scene and
25% player / 75% enemy actors at N=16/64 (N=1 player). Actor modules: Stat, Ability on
player, TimeScale, Control and headless Buff presentation. Load step measurement
wraps already dependency-ordered original components in a test-only loader subclass.
Do not use the instrumented subclass in gameplay.

Cases: N=1/16/64, 0/4 timed timescale Buffs per actor, first-touch and two reloads on
the same objects. The fixture explicitly advances activation in preview scenes;
preview Unity callbacks alone are insufficient to resume the loaded Buff collection.
First-touch does not mean cold disk/asset/JIT. Warm/cold Player profiles are future.

Outputs separate object creation, load/unload totals, original module steps,
directory/Buff capture, existing Buff JSON encoding, experimental actor-body encoding
and raw Buff message bursts. Embedded Buff envelopes repeat headers; these bytes
are a sample representation, not the final full-world schema. Delta samples include
the apply burst and one second of explicit advances; no coalescer is implemented.
2N control retains 128 distinct current-loader objects, then unloads all.

Allocation counters are calibrated with an actual allocation. Unavailable per-thread
counters are -1. Timing instrumentation overhead remains in totals. Output includes
content fingerprint, hardware, backend and content mix. Bounds cannot be selected
from three repeats or reused after changing content without remeasurement.

`PNLifecycleFrameBaselineReview.Launch()` enters Play in the existing clean scene,
creates a disposable additive runner, takes 180 frame samples after warmup and exits
back to Edit. It does not save or explicitly reload the authored scene, change
Game/Simulator, Play Maximized, Mouse, cameras, timescale, VSync or target FPS.
Main/wall timings are inclusive and can include waiting/editor work; keep the raw
markers, do not subtract overlapping timers to invent an active critical-path bound.
This frame sample is the authored scene baseline, not the headless actor swarm.

Artifacts: `.utmp/LifecyclePrototype/{NativeSamples,Provenance,PlayFrames}.json`,
`DeltaSamples.jsonl`, native results and frame audit. Run
`python Tests/EveEngine/SummarizeLifecycleBaseline.py` to validate sample structure
and produce `BaselineSummary.json` / `BaselineReport.md`.

The current three-sample cases are reported as first-touch n=1 and reload n=2,
min/max only. Reprocessing existing files is not a new measurement. Before selecting
any production limits, run a Player Mono fixture with separate startup/warmup,
hundreds of measured samples/case for p99, raw distributions, actual supported
ProfilerRecorder GC-per-frame marker and verified active main-thread traces.
Inclusive wall/main timings are not active frame work. CoreCLR requires new measurements.

Not measured yet: production Prepared/Commit, new snapshot boundary/freeze,
retained-state swap, global serialization pacing, actual transport/goodput/native
buffers, Player/CoreCLR, complete world representation, or high-load active frame
budget. Remeasure these after their actual implementations exist. No production
policy values were selected by this fixture.
