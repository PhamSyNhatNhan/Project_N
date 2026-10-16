using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using EveEngine;
using EveEngine.Experimental.Lifecycle;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

// Opt-in measurement only. No production policy or gameplay content is installed.
public sealed class PNPolicyTimedLoader : EveEntityLoader
{
    public Action<string, string, double> Record;
    private sealed class Step : EveIEntityLoadStep
    {
        private readonly EveIEntityLoadStep inner;
        private readonly PNPolicyTimedLoader owner;
        public Step(EveIEntityLoadStep inner, PNPolicyTimedLoader owner) { this.inner = inner; this.owner = owner; }
        public int LoadOrder => inner.LoadOrder;
        public IEnumerable<Type> Dependencies => inner.Dependencies;
        public void Load(EveEntityLoadContext context)
        { long t = Stopwatch.GetTimestamp(); try { inner.Load(context); } finally { owner.Record?.Invoke("load-step", inner.GetType().Name, PNPolicyClock.Elapsed(t)); } }
        public void Unload()
        { long t = Stopwatch.GetTimestamp(); try { inner.Unload(); } finally { owner.Record?.Invoke("unload-step", inner.GetType().Name, PNPolicyClock.Elapsed(t)); } }
    }
    protected override EveIEntityLoadStep[] OrderSteps(EveIEntityLoadStep[] steps)
        => base.OrderSteps(steps).Select(x => (EveIEntityLoadStep)new Step(x, this)).ToArray();
}
public static class PNPolicyClock
{
    public static double Now() => Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency;
    public static double Elapsed(long start) => (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
}
// Diagnostic control only: production popup code and authored settings are unchanged.
public sealed class PNPolicyStat : PNEnemyStat
{
    public bool Popups = true;
    public override bool CanShowDamagePopup => Popups && base.CanShowDamagePopup;
}
public sealed class PNPolicyRuntime : EveRuntime
{
    public EveSessionRole SelectedRole;
    public override EveSessionRole Role => SelectedRole;
    public static string FixtureRulesVersion => "policy_fixture:" + EveGameRuntime.CurrentRulesVersion.Split(':').Last();
    public override string RulesVersion => FixtureRulesVersion;
    protected override void Awake() { }
    protected override IEnumerable<EveIContentModule> CreateContentModules() => new EveIContentModule[] { new PNContentModule(), new PNPolicyContent() };
    protected override IEnumerable<EveContentDocument> ReadContentDocuments()
        => new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments();
}
public sealed class PNPolicyStableBuff : PNBuffBehavior
{
    public override bool EventDrivenReplication => true;
    private static readonly EveStatModifier modifier = PNStatModifierData.Parse(JObject.Parse("{'statId':'base:attack_power','group':1,'value':0.001}"));
    public override void OnApply(PNBuffContext context, JToken input) => OnResume(context);
    public override void OnResume(PNBuffContext context) => context.ModifyStats(new[] { modifier });
}
public sealed class PNPolicyContent : PNGameplayContentModule
{
    public static EveId BuffId(string pattern, int index) => EveId.Parse("policy_fixture:" + pattern + "_" + index);
    public override void Register(EveRegistryHub hub)
    {
        base.Register(hub);
        var stat = hub.Buffs().Get(EveId.Parse("project_n:buff_stat_template"));
        var dot = hub.Buffs().Get(EveId.Parse("project_n:burn"));
        // Derive never inherits the countdown descriptor, so the fixture opts in explicitly (launch flag) to allow
        // legacy-vs-opt-in comparison from one Player binary in the same session.
        bool countdown = Environment.GetCommandLineArgs().Contains("-pn-countdown") &&
            Environment.GetCommandLineArgs()[Array.IndexOf(Environment.GetCommandLineArgs(), "-pn-countdown") + 1] == "1";
        for (int i = 0; i < 100; i++)
        {
            hub.Buffs().Register(new PNBuffDefinition(BuffId("stable", i), EveId.Parse("policy_fixture:stable"), StableFactory, new JObject(),
                stat.Tags, validate: ValidateStable, showInUI: true));
            var timed = (JObject)stat.Payload; timed["duration"] = 7200; timed["value"] = .001; timed["maximumStacks"] = 3;
            var timedProfile = stat.Derive(BuffId("timed", i), timed, stat.Tags, false, 1, PNBuffInstanceScope.Target, true);
            hub.Buffs().Register(countdown ? timedProfile.WithCountdown(PNBuffCountdown.BottomRight) : timedProfile);
            var tick = (JObject)dot.Payload; tick["duration"] = 7200; tick["period"] = .5; tick["attackRatio"] = 0; tick["amount"] = .000001;
            var dotProfile = dot.Derive(BuffId("dot", i), tick, dot.Tags, false, 1, PNBuffInstanceScope.Source, true);
            hub.Buffs().Register(countdown ? dotProfile.WithCountdown(PNBuffCountdown.BottomRight) : dotProfile);
        }
    }
    private static PNBuffBehavior StableFactory(JToken data) => new PNPolicyStableBuff();
    private static void ValidateStable(JToken data, EveRegistryHub hub)
    { if (!(data is JObject obj) || obj.Count != 0) throw new FormatException("Empty measurement payload required."); hub.Stats.Get(PNContentIds.AttackPower); }
}

public sealed class PNLifecyclePolicyPlayer : MonoBehaviour
{
    private sealed class Case
    {
        public string Name;
        public int Actors, Players, Enemies, Bullets;
        public Case(string name, int actors, int players, int enemies, int bullets = 0) { Name = name; Actors = actors; Players = players; Enemies = enemies; Bullets = bullets; }
    }
    private sealed class Actor
    {
        public PNPolicyTimedLoader Loader;
        public PNBuffManager Buff;
        public PNStat Stat;
        public PNBuffHandle[] Handles;
        public PNBuffSource Source;
        public long Id;
        // Natural pattern: per-actor schedule so apply and churn phases follow actual spawn/apply times.
        public double NextStack, NextReapply;
        public int StackToggle;
        public double[] ApplyAt;
        public string[] ApplyKind;
        public PNPolicyIntegrity Integrity;
        public int[] AoeSlots;
    }
    public sealed class Row
    {
        public string Case, Pattern, Phase, Step;
        public string Window;
        public int Repeat, Sample, Frame, Actors, Instances, Resident, Backlog;
        public double BudgetMs, Ms, WallMs, MainInclusiveMs, WaitMs, CpuFrameMs, GpuFrameMs, ActiveMainMs, PresentWaitMs, ActiveRenderMs, GameDeltaMs;
        public ulong TimingTimestamp;
        public bool AllocationRecording;
        public double ExclusiveMs;
        public long ScopeAllocatedBytes;
        public long GcBytes, WireBytes, Messages, ManagedBytes, NativeAllocatedBytes;
        public int ActualInstances, ActualDotInstances; public long ActualDotHits, ActualDotCues;
        public int GcCollections0, GcCollections1, GcCollections2;
    }
    private readonly List<Row> rows = new List<Row>(16384);
    private readonly List<GameObject> roots = new List<GameObject>();
    private readonly List<Actor> live = new List<Actor>();
    private readonly List<EveActorHostConnection> peers = new List<EveActorHostConnection>();
    private EveRegistryHub hub;
    private PNPolicyRuntime runtime;
    private EveSessionContext sessionContext;
    private EveContentCompatibility compatibility;
    private ModelPolicy wirePolicy;
    private string output, mode, selectedCase = "all", selectedPattern = "all", caseName = "startup", pattern = "none";
    private int repeat, sample, totalInstances, actorCount;
    private int peerCount = 3;
    private long nextId = 100000, wireBytes, messages;
    private double budgetMs = 1, frameWorkStart;
    private bool recording = true;
    private bool hotPaths = true;
    private bool stagger;
    private bool popups = true;
    private double selectedSeconds;
    private int naturalSeed = 20261012;
    private bool countdownOptIn;
    // AoE cohort (natural pattern only): every aoePeriod game seconds, aoeTargets enemies get aoeDepth DOT instances
    // replaced in the same frame, so their ticks realign (a skill hitting a group). 0 targets disables it.
    private int aoeTargets, aoeDepth = 2;
    private double aoePeriod = 2;
    private readonly List<object> naturalLog = new List<object>();
    private double naturalPopulateSeconds;
    private int frameCap;
    private bool eventLabels, allocationTrace;
    private bool popupDiagnostic;
    private bool measurementCameraOptIn;
    private GameObject measurementCameraRoot;
    private RenderTexture measurementCameraTexture;
    private string popupDamage = "low";
    private PNPopupMeasurementProbe popupProbe;
    private bool traceCaptured;
    private double traceStarted;
    private long frameStarted;
    private long nativeFrameStarted;
    [DllImport("kernel32.dll")]
    private static extern bool QueryPerformanceCounter(out long value);
    private readonly List<object> eventRows = new List<object>();
    private int retainedDiagnosticEvents;
    private long droppedDiagnosticEvents;
    private ProfilerRecorder gc, main, wait;
    private FrameTiming[] timing = new FrameTiming[1];
    private readonly List<List<byte[]>> snapshots = new List<List<byte[]>>();
    private Guid recordedSession;
    private bool allocationAvailable;
    private int assertions;
    private int integrityInstances, integrityDots;
    private long integrityHits, integrityCues;
    private bool done;
    private int runtimeLogErrors;
    private string stage = "startup";
    private PNProjectileCollisionPlayerBenchmark.World projectiles;
    private Camera presentationCamera;
    private RenderTexture presentationTexture;
    private RenderTexture hudTexture;
    private PNUiRuntime presentationUI;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        var args = Environment.GetCommandLineArgs(); if (!args.Contains("-pn-lifecycle-policy")) return;
        var root = new GameObject("Disposable lifecycle policy measurement");
        var runner = root.AddComponent<PNLifecyclePolicyPlayer>();
        runner.output = Option(args, "-pn-output", Path.Combine(Application.persistentDataPath, "LifecyclePolicy"));
        runner.mode = Option(args, "-pn-mode", "screen");
        runner.selectedCase = Option(args, "-pn-case", "all"); runner.selectedPattern = Option(args, "-pn-pattern", "all");
        runner.peerCount = int.Parse(Option(args, "-pn-peers", "3"), CultureInfo.InvariantCulture);
        runner.hotPaths = Option(args, "-pn-hotpaths", "1") == "1";
        runner.stagger = Option(args, "-pn-stagger", "0") == "1";
        runner.popups = Option(args, "-pn-popups", "1") == "1";
        runner.selectedSeconds = double.Parse(Option(args, "-pn-seconds", "0"), CultureInfo.InvariantCulture);
        runner.frameCap = int.Parse(Option(args, "-pn-cap", "0"), CultureInfo.InvariantCulture);
        runner.eventLabels = Option(args, "-pn-events", "0") == "1";
        runner.naturalSeed = int.Parse(Option(args, "-pn-seed", "20261012"), CultureInfo.InvariantCulture);
        runner.countdownOptIn = Option(args, "-pn-countdown", "0") == "1";
        runner.aoeTargets = int.Parse(Option(args, "-pn-aoe", "0"), CultureInfo.InvariantCulture);
        runner.aoeDepth = int.Parse(Option(args, "-pn-aoe-depth", "2"), CultureInfo.InvariantCulture);
        runner.aoePeriod = double.Parse(Option(args, "-pn-aoe-period", "2"), CultureInfo.InvariantCulture);
        runner.allocationTrace = Option(args, "-pn-allocation-trace", "0") == "1";
        runner.popupDiagnostic = Option(args, "-pn-popup-diagnostic", "0") == "1";
        runner.measurementCameraOptIn = Option(args, "-pn-camera", "0") == "1";
        runner.popupDamage = Option(args, "-pn-popup-damage", "low");
    }
    private static string Option(string[] args, string key, string fallback)
    { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
    private IEnumerator Start()
    {
        Directory.CreateDirectory(output);
        Application.runInBackground = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
        Application.logMessageReceived += Log;
        EveHotPathMetrics.Enabled = hotPaths;
        EveHotPathMetrics.CountOnly = eventLabels;
        EveHotPathMetrics.TraceAllocations = allocationTrace;
        if (popupDiagnostic && EveHotPathMetrics.Active) popupProbe = new PNPopupMeasurementProbe(output, eventLabels);
        if (allocationTrace)
        {
            if (!UnityEngine.Debug.isDebugBuild) throw new InvalidOperationException("Allocation trace requires Development Player.");
            UnityEngine.Profiling.Profiler.logFile = Path.Combine(output, "AllocationTrace.raw");
            UnityEngine.Profiling.Profiler.enableBinaryLog = true;
            UnityEngine.Profiling.Profiler.enableAllocationCallstacks = true;
            UnityEngine.Profiling.Profiler.enabled = false;
        }
        gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
        main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
        wait = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "WaitForTargetFPS", 1);
        var run = Run(); Exception failure = null;
        while (true)
        {
            bool more = false; object current = null;
            try { more = run.MoveNext(); if (more) current = run.Current; }
            catch (Exception error) { failure = error; }
            if (failure != null || !more) break;
            yield return current;
        }
        if (failure != null) File.WriteAllText(Path.Combine(output, "Failure.txt"), failure.ToString());
        try { foreach (var a in live) a.Integrity?.Dispose(); StopPresentation(); projectiles?.Dispose(); projectiles = null; foreach (var peer in peers) peer.Dispose(); peers.Clear(); if (EveRegistrySession.State != EveSessionState.Stopped) runtime?.EndSession(); }
        catch (Exception error) { File.AppendAllText(Path.Combine(output, "Failure.txt"), "\nCleanup: " + error); failure = error; }
        for (int i = 0; i < 10000 && EveRegistrySession.State == EveSessionState.Stopping; i++) { EveRegistrySession.PumpCleanup(); yield return null; }
        foreach (var root in roots) if (root != null) Destroy(root); roots.Clear(); yield return null;
        Flush(); EveHotPathMetrics.Enabled = false; EveHotPathMetrics.CountOnly = false; EveHotPathMetrics.TraceAllocations = false; EveHotPathMetrics.Drain(); EveHotPathMetrics.DrainEvents(); gc.Dispose(); main.Dispose(); wait.Dispose();
        if (allocationTrace) { UnityEngine.Profiling.Profiler.enabled = false; UnityEngine.Profiling.Profiler.enableBinaryLog = false; }
        File.WriteAllText(Path.Combine(output, "Result.json"), JsonConvert.SerializeObject(new { success = failure == null && runtimeLogErrors == 0 && EveRegistrySession.State == EveSessionState.Stopped,
            completed = failure == null, runtimeLogErrors, mode, assertions, session = EveRegistrySession.State.ToString(), remainingActors = FindObjectsByType<PNPolicyTimedLoader>(FindObjectsSortMode.None).Length,
            remainingProjectiles = FindObjectsByType<PNProjectileCollisionBody>(FindObjectsSortMode.None).Length, productionPolicySelected = false }, Formatting.Indented));
        Application.logMessageReceived -= Log; done = true; Application.Quit(failure == null ? 0 : 1);
        popupProbe?.Dispose(); popupProbe = null;
    }
    private void Log(string message, string stack, LogType type)
    {
        if (type != LogType.Exception && type != LogType.Error) return;
        runtimeLogErrors++;
        File.AppendAllText(Path.Combine(output, "Errors.txt"), message + "\n" + stack + "\n");
        File.AppendAllText(Path.Combine(output, "ErrorEvents.jsonl"), JsonConvert.SerializeObject(new { caseName, pattern, repeat, sample, stage, frame = Time.frameCount, message, stack }) + "\n");
    }
    private void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); assertions++; }
    private void Progress(string phase)
    { stage = phase; if (popupProbe != null) popupProbe.Phase = recording ? phase : "warmup"; File.WriteAllText(Path.Combine(output, "Progress.json"), JsonConvert.SerializeObject(new { mode, caseName, pattern, repeat, sample, phase, budgetMs, time = DateTime.UtcNow, assertions, runtimeLogErrors })); }
    private void Add(string phase, double ms, string step = "", long bytes = 0, long count = 0)
    {
        if (!recording) return;
        var cleanup = sessionContext?.Cleanup;
        rows.Add(new Row { Case = caseName, Pattern = pattern, Phase = phase, Step = step, Window = stage, Actors = actorCount, Instances = totalInstances,
            Repeat = repeat, Sample = sample, Frame = Time.frameCount, BudgetMs = budgetMs, Ms = ms, WireBytes = bytes, Messages = count,
            Resident = cleanup?.ResidentCount ?? 0, Backlog = cleanup?.Scheduler.BacklogCount ?? 0 });
    }
    private double Measure(string phase, Action work)
    { long t = Stopwatch.GetTimestamp(); work(); double ms = PNPolicyClock.Elapsed(t); Add(phase, ms); return ms; }
    private void Flush()
    {
        using (var writer = new StreamWriter(Path.Combine(output, "Samples.jsonl"), true)) foreach (var row in rows) writer.WriteLine(JsonConvert.SerializeObject(row));
        rows.Clear();
        using (var writer = new StreamWriter(Path.Combine(output, "Events.jsonl"), true)) foreach (var row in eventRows) writer.WriteLine(JsonConvert.SerializeObject(row));
        eventRows.Clear();
        retainedDiagnosticEvents = 0;
        roots.RemoveAll(x => x == null);
    }
    private void Begin(EveSessionRole role)
    {
        Check(EveRegistrySession.State == EveSessionState.Stopped, "Previous session not stopped.");
        if (runtime == null) runtime = new GameObject("Fixture-only content runtime").AddComponent<PNPolicyRuntime>();
        runtime.SelectedRole = role; runtime.Initialize();
        var session = sessionContext = EveRegistrySession.Context; hub = session.Registry; compatibility = session.Compatibility;
        session.ConfigureCleanup(new CleanupPolicy(2048, 2048, 256, 32, 32, budgetMs, 3, 1000), PNPolicyClock.Now,
            () => Math.Max(0, budgetMs - (PNPolicyClock.Now() - frameWorkStart)));
        if (role == EveSessionRole.Host) session.ConfigureSnapshots(8, new EveEntityPreparationBudget(4, 32, budgetMs), PNPolicyClock.Now);
    }
    private Actor Create(int index, long id = 0, long generation = 1)
    {
        long t = Stopwatch.GetTimestamp();
        var root = new GameObject("Policy actor " + index); root.SetActive(false); roots.Add(root);
        root.transform.position = new Vector3(index % 16 - 8, 0, index / 16 - 8);
        long assignedId = id == 0 ? ++nextId : id;
        root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(assignedId), EveId.Parse(index < 4 ? "project_n:eve" : "project_n:dummy"), index < 4 ? index + 1 : 0, generation);
        root.AddComponent<EveTimeScale>(); root.AddComponent<CapsuleCollider>();
        PNStat stat = index < 4 ? (PNStat)root.AddComponent<PNPlayerStat>() : root.AddComponent<PNPolicyStat>();
        if (stat is PNPolicyStat measured) measured.Popups = popups;
        if (index < 4) root.AddComponent<PNAbilityManager>();
        var buff = root.AddComponent<PNBuffManager>(); buff.RenderPresentation = mode == "presentation";
        // Explicit fixture scheduling avoids automatic and manual advancement in the same frame.
        buff.enabled = false;
        var loader = root.AddComponent<PNPolicyTimedLoader>(); loader.Record = (phase, step, ms) => Add(phase, ms, step);
        Add("create-object", PNPolicyClock.Elapsed(t)); return new Actor { Loader = loader, Buff = buff, Stat = stat, Id = assignedId };
    }
    private IEnumerator Prepare(Case c, List<Actor> group, long generation = 1, List<Actor> previous = null)
    {
        double begin = PNPolicyClock.Now(); int index = 0;
        while (index < c.Actors)
        {
            frameWorkStart = PNPolicyClock.Now(); int created = 0, steps = 0;
            while (index < c.Actors && steps < 32 && (steps == 0 || PNPolicyClock.Now() - frameWorkStart < budgetMs))
            {
                if (group.Count <= index)
                {
                    if (created >= 4 || !EveRegistrySession.Context.Cleanup.CanBeginPreparation) break;
                    var actor = Create(index, previous == null ? 0 : previous[index].Id, generation); group.Add(actor);
                    Measure("begin-prepare", () => actor.Loader.BeginPrepare(hub));
                    Check(EveRegistrySession.Context.Cleanup.Track(actor.Loader, () => Destroy(actor.Loader.gameObject)), "Resident admission."); created++;
                }
                var selected = group[index]; Measure("prepare-next", () => selected.Loader.PrepareNext()); steps++;
                if (selected.Loader.State == EveEntityLoadState.Prepared) index++;
            }
            Add("prepare-frame", PNPolicyClock.Now() - frameWorkStart);
            if (created == 0 && steps == 0 && index < c.Actors) { Check(EveRegistrySession.Context.Cleanup.Scheduler.BacklogCount > 0, "Prepare starvation without cleanup backpressure."); Add("prepare-backpressure-wait", PNPolicyClock.Now() - frameWorkStart); }
            Check(PNPolicyClock.Now() - begin < 600000, "Measurement prepare deadline exceeded.");
            yield return null;
        }
        Add("prepare-latency", PNPolicyClock.Now() - begin);
        Measure("commit-total", () => EveEntityLoader.CommitBatch(group.Select(x => x.Loader)));
        Add("commit-essential", EveRegistrySession.Context.LastCommitMilliseconds);
        Measure("activate", () => { foreach (var actor in group) { actor.Loader.gameObject.SetActive(true); actor.Buff.enabled = true; actor.Buff.Advance(0); actor.Buff.enabled = false; } });
        // Buff disabled would suspend effects; use a separate manual driver below, enabling only inside each invocation.
        foreach (var actor in group) { actor.Buff.enabled = true; }
        Check(group.All(x => x.Loader.IsReady), "Whole wave Ready.");
    }
    private static string KindFor(Case c, int actorIndex, int buffIndex, string selectedPattern)
        => c.Bullets > 0 && actorIndex >= 4 ? "dot" : selectedPattern == "stable" ? "stable"
            : (selectedPattern == "mixed" || selectedPattern == "natural") && buffIndex % 3 == 0 ? "dot" : "timed";
    // Natural pattern: same Buff composition as mixed, but each instance is applied at a precomputed seeded time
    // (actor spawn offset + instance offset) instead of one burst. Gameplay ticks are not moved: each DOT schedules
    // its own ticks from the moment it is applied. The schedule is built before the run from the seed only.
    private void PrepareNatural(Case c, List<Actor> group)
    {
        totalInstances = 0;
        var plan = PNPolicyFixturePlan.Natural(naturalSeed, group.Count, 4, c.Players, c.Enemies);
        for (int i = 0; i < group.Count; i++)
        {
            var actor = group[i]; actor.Source = PNBuffSource.Capture(actor.Stat); int count = i < 4 ? c.Players : c.Enemies;
            var scheduled = plan[i];
            actor.Handles = new PNBuffHandle[count]; actor.ApplyAt = new double[count]; actor.ApplyKind = new string[count];
            for (int j = 0; j < count; j++)
            { actor.ApplyAt[j] = scheduled.ApplyAt[j]; actor.ApplyKind[j] = KindFor(c, i, j, "natural"); }
            actor.StackToggle = 0; actor.NextStack = scheduled.StackPhase; actor.NextReapply = scheduled.ReapplyPhase;
            actor.Integrity = new PNPolicyIntegrity(actor.Buff, count, j => actor.ApplyKind[j]);
        }
    }
    private IEnumerator Populate(Case c, List<Actor> group)
    {
        Progress("natural-populate");
        var pending = new List<(Actor actor, int index, double at)>();
        foreach (var actor in group) for (int j = 0; j < actor.ApplyAt.Length; j++) pending.Add((actor, j, actor.ApplyAt[j]));
        pending = pending.OrderBy(x => x.at).ThenBy(x => x.actor.Id).ThenBy(x => x.index).ToList();
        double elapsed = 0, settleUntil = double.PositiveInfinity; int next = 0, frames = 0;
        while (true)
        {
            elapsed += Time.deltaTime; frames++;
            while (next < pending.Count && elapsed >= pending[next].at)
            {
                var item = pending[next++]; var actor = item.actor;
                var receipt = actor.Buff.Buffs.Apply(new PNBuffApplyRequest(PNPolicyContent.BuffId(actor.ApplyKind[item.index], item.index), actor.Source));
                Check(receipt.Handle.IsAlive && receipt.Error == null, "Natural apply rejected: " + receipt.Message + " " + receipt.Error);
                actor.Handles[item.index] = receipt.Handle; totalInstances++;
                naturalLog.Add(new { actor = actor.Id, index = item.index, kind = actor.ApplyKind[item.index], planned = item.at, actual = elapsed, frame = frames });
            }
            Drain(false);
            if (next >= pending.Count)
            {
                if (double.IsPositiveInfinity(settleUntil)) settleUntil = elapsed + 1; // let the last DOTs reach steady scheduling
                else if (elapsed >= settleUntil) break;
            }
            Check(elapsed < 120, "Natural population did not finish.");
            yield return null;
        }
        naturalPopulateSeconds = elapsed;
        File.WriteAllText(Path.Combine(output, c.Name + "-NaturalSchedule-" + repeat + ".json"),
            JsonConvert.SerializeObject(new { seed = naturalSeed, populateSeconds = elapsed, instances = naturalLog }));
        naturalLog.Clear();
    }
    private void Apply(Case c, List<Actor> group, string selectedPattern)
    {
        if (selectedPattern == "natural") { PrepareNatural(c, group); return; }
        totalInstances = 0;
        Measure("apply-burst", () =>
        {
            for (int i = 0; i < group.Count; i++)
            {
                var actor = group[i]; actor.Source = PNBuffSource.Capture(actor.Stat); int count = i < 4 ? c.Players : c.Enemies;
                actor.Handles = new PNBuffHandle[count];
                actor.Integrity = new PNPolicyIntegrity(actor.Buff, count, j => KindFor(c, i, j, selectedPattern));
                for (int j = 0; j < count; j++)
                {
                    string kind = KindFor(c, i, j, selectedPattern);
                    var receipt = actor.Buff.Buffs.Apply(new PNBuffApplyRequest(PNPolicyContent.BuffId(kind, j), actor.Source));
                    Check(receipt.Handle.IsAlive && receipt.Error == null, "Buff apply rejected: " + receipt.Message + " " + receipt.Error);
                    actor.Handles[j] = receipt.Handle; totalInstances++;
                }
            }
        });
    }
    private void Connect(List<Actor> group)
    {
        Check(peerCount >= 0 && peerCount <= 3, "Peer count must be 0..3.");
        for (int i = 1; i <= peerCount; i++)
        {
            var peer = new EveActorHostConnection(EveRegistrySession.Context, i, wirePolicy, PNPolicyClock.Now, compatibility); peers.Add(peer);
            Measure("track-peer", () => { foreach (var actor in group) peer.Track(actor.Loader); });
            peer.CaptureSnapshot();
        }
    }
    private void StartPresentation()
    {
        presentationTexture = new RenderTexture(1280, 720, 24); presentationTexture.Create();
        hudTexture = new RenderTexture(1280, 720, 0); hudTexture.Create();
        var root = new GameObject("Owned policy presentation camera"); root.tag = "MainCamera";
        presentationCamera = root.AddComponent<Camera>(); presentationCamera.orthographic = true; presentationCamera.orthographicSize = 12;
        presentationCamera.transform.SetPositionAndRotation(new Vector3(0, 12, -20), Quaternion.LookRotation(new Vector3(0, -12, 20)));
        presentationCamera.clearFlags = CameraClearFlags.SolidColor; presentationCamera.backgroundColor = new Color(.04f, .06f, .08f); presentationCamera.targetTexture = presentationTexture;
        presentationCamera.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Base;
        presentationUI = new GameObject("Owned existing combat UI fixture").AddComponent<PNUiRuntime>();
        presentationUI.Bind(EveRegistrySession.Context); presentationUI.ViewCamera = presentationCamera; presentationUI.LocalOwnerId = 1; presentationUI.LocalPlayerId = live[0].Stat.EntityId;
        foreach (var document in presentationUI.GetComponentsInChildren<UIDocument>()) if (document.panelSettings.renderMode != PanelRenderMode.WorldSpace) document.panelSettings.targetTexture = hudTexture;
        presentationUI.RefreshUI();
        Check(presentationUI.HudRoot.Q("player-buffs").childCount == live[0].Handles.Length, "Player Buff UI count.");
    }
    private void CapturePresentation()
    {
        RenderPresentation();
        var before = RenderTexture.active; var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try { RenderTexture.active = presentationTexture; image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            Check(image.GetPixels32().Count(x => x.r > 80 || x.g > 80 || x.b > 80) > 100, "Presentation rendered pixels.");
            File.WriteAllBytes(Path.Combine(output, caseName + "-" + budgetMs.ToString(CultureInfo.InvariantCulture) + "-world.png"), image.EncodeToPNG());
            RenderTexture.active = hudTexture; image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            Check(image.GetPixels32().Count(x => x.r > 80 || x.g > 80 || x.b > 80) > 100, "HUD rendered pixels.");
            File.WriteAllBytes(Path.Combine(output, caseName + "-" + budgetMs.ToString(CultureInfo.InvariantCulture) + "-hud.png"), image.EncodeToPNG()); }
        finally { RenderTexture.active = before; Destroy(image); }
    }
    private void RenderPresentation()
    {
        if (presentationCamera == null) return;
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null) presentationCamera.Render();
        else UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(presentationCamera, new UniversalRenderPipeline.SingleCameraRequest { destination = presentationTexture });
    }
    // Diagnostic-only MainCamera (opt in with -pn-camera): static, culls nothing, renders to a tiny target. It exists so Camera.main
    // resolves like in a scene with a main camera; cases with and without it are never compared for savings.
    private void StartMeasurementCamera()
    {
        if (measurementCameraRoot != null) return;
        measurementCameraTexture = new RenderTexture(16, 16, 0); measurementCameraTexture.Create();
        measurementCameraRoot = new GameObject("Owned measurement MainCamera"); measurementCameraRoot.tag = "MainCamera";
        var camera = measurementCameraRoot.AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = 12; camera.cullingMask = 0;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.targetTexture = measurementCameraTexture;
        camera.transform.SetPositionAndRotation(new Vector3(0, 12, -20), Quaternion.LookRotation(new Vector3(0, -12, 20)));
    }
    private void StopPresentation()
    {
        if (measurementCameraRoot != null) { var camera = measurementCameraRoot.GetComponent<Camera>(); camera.enabled = false; camera.targetTexture = null; Destroy(measurementCameraRoot); measurementCameraRoot = null; }
        if (measurementCameraTexture != null) { measurementCameraTexture.Release(); Destroy(measurementCameraTexture); measurementCameraTexture = null; }
        if (presentationUI != null) { presentationUI.enabled = false; Destroy(presentationUI.gameObject); presentationUI = null; }
        if (presentationCamera != null) { presentationCamera.enabled = false; presentationCamera.targetTexture = null; Destroy(presentationCamera.gameObject); presentationCamera = null; }
        if (presentationTexture != null) { presentationTexture.Release(); Destroy(presentationTexture); presentationTexture = null; }
        if (hudTexture != null) { hudTexture.Release(); Destroy(hudTexture); hudTexture = null; }
    }
    private void Drain(bool capture, List<byte[]> packets = null)
    {
        foreach (var peer in peers)
        {
            int guard = 10000;
            while (peer.Send(16, batch => { foreach (var bytes in batch) { wireBytes += bytes.Length; messages++; if (capture && ReferenceEquals(peer, peers[0])) packets?.Add(bytes); } return true; }))
            { peer.Protocol.ReleaseAccepted((int)peer.Protocol.AcceptedBytes); Check(--guard > 0, "Send loop."); }
            Check(peer.Protocol.Phase != HostPhase.Closed, "Host protocol closed: " + peer.Protocol.Failure);
        }
    }
    private IEnumerator Snapshot(bool save = false)
    {
        if (peers.Count == 0)
        {
            Measure("solo-buff-capture", () => { foreach (var actor in live) actor.Buff.CaptureReplication(); });
            yield break;
        }
        double start = PNPolicyClock.Now(); int captures = EveRegistrySession.Context.Snapshots.Captures;
        foreach (var peer in peers) if (peer.Protocol.Phase == HostPhase.Live) { peer.Protocol.RequestSnapshot(PNPolicyClock.Now()); peer.CaptureSnapshot(); }
        var packets = new List<byte[]>(); wireBytes = messages = 0;
        while (EveRegistrySession.Context.Snapshots.PendingJobs > 0)
        {
            frameWorkStart = PNPolicyClock.Now(); bool freeze = EveRegistrySession.Context.Snapshots.Captures == captures;
            Measure(freeze ? "capture-freeze" : "serialize-frame", () => EveRegistrySession.Context.Snapshots.AdvanceFrame());
            yield return null;
        }
        Measure("snapshot-encode-send", () => Drain(save, packets));
        Add("snapshot-wire-three-peers", PNPolicyClock.Now() - start, bytes: wireBytes, count: messages);
        Check(EveRegistrySession.Context.Snapshots.Captures == captures + 1, "Three peers did not share one capture.");
        if (save) { snapshots.Add(packets); recordedSession = EveRegistrySession.Context.SessionId; }
        foreach (var peer in peers) peer.Applied(new AppliedAck(peer.Incarnation, peer.Protocol.SnapshotId, peer.Protocol.EndSequence));
        Drain(false);
    }
    private void FrameSample(double previous, double work)
    {
        ValidateIntegrity();
        var hot = hotPaths && !eventLabels ? EveHotPathMetrics.Drain() : null;
        if (!recording) return;
        if (hot != null) EveHotPathMetrics.DrainEvents();
        if (hot != null) foreach (var item in hot)
        {
            Add("hot-path", item.Value.Milliseconds, item.Key, count: item.Value.Calls);
            rows[rows.Count - 1].ExclusiveMs = item.Value.ExclusiveMilliseconds;
            rows[rows.Count - 1].ScopeAllocatedBytes = item.Value.AllocatedBytes;
        }
        FrameTimingManager.CaptureFrameTimings(); uint n = FrameTimingManager.GetLatestTimings(1, timing);
        rows.Add(new Row { Case = caseName, Pattern = pattern, Phase = "frame", Window = stage, Actors = actorCount, Instances = totalInstances, Repeat = repeat, Sample = sample,
            Frame = Time.frameCount, BudgetMs = budgetMs, ActualInstances = integrityInstances, ActualDotInstances = integrityDots, ActualDotHits = integrityHits, ActualDotCues = integrityCues, Ms = work, WallMs = PNPolicyClock.Now() - previous,
            MainInclusiveMs = main.Valid ? main.LastValue / 1e6 : -1, WaitMs = wait.Valid ? wait.LastValue / 1e6 : -1,
            CpuFrameMs = n > 0 ? timing[0].cpuFrameTime : -1, GpuFrameMs = n > 0 ? timing[0].gpuFrameTime : -1,
            ActiveMainMs = n > 0 ? timing[0].cpuMainThreadFrameTime : -1, PresentWaitMs = n > 0 ? timing[0].cpuMainThreadPresentWaitTime : -1,
            ActiveRenderMs = n > 0 ? timing[0].cpuRenderThreadFrameTime : -1, TimingTimestamp = n > 0 ? timing[0].frameStartTimestamp : 0,
            GcBytes = gc.Valid ? gc.LastValue : -1, WireBytes = wireBytes, Messages = messages,
            GcCollections0 = GC.CollectionCount(0), GcCollections1 = GC.MaxGeneration >= 1 ? GC.CollectionCount(1) : -1, GcCollections2 = GC.MaxGeneration >= 2 ? GC.CollectionCount(2) : -1,
            AllocationRecording = allocationTrace && UnityEngine.Profiling.Profiler.enabled,
            Resident = EveRegistrySession.Context.Cleanup.ResidentCount, Backlog = EveRegistrySession.Context.Cleanup.Scheduler.BacklogCount, ManagedBytes = GC.GetTotalMemory(false),
            NativeAllocatedBytes = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(), GameDeltaMs = Time.deltaTime * 1000d });
    }
    private void Update()
    {
        if (popupProbe != null) { popupProbe.Phase = recording ? stage : "warmup"; popupProbe.Repeat = repeat; popupProbe.Sample = sample; }
        frameStarted = Stopwatch.GetTimestamp();
        if (eventLabels) QueryPerformanceCounter(out nativeFrameStarted);
    }
    private void LateUpdate()
    {
        if (popupProbe != null && !done)
        {
            popupProbe.Repeat = repeat; popupProbe.Sample = sample; popupProbe.Frame();
        }
        if (!eventLabels || done) return;
        var counters = EveHotPathMetrics.Drain();
        var diagnosticEvents = EveHotPathMetrics.DrainEvents();
        if (!recording) return;
        if (retainedDiagnosticEvents + diagnosticEvents.Length > 250000) { droppedDiagnosticEvents += diagnosticEvents.Length; diagnosticEvents = Array.Empty<EveHotPathMetrics.Event>(); }
        else retainedDiagnosticEvents += diagnosticEvents.Length;
        QueryPerformanceCounter(out long nativeLate);
        eventRows.Add(new { frame = Time.frameCount, repeat, sample, caseName, pattern, stage,
            realtimeSeconds = Time.realtimeSinceStartupAsDouble, gameSeconds = Time.timeAsDouble,
            updateTimestamp = frameStarted, lateTimestamp = Stopwatch.GetTimestamp(),
            nativeUpdateTimestamp = nativeFrameStarted, nativeLateTimestamp = nativeLate,
            wireBytes, messages, gcCollections0 = GC.CollectionCount(0), gcCollections1 = GC.MaxGeneration >= 1 ? GC.CollectionCount(1) : -1,
            gcCollections2 = GC.MaxGeneration >= 2 ? GC.CollectionCount(2) : -1, counters, diagnosticEvents, droppedDiagnosticEvents });
    }
    private void ChangeStacks(Actor actor,int delta)
    {
        if(actor.Handles.Length<=1) return;
        actor.Integrity.ExpectStackDelta(1,delta);
        actor.Buff.Buffs.ChangeStacks(actor.Handles[1],delta);
    }
    private void Replace(Actor actor, int slot, string kind)
    {
        actor.Integrity.BeginChange(slot, kind);
        try
        {
            actor.Buff.Buffs.Remove(actor.Handles[slot]);
            actor.Integrity.MarkNewKind(slot, kind);
            var receipt = actor.Buff.Buffs.Apply(new PNBuffApplyRequest(PNPolicyContent.BuffId(kind, slot), actor.Source));
            Check(receipt.Handle.IsAlive && receipt.Error == null, "Fixture replacement rejected."); actor.Handles[slot] = receipt.Handle;
        }
        finally { actor.Integrity.EndChange(slot, kind); }
    }
    private void ValidateIntegrity()
    {
        integrityInstances = integrityDots = 0; integrityHits = integrityCues = 0;
        foreach (var actor in live)
        {
            if (actor.Integrity == null) continue;
            actor.Integrity.Validate(actor.Handles);
            integrityInstances += actor.Integrity.Alive; integrityDots += actor.Integrity.Dots; integrityHits += actor.Integrity.Hits; integrityCues += actor.Integrity.Cues;
        }
    }
    private void PrepareAoe(Case c)
    {
        if (aoeTargets == 0) return;
        Check(pattern == "natural" && aoeTargets <= c.Actors - 4 && aoeTargets > 0 && aoeDepth > 0, "AoE targets/depth require a valid natural cohort.");
        var selected = new List<object>();
        foreach (var actor in live.Skip(4).Take(aoeTargets))
        {
            var isDot = new bool[actor.Handles.Length];
            for (int j=0;j<isDot.Length;j++)
                isDot[j] = actor.Handles[j].IsAlive && actor.Buff.Buffs.Get(actor.Handles[j]).Id == PNPolicyContent.BuffId("dot",j);
            actor.AoeSlots = PNPolicyFixturePlan.SelectAoe(isDot, aoeDepth);
            selected.Add(new { actor=actor.Id, slots=actor.AoeSlots });
        }
        File.WriteAllText(Path.Combine(output, caseName+"-AoeSelection-"+repeat+".json"), JsonConvert.SerializeObject(new { aoeTargets, aoeDepth, cohort=checked(aoeTargets*aoeDepth), selected }));
    }
    private IEnumerator Steady(int frames, double seconds, bool churn, double maximumSeconds = double.PositiveInfinity)
    {
        double started = PNPolicyClock.Now(), previous = started;
        ValidateIntegrity(); long hitsBefore=integrityHits, cuesBefore=integrityCues;
        if (allocationTrace && recording && !traceCaptured)
        {
            traceCaptured = true; traceStarted = started;
            UnityEngine.Profiling.Profiler.enabled = true;
        }
        double gameElapsed = 0, nextStack = 0, nextReapply = 0, nextAoe = 1; int stackBurst = 0;
        for (sample = 0; (sample < frames || PNPolicyClock.Now() - started < seconds * 1000) && PNPolicyClock.Now() - started < maximumSeconds * 1000; sample++)
        {
            frameWorkStart = PNPolicyClock.Now(); wireBytes = messages = 0; double work = 0;
            // PNBuffManager.Update runs real selected-clock gameplay once; this coroutine measures dispatch/churn separately.
            gameElapsed += Time.deltaTime;
            if (churn && pattern == "natural")
            {
                // Per-actor schedules and stack counters: no actor receives -1 before its first +1.
                bool anyStack = false, anyReapply = false;
                foreach (var a in live) { if (gameElapsed >= a.NextStack) anyStack = true; if (gameElapsed >= a.NextReapply) anyReapply = true; }
                if (anyStack) work += Measure("stack-burst", () =>
                { foreach (var a in live) while (gameElapsed >= a.NextStack) { int delta = a.StackToggle++ % 2 == 0 ? 1 : -1; ChangeStacks(a, delta); a.NextStack += .5; } });
                if (anyReapply) work += Measure("remove-apply-burst", () =>
                { foreach (var a in live) while (gameElapsed >= a.NextReapply)
                    {
                        int j = a.Handles.Length - 1;
                        if (j >= 0)
                        {
                            string replacementKind = projectiles != null && a.Stat is PNPolicyStat ? "dot" : "timed";
                            Replace(a, j, replacementKind);
                        }
                        a.NextReapply += 1;
                    } });
            }
            if (churn && pattern == "natural" && aoeTargets > 0)
                while (gameElapsed >= nextAoe)
                {
                    work += Measure("aoe-burst", () =>
                    {
                        // Handles below the churn index only (churn replaces the last handle), so the cohort stays aligned.
                        foreach (var a in live.Skip(4).Take(aoeTargets))
                            foreach (int k in a.AoeSlots) Replace(a, k, "dot");
                        ValidateIntegrity();
                    });
                    nextAoe += aoePeriod;
                }
            while (churn && pattern != "natural" && gameElapsed >= nextStack)
            {
                int delta = stackBurst++ % 2 == 0 ? 1 : -1;
                work += Measure("stack-burst", () => { foreach (var actor in live) ChangeStacks(actor, delta); });
                nextStack += .5;
            }
            while (churn && pattern != "natural" && gameElapsed >= nextReapply)
            {
                work += Measure("remove-apply-burst", () =>
                { foreach (var actor in live) { int j = actor.Handles.Length - 1; if (j < 0) continue; string replacementKind = projectiles != null && actor.Stat is PNPolicyStat ? "dot" : "timed"; Replace(actor, j, replacementKind); } });
                nextReapply += 1;
            }
            ValidateIntegrity();
            work += Measure("outbox-encode-send", () => Drain(false));
            if (presentationCamera != null) work += Measure("presentation-render-submit", RenderPresentation);
            if (projectiles != null)
            {
                var tick = projectiles.Tick(sample); Add("projectile-tick", tick.Step); Add("projectile-motion", tick.Plan); Add("projectile-placement", tick.Place);
                Add("projectile-physics", tick.Physics); Add("projectile-query", tick.Query); Add("projectile-render-submit", tick.Render); Add("projectile-pool", tick.Pool); work += tick.Step;
            }
            yield return null;
            FrameSample(previous, work); previous = PNPolicyClock.Now();
            if (allocationTrace && UnityEngine.Profiling.Profiler.enabled && PNPolicyClock.Now() - traceStarted >= 2000)
                UnityEngine.Profiling.Profiler.enabled = false;
            if (sample % 60 == 0) Progress("steady");
            Check(live.All(x => x.Buff.RuntimeFailure == null && !x.Stat.IsDead), "Actor/Buff runtime failure or dead test actor.");
        }
        if(recording && live.Count>0)
            File.AppendAllText(Path.Combine(output,"WorkloadAudit.jsonl"),JsonConvert.SerializeObject(new{caseName,pattern,repeat,frames=sample,gameSeconds=gameElapsed,actualDamageHits=integrityHits-hitsBefore,scheduledTickCues=integrityCues-cuesBefore,instances=integrityInstances,dotInstances=integrityDots,aoeTargets,aoeDepth,cohort=aoeTargets*aoeDepth})+"\n");
    }
    private IEnumerator Cleanup(List<Actor> group, bool finish)
    {
        foreach (var actor in group) { actor.Integrity?.Dispose(); actor.Integrity = null; }
        double start = PNPolicyClock.Now();
        if (finish) { foreach (var peer in peers) peer.Dispose(); peers.Clear(); runtime.EndSession(); }
        else Measure("hide-retire", () => { foreach (var actor in group) EveRegistrySession.Context.Cleanup.Retire(actor.Loader); });
        Check(group.All(x => !x.Loader.gameObject.activeInHierarchy && !x.Loader.IsReady), "Retired actor visible/Ready.");
        while (finish ? EveRegistrySession.State == EveSessionState.Stopping : group.Any(x => x.Loader != null && x.Loader.State != EveEntityLoadState.Idle))
        {
            frameWorkStart = PNPolicyClock.Now();
            Measure("cleanup-pump", () => EveRegistrySession.PumpCleanup());
            Check(PNPolicyClock.Now() - start < 120000, "Cleanup timeout."); yield return null;
        }
        Add("cleanup-latency", PNPolicyClock.Now() - start);
        if (finish) Check(EveRegistrySession.State == EveSessionState.Stopped, "Session cleanup blocked.");
        yield return null;
    }
    private IEnumerator Replay()
    {
        if (snapshots.Count == 0) yield break;
        pattern = "client-replay"; caseName = "retained-snapshot"; Begin(EveSessionRole.Client);
        using (var client = new EveActorClientConnection(EveRegistrySession.Context, recordedSession, 1, wirePolicy, PNPolicyClock.Now,
            spawn => { var actor = Create(spawn.OwnerId > 0 ? (int)spawn.OwnerId - 1 : 4); actor.Loader.GetComponent<EveEntityIdentity>().ResetIdentity(); return actor.Loader; }, loader => Destroy(loader.gameObject), compatibility))
        {
            foreach (var packets in snapshots)
            {
                double start = PNPolicyClock.Now();
                Measure("snapshot-receive", () => { foreach (var bytes in packets) client.Receive(bytes); });
                while (client.Protocol.Phase == ClientPhase.Preparing)
                {
                    frameWorkStart = PNPolicyClock.Now(); Measure("client-prepare-commit-frame", () => client.AdvanceFrame(new EveEntityPreparationBudget(4, 32, budgetMs)));
                    Check(client.LastError == null, "Client replay: " + client.LastError); yield return null;
                }
                Check(client.Protocol.Phase == ClientPhase.Live && client.TakeAppliedAck().HasValue, "Client did not commit/ack.");
                Check(EveRegistrySession.Context.Entities.Snapshot().Count == actorCount, "Client actor count.");
                Check(EveRegistrySession.Context.Entities.Snapshot().Sum(x => x.GetComponent<PNBuffManager>().Presentation.PresentationSnapshot().Count) == totalInstances, "Client Buff count.");
                Add("client-snapshot-latency", PNPolicyClock.Now() - start); Add("client-commit-essential", EveRegistrySession.Context.LastCommitMilliseconds);
            }
        }
        var clean = Cleanup(new List<Actor>(), true); while (clean.MoveNext()) yield return clean.Current;
        snapshots.Clear(); Flush();
    }
    private IEnumerator Micro(int count, int rounds)
    {
        caseName = "micro-wave-" + count; pattern = "no-buff"; actorCount = count; totalInstances = 0; budgetMs = 1;
        Begin(EveSessionRole.Host); var group = Enumerable.Range(0, count).Select(i => Create(i)).ToList();
        for (sample = -10; sample < rounds; sample++)
        {
            recording = sample >= 0; double start = PNPolicyClock.Now();
            foreach (var actor in group) actor.Loader.Prepare(hub);
            Add("prepare-sync-total", PNPolicyClock.Now() - start);
            Measure("commit-total", () => EveEntityLoader.CommitBatch(group.Select(x => x.Loader))); Add("commit-essential", EveRegistrySession.Context.LastCommitMilliseconds);
            foreach (var actor in group) actor.Loader.Unload();
            foreach (var actor in group) { actor.Loader.GetComponent<EveEntityIdentity>().ResetIdentity(); actor.Loader.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(++nextId), EveId.Parse(group.IndexOf(actor) < 4 ? "project_n:eve" : "project_n:dummy"), 0); }
            yield return null;
            if (sample % 50 == 0) { Progress("micro"); Flush(); }
        }
        recording = true; runtime.EndSession(); foreach (var actor in group) Destroy(actor.Loader.gameObject); yield return null; Flush();
    }
    private IEnumerator Run()
    {
        long allocationBefore = GC.GetAllocatedBytesForCurrentThread(); var probe = new byte[1048576]; allocationAvailable = GC.GetAllocatedBytesForCurrentThread() > allocationBefore; GC.KeepAlive(probe);
        EveHotPathMetrics.TraceAllocations = allocationTrace && allocationAvailable;
        var docs = new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNPolicyContent() }, docs);
        compatibility = EveContentCompatibility.Create(hub, docs, PNPolicyRuntime.FixtureRulesVersion);
        // Generous TEST capacities prevent an invented production bound from truncating measured traffic.
        wirePolicy = new ModelPolicy(1024, 67108864, 1048576, 32768, 2048, 65536, 268435456, 268435456, 4096, 1024, 3, 30000, 120000, 120000, 120000, 360000, 1080000);
        File.WriteAllText(Path.Combine(output, "Provenance.json"), JsonConvert.SerializeObject(new { fixture = 6, revision = "step3b-combat-validated-aoe", integrity = "continuous-exact-kind-stack-hit-schedule-and-DamageReceived", countdownOptIn, aoeTargets, aoeDepth, aoePeriod, naturalSeed, naturalWaveWindowSeconds = 3.0, naturalInstanceWindowSeconds = .5, frameCap, eventLabels, allocationTrace, stopwatchFrequency = Stopwatch.Frequency, cpuTimerFrequency = FrameTimingManager.GetCpuTimerFrequency(), churn = "game-time: stack every .5s, remove/apply every 1s", stagger, unity = Application.unityVersion, mode, backend = "Mono", development = UnityEngine.Debug.isDebugBuild,
            editor = Application.isEditor, batchMode = Application.isBatchMode, selectedCase, selectedPattern, peerCount, hotPaths, popups, measurementCamera = measurementCameraOptIn, selectedSeconds, cpu = SystemInfo.processorType, gpu = SystemInfo.graphicsDeviceName, ramMiB = SystemInfo.systemMemorySize, resolution = new[] { Screen.width, Screen.height },
            quality = QualitySettings.GetQualityLevel(), rules = int.Parse(EveGameRuntime.CurrentRulesVersion.Split(':').Last(), CultureInfo.InvariantCulture), gameRules = EveGameRuntime.CurrentRulesVersion, runtimeRules = PNPolicyRuntime.FixtureRulesVersion, modApi = 6, compatibility.ContentHashes, allocationAvailable,
            assemblyHashes = new[] { typeof(EveStatSet).Assembly, typeof(PNStatSet).Assembly, typeof(EveGameRuntime).Assembly, typeof(PNLifecyclePolicyPlayer).Assembly, typeof(PNProjectileCollisionPlayerBenchmark).Assembly }.Distinct().ToDictionary(a => a.GetName().Name, a => AssemblyHash(a.Location)),
            caveats = "Opt-in actors: Stat/TimeScale/Control/Buff, Ability on four players. No enemy AI/Move/Skill/HP replication or full-game renderer. Stable/timed/DOT profiles registered only in fixture. Normal Player counters may be unavailable (-1). Inclusive frame markers are not verified active CPU work. Three host outboxes, transport not connected; accepted bytes released immediately. No production policy." }, Formatting.Indented));
        for (int i = 0; i < 120; i++) yield return null;
        File.WriteAllText(Path.Combine(output, "PopupMeasurement.json"), JsonConvert.SerializeObject(new { revision = "step4a-popup1", popupDiagnostic, popupDamage,
            gauge = "before/after Show and Advance, peak across observed pools; LateUpdate frame sample",
            stringCapture = eventLabels && popupDiagnostic, fixedAuthoredStyle = true,
            caveat = "CPU scopes only; scope byte counts are managed allocations on current thread, not native mesh memory or GPU costs." }, Formatting.Indented));
        if (mode == "popup") { var popup = PopupFixture(); while (popup.MoveNext()) yield return popup.Current; yield break; }
        if (mode == "baseline")
        {
            actorCount = totalInstances = 0; caseName = "instrumented-empty-player"; pattern = "none"; Begin(EveSessionRole.Host);
            for (repeat = 0; repeat < 3; repeat++)
            {
                Application.targetFrameRate = frameCap > 0 ? frameCap : repeat == 2 ? 120 : -1;
                var baseline = Steady(300, 10, false); while (baseline.MoveNext()) yield return baseline.Current; Flush();
            }
            runtime.EndSession(); yield break;
        }
        if (mode == "micro")
        { foreach (int count in new[] { 32, 64, 128, 256 }) { var test = Micro(count, 300); while (test.MoveNext()) yield return test.Current; } yield break; }
        var cases = new[] { new Case("wave32", 32, 30, 5), new Case("wave64", 64, 30, 10), new Case("wave128matched", 128, 30, 10), new Case("wave128", 128, 30, 20), new Case("overload256", 256, 30, 20), new Case("player60", 64, 60, 10), new Case("one100", 4, 100, 0), new Case("normal20", 20, 30, 5, 200), new Case("normal6", 6, 30, 5, 500) };
        Check(selectedCase == "all" || cases.Any(x => x.Name == selectedCase), "Unknown measurement case.");
        bool full = mode == "full", endurance = mode == "endurance", combined = mode == "combined", validate = mode == "validate";
        foreach (var c in (validate ? cases.Where(x => x.Name == "wave32") : endurance ? cases.Where(x => x.Name == "wave64") : combined ? cases.Where(x => x.Name == "wave64" || x.Name == "wave128" || x.Name == "overload256") : cases).Where(x => selectedCase == "all" || selectedCase == x.Name))
        foreach (string p in (combined || endurance || validate ? new[] { "mixed" } : new[] { "stable", "timed", "mixed", "natural" }).Where(x => selectedPattern == x || selectedPattern == "all" && x != "natural"))
        foreach (double b in combined || endurance || full || validate ? new[] { 1d } : new[] { .5, 1, 2 })
        for (repeat = 0; repeat < (full ? 3 : 1); repeat++)
        {
            caseName = c.Name; pattern = p; actorCount = c.Actors; budgetMs = b; sample = -1; Progress("prepare"); Begin(EveSessionRole.Host);
            var prepare = Prepare(c, live); while (prepare.MoveNext()) yield return prepare.Current;
            Apply(c, live, p);
            if (stagger) for (int i = 0; i < live.Count; i++) live[i].Buff.Advance(.49 * i / Math.Max(1, live.Count - 1));
            Connect(live); var capture = Snapshot(c.Name == "wave32" && p == "mixed" && b == 1); while (capture.MoveNext()) yield return capture.Current;
            capture = Snapshot(c.Name == "wave32" && p == "mixed" && b == 1); while (capture.MoveNext()) yield return capture.Current;
            if (p == "natural") { var populate = Populate(c, live); while (populate.MoveNext()) yield return populate.Current; }
            if (c.Bullets > 0)
            {
                Check(peerCount == 3 && (p == "mixed" || p == "natural"), "Normal additions require three outboxes and mixed player Buffs.");
                Check(totalInstances == 120 + (c.Actors - 4) * 5, "Normal-load instance count.");
                projectiles = new PNProjectileCollisionPlayerBenchmark.World(PNProjectileCollisionPlayerBenchmark.Method.Trigger, 2, PNProjectileCollisionPlayerBenchmark.Pattern.Player90Homing, c.Bullets, true, c.Actors + c.Bullets, 128);
                projectiles.IncludeStayCallbacks = false;
                for (int layer = 0; layer < 32; layer++) Physics.IgnoreLayerCollision(31, layer, layer != 29 && layer != 30);
                projectiles.Render(); projectiles.Capture(Path.Combine(output, c.Name + "-projectiles-" + repeat + ".png"), true);
            }
            if (measurementCameraOptIn && mode == "full") StartMeasurementCamera();
            // Warmup is excluded from frame distribution, but first-touch lifecycle samples remain labelled sample=-1.
            Flush(); recording = false; var warm = Steady(validate ? 10 : 120, 0, false, validate ? 2 : 10); while (warm.MoveNext()) yield return warm.Current; recording = true; EveHotPathMetrics.Drain(); EveHotPathMetrics.DrainEvents();
            PrepareAoe(c); ValidateIntegrity();
            Progress("steady");
            if (mode == "presentation") { StartPresentation(); for (int settle = 0; settle < 10; settle++) yield return null; CapturePresentation(); }
            if (combined)
            {
                foreach (int bullets in new[] { 300, 500, 1000 })
                foreach (var trajectory in new[] { PNProjectileCollisionPlayerBenchmark.Pattern.Player90Homing, PNProjectileCollisionPlayerBenchmark.Pattern.BossSin })
                foreach (var method in c.Actors == 128 ? new[] { PNProjectileCollisionPlayerBenchmark.Method.Sweep, PNProjectileCollisionPlayerBenchmark.Method.Trigger } : new[] { PNProjectileCollisionPlayerBenchmark.Method.Trigger, PNProjectileCollisionPlayerBenchmark.Method.Sweep })
                {
                    caseName = c.Name + "-bullet" + bullets + "-" + trajectory + "-" + method;
                    projectiles = new PNProjectileCollisionPlayerBenchmark.World(method, 2, trajectory, bullets, true, c.Actors + bullets);
                    projectiles.IncludeStayCallbacks = false;
                    for (int layer = 0; layer < 32; layer++) Physics.IgnoreLayerCollision(31, layer, layer != 29 && layer != 30);
                    projectiles.Render(); projectiles.Capture(Path.Combine(output, caseName + ".png"), true);
                    var combinedFrames = Steady(300, 0, false, 15); while (combinedFrames.MoveNext()) yield return combinedFrames.Current;
                    Check(projectiles.Contacts.Any(x => x != 0), "No projectile contacts in combined fixture.");
                    projectiles.Dispose(); projectiles = null; Flush(); yield return null;
                }
            }
            else
            {
                Application.targetFrameRate = frameCap > 0 ? frameCap : repeat == 2 ? 120 : -1;
                if (endurance)
                {
                    double enduranceStarted = PNPolicyClock.Now();
                    while (PNPolicyClock.Now() - enduranceStarted < 1800000)
                    {
                        var steady = Steady(0, Math.Min(60, (1800000 - (PNPolicyClock.Now() - enduranceStarted)) / 1000), true); while (steady.MoveNext()) yield return steady.Current;
                        Flush(); var resync = Snapshot(); while (resync.MoveNext()) yield return resync.Current;
                        // Wave/pool reuse gets new IDs. Keep generation-replacement failure testing separate.
                        var replacement = new List<Actor>(); prepare = Prepare(c, replacement); while (prepare.MoveNext()) yield return prepare.Current;
                        var old = live.ToList(); foreach (var actor in old) actor.Integrity?.Dispose(); live.Clear(); live.AddRange(replacement); Apply(c, live, p);
                        foreach (var peer in peers) { foreach (var actor in old) peer.Despawn(actor.Id); foreach (var actor in live) peer.Track(actor.Loader); }
                        var retire = Cleanup(old, false); while (retire.MoveNext()) yield return retire.Current;
                        Measure("endurance-drain", () => Drain(false)); Flush(); Progress("endurance-wave-cycle");
                    }
                    Add("endurance-wall", PNPolicyClock.Now() - enduranceStarted);
                }
                else
                {
                    var steady = Steady(full ? 0 : validate ? 30 : 600, selectedSeconds > 0 ? selectedSeconds : full ? 60 : 0, p == "mixed" || p == "natural", full ? double.PositiveInfinity : validate ? 5 : 15); while (steady.MoveNext()) yield return steady.Current;
                }
                Flush();
            }
            Application.targetFrameRate = -1;
            if (c.Bullets > 0) { Check(projectiles.Contacts.Any(x => x != 0), "Normal-load projectile contacts."); projectiles.Dispose(); projectiles = null; }
            StopPresentation();
            if (!endurance && !combined && mode != "presentation")
            {
                Progress("generation-replacement"); var replacement = new List<Actor>();
                bool backpressure = c.Actors == 256 && p == "mixed" && b == 2;
                if (backpressure)
                {
                    Measure("backpressure-retire-burst", () => { foreach (var actor in live) EveRegistrySession.Context.Cleanup.Retire(actor.Loader); });
                    Check(!EveRegistrySession.Context.Cleanup.CanBeginPreparation, "Backpressure threshold not reached.");
                }
                prepare = Prepare(c, replacement, 2, live); while (prepare.MoveNext()) yield return prepare.Current;
                foreach (var peer in peers) { foreach (var actor in live) peer.Despawn(actor.Stat.EntityId.Value); }
                if (!backpressure) { var retire = Cleanup(live, false); while (retire.MoveNext()) yield return retire.Current; }
                live.Clear(); live.AddRange(replacement); Apply(c, live, p);
            }
            Progress("cleanup"); var cleanup = Cleanup(live, true); while (cleanup.MoveNext()) yield return cleanup.Current;
            live.Clear(); Flush();
            if (snapshots.Count > 0) { var replay = Replay(); while (replay.MoveNext()) yield return replay.Current; }
        }
    }
    // Isolates popup cost through actual PNStat.ReceiveDamage. Scheduled combat at .5s;
    // it deliberately has no Buff behaviors, outboxes or projectile workload.
    private IEnumerator PopupFixture()
    {
        Check(popupDamage == "low" || popupDamage == "diverse" || popupDamage == "repeat", "Unknown popup damage distribution.");
        Application.targetFrameRate = frameCap > 0 ? frameCap : 120;
        caseName = "popup-small"; pattern = popupDamage; actorCount = 20; totalInstances = 0;
        presentationTexture = new RenderTexture(1280,720,24,RenderTextureFormat.ARGBHalf); presentationTexture.Create();
        var popupCameraRoot = new GameObject("Owned popup measurement camera"); popupCameraRoot.tag = "MainCamera";
        presentationCamera = popupCameraRoot.AddComponent<Camera>(); presentationCamera.orthographic = true; presentationCamera.orthographicSize = 12;
        presentationCamera.transform.SetPositionAndRotation(new Vector3(0,12,-20),Quaternion.LookRotation(new Vector3(0,-12,20)));
        presentationCamera.clearFlags = CameraClearFlags.SolidColor; presentationCamera.backgroundColor = Color.black;
        presentationCamera.targetTexture = presentationTexture; presentationCamera.GetUniversalAdditionalCameraData().SetRenderer(1);
        var schedule = new System.Random(naturalSeed);
        double[] offsets = Enumerable.Range(0, 16).Select(_ => schedule.NextDouble() * .5).ToArray();
        File.WriteAllText(Path.Combine(output, "PopupSchedule.json"), JsonConvert.SerializeObject(new { naturalSeed, offsets, period = .5,
            critical = "every fourth scheduled hit", elements = "hit index modulo four: none/fire/ice+lightning/all", cold = "pool cold; assets/TMP may already be warm in repeat >0" }));
        for (repeat = 0; repeat < 3; repeat++)
        {
            Begin(EveSessionRole.Offline);
            var prepare = Prepare(new Case("popup-small",20,0,0), live); while (prepare.MoveNext()) yield return prepare.Current;
            var sender = live[0].Stat;
            sender.SetSource(EveId.Parse("test:popup_measure_crit"), new[] { new EveStatModifier(PNContentIds.CritRate,100d,0) });
            Check(live.Skip(4).All(a => a.Stat.DamagePopupPool == null), "Cold phase did not start with empty pools.");
            var ordinals = new int[16]; long actualHits = 0;
            void Hit(int target)
            {
                int ordinal = ordinals[target]++;
                long key = target + ordinal * 16L;
                double damage = popupDamage == "low" ? .000001 : popupDamage == "repeat" ? 128 : 1 + (key * 7919 + naturalSeed) % 99999 + .25;
                EveId[] tags = ((ordinal + target) % 4) switch
                {
                    0 => new[] { PNContentIds.True },
                    1 => new[] { PNContentIds.True, PNContentIds.Fire },
                    2 => new[] { PNContentIds.True, PNContentIds.Ice, PNContentIds.Lightning },
                    _ => new[] { PNContentIds.True, PNContentIds.Fire, PNContentIds.Ice, PNContentIds.Lightning }
                };
                bool crit = (ordinal + target) % 4 == 0;
                var result = live[target+4].Stat.ReceiveDamage(sender.CreateDamage(damage, crit, 0, tags));
                Check(result.IsCritical == crit && !result.Killed, "Popup micro combat schedule changed crit/death."); actualHits++;
            }
            sample = 0; Progress("popup-cold");
            if (allocationTrace)
            {
                UnityEngine.Profiling.Profiler.logFile = Path.Combine(output,"AllocationTrace-Cold-"+repeat+".raw");
                UnityEngine.Profiling.Profiler.enabled = true;
            }
            if (popupProbe != null) { popupProbe.Repeat = repeat; popupProbe.Sample = sample; }
            EveHotPathMetrics.Drain(); double previous = PNPolicyClock.Now();
            for (int target = 0; target < 16; target++) Hit(target);
            yield return null; FrameSample(previous,0);
            if (allocationTrace) UnityEngine.Profiling.Profiler.enabled = false;
            if (repeat == 0)
            {
                // Focus a real existing popup for readable render evidence; restore before warmup.
                var savedCameraPosition = presentationCamera.transform.position;
                var savedCameraRotation = presentationCamera.transform.rotation;
                float savedCameraSize = presentationCamera.orthographicSize;
                presentationCamera.orthographicSize = .9f;
                presentationCamera.transform.rotation = Quaternion.identity;
                presentationCamera.transform.position = live[7].Stat.DamagePopupPool.Active[0].transform.position + Vector3.back * 5;
                foreach (var actor in live.Skip(4)) foreach (var item in actor.Stat.DamagePopupPool.Active) item.Advance(0,presentationCamera);
                RenderPresentation();
                var previousTarget = RenderTexture.active; var pixels = new Texture2D(1280,720,TextureFormat.RGB24,false);
                try
                {
                    RenderTexture.active = presentationTexture; pixels.ReadPixels(new Rect(0,0,1280,720),0,0); pixels.Apply();
                    Check(pixels.GetPixels32().Count(p => p.r > 80 || p.g > 80 || p.b > 80) > 100, "Popup Player rendered no visible pixels.");
                    File.WriteAllBytes(Path.Combine(output,"PopupPlayer.png"),pixels.EncodeToPNG());
                }
                finally
                {
                    RenderTexture.active = previousTarget; Destroy(pixels);
                    presentationCamera.transform.SetPositionAndRotation(savedCameraPosition,savedCameraRotation); presentationCamera.orthographicSize = savedCameraSize;
                    foreach (var actor in live.Skip(4)) foreach (var item in actor.Stat.DamagePopupPool.Active) item.Advance(0,presentationCamera);
                }
            }

            File.AppendAllText(Path.Combine(output,"PopupAudit.jsonl"),JsonConvert.SerializeObject(new { repeat, phase = "cold", actualHits,
                pools = live.Skip(4).Count(a => a.Stat.DamagePopupPool != null), frame = Time.frameCount })+"\n");
            recording = false; Progress("popup-warmup");
            for (int warm = 0; warm < 120; warm++) yield return null;
            recording = true; sample = 0; Progress("popup-steady");
            var next = (double[])offsets.Clone(); double elapsed = 0, started = PNPolicyClock.Now(); previous = started;
            long before = actualHits;
            if (allocationTrace)
            {
                traceStarted = started; UnityEngine.Profiling.Profiler.logFile = Path.Combine(output,"AllocationTrace-Steady-"+repeat+".raw");
                UnityEngine.Profiling.Profiler.enabled = true;
            }
            EveHotPathMetrics.Drain();
            while (PNPolicyClock.Now()-started < (selectedSeconds > 0 ? selectedSeconds : 20)*1000)
            {
                elapsed += Time.deltaTime;
                for (int target = 0; target < 16; target++) while (elapsed >= next[target]) { Hit(target); next[target] += .5; }
                yield return null; FrameSample(previous,0); previous = PNPolicyClock.Now(); sample++;
                if (allocationTrace && UnityEngine.Profiling.Profiler.enabled && PNPolicyClock.Now()-traceStarted >= 2000) UnityEngine.Profiling.Profiler.enabled = false;
            }
            long expected = offsets.Sum(offset => elapsed < offset ? 0L : (long)Math.Floor((elapsed-offset)/.5)+1);
            Check(actualHits-before == expected, "Popup fixture scheduled/actual hit mismatch.");
            File.AppendAllText(Path.Combine(output,"PopupAudit.jsonl"),JsonConvert.SerializeObject(new { repeat, phase = "steady", actualHits = actualHits-before, expected, elapsed })+"\n");
            Progress("cleanup"); var cleanup = Cleanup(live,true); while (cleanup.MoveNext()) yield return cleanup.Current;
            live.Clear(); Flush(); popupProbe?.Flush(); yield return null;
        }
        StopPresentation();
    }
    private static string AssemblyHash(string path)
    {
        using (var stream = File.OpenRead(path)) using (var hash = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    private void OnDestroy() { if (!done) { gc.Dispose(); main.Dispose(); wait.Dispose(); Application.logMessageReceived -= Log; } }
}
