#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using EveEngine;
using EveEngine.Experimental.Lifecycle;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PNLifecycleBaselineSample
{
    public string Case, Phase, Step;
    public int Actors, BuffsPerActor, Repeat, WireBytes;
    public double Milliseconds;
    public long AllocatedBytes;
}

// Test-only subclass: base dependency resolution runs on original component types first.
public sealed class PNLifecycleTimedLoader : EveEntityLoader
{
    public Action<string, string, double, long> Record;
    public bool AllocationCounterAvailable;
    private sealed class TimedStep : EveIEntityLoadStep
    {
        private readonly EveIEntityLoadStep inner;
        private readonly Action<string, string, double, long> record;
        private readonly bool allocationCounterAvailable;
        public TimedStep(EveIEntityLoadStep inner, Action<string, string, double, long> record, bool allocationCounterAvailable)
        { this.inner = inner; this.record = record; this.allocationCounterAvailable = allocationCounterAvailable; }
        public int LoadOrder => inner.LoadOrder;
        public IEnumerable<Type> Dependencies => inner.Dependencies;
        public void Load(EveEntityLoadContext context) => Measure("step-load", () => inner.Load(context));
        public void Unload() => Measure("step-unload", inner.Unload);
        private void Measure(string phase, Action work)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            try { work(); }
            finally { record?.Invoke(phase, inner.GetType().Name, (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency, allocationCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() - allocated : -1); }
        }
    }
    protected override EveIEntityLoadStep[] OrderSteps(EveIEntityLoadStep[] steps)
        => base.OrderSteps(steps).Select(x => (EveIEntityLoadStep)new TimedStep(x, Record, AllocationCounterAvailable)).ToArray();
}

public static class PNLifecycleBaselineReview
{
    public const string Folder = ".utmp/LifecyclePrototype";
    public static string Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling || EveRegistrySession.State != EveSessionState.Stopped)
            throw new InvalidOperationException("Stopped idle Edit session required.");
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "DeltaSamples.jsonl"), "");
        File.WriteAllText(Path.Combine(Folder, "NativeResult.txt"), "RUNNING");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var docs = new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        var compatibility = EveContentCompatibility.Create(hub, docs, "project_n:66");
        var samples = new List<PNLifecycleBaselineSample>();
        long allocationBefore = GC.GetAllocatedBytesForCurrentThread(); var allocationProbe = new byte[1048576];
        bool allocationCounterAvailable = GC.GetAllocatedBytesForCurrentThread() > allocationBefore;
        GC.KeepAlive(allocationProbe);
        var preview = EditorSceneManager.NewPreviewScene();
        var roots = new List<GameObject>(); long nextId = 8800000;
        int actors = 0, buffs = 0, repeat = 0, residentPeak = 0;
        string label = "";
        void Record(string phase, string step, double ms, long alloc)
            => samples.Add(new PNLifecycleBaselineSample { Case = label, Phase = phase, Step = step, Actors = actors, BuffsPerActor = buffs, Repeat = repeat, Milliseconds = ms, AllocatedBytes = alloc });
        T Measure<T>(string phase, Func<T> work)
        {
            long a = GC.GetAllocatedBytesForCurrentThread(), t = Stopwatch.GetTimestamp();
            var result = work(); Record(phase, "whole-operation", (Stopwatch.GetTimestamp() - t) * 1000d / Stopwatch.Frequency, allocationCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() - a : -1); return result;
        }
        PNLifecycleTimedLoader Create(bool player)
        {
            var root = new GameObject("Disposable lifecycle actor " + ++nextId); roots.Add(root); root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, preview);
            root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(nextId), EveId.Parse(player ? "project_n:eve" : "project_n:dummy"), 0);
            root.AddComponent<EveTimeScale>();
            if (player) { root.AddComponent<PNPlayerStat>(); root.AddComponent<PNAbilityManager>(); } else root.AddComponent<PNEnemyStat>();
            root.AddComponent<PNBuffManager>().RenderPresentation = false;
            var loader = root.AddComponent<PNLifecycleTimedLoader>(); loader.Record = Record; loader.AllocationCounterAvailable = allocationCounterAvailable; return loader;
        }
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, compatibility);
            // First-touch is honestly labelled; assets/JIT/domain may already be warm in this Editor.
            foreach (int count in new[] { 1, 16, 64 }) foreach (int perActor in new[] { 0, 4 })
            {
                actors = count; buffs = perActor;
                var group = new List<PNLifecycleTimedLoader>();
                for (repeat = 0; repeat < 3; repeat++)
                {
                    label = "mixed-25pct-player-" + count + "-buff" + perActor + (repeat == 0 ? "-first-touch" : "-same-object-reload");
                    if (repeat == 0) Measure("create-objects", () => { for (int i = 0; i < count; i++) group.Add(Create(i % 4 == 0)); return group.Count; });
                    Measure("load-total", () => { foreach (var loader in group) { loader.Load(hub); loader.gameObject.SetActive(true); loader.GetComponent<PNBuffManager>().Advance(0); } return 0; });
                    if (EveRegistrySession.Context.Entities.Snapshot().Count != count) throw new Exception("Directory does not match fixture actors.");
                    var messages = new List<PNBuffWireMessage>();
                    foreach (var loader in group)
                    {
                        var manager = loader.GetComponent<PNBuffManager>(); manager.Publisher.Message += messages.Add;
                        var source = PNBuffSource.Capture(loader.GetComponent<PNStat>());
                        for (int j = 0; j < perActor; j++)
                        {
                            var receipt = manager.Buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:timescale_reduction"), source,
                                new JObject { ["reduction"] = 0, ["minimumReduction"] = 0, ["duration"] = 5 }));
                            if (!receipt.Handle.IsAlive) throw new Exception("Fixture Buff apply failed: " + receipt.Status + " " + receipt.Message + " " + receipt.Error);
                        }
                    }
                    Measure("advance-one-second", () => { for (int tick = 0; tick < 4; tick++) foreach (var loader in group) loader.GetComponent<PNBuffManager>().Advance(.25); return 0; });
                    var captured = Measure("capture-existing-directory-and-buff", () => EveRegistrySession.Context.Entities.Snapshot()
                        .Select(x => new { Id = x.GetComponent<EveEntityIdentity>().Id.Value, State = x.GetComponent<PNBuffManager>().Publisher.Snapshot() }).ToArray());
                    var encoded = Measure("encode-existing-buff-envelopes", () => captured.Select(x => new ActorState(new PresenceKey(x.Id, 1), false, PNBuffWireCodec.Encode(x.State))).ToArray());
                    var modelPolicy = new ModelPolicy(128, 4194304, 65536, 1024, 4096, 1024, 4194304, 4194304, 128, 4096, 3, 10000, 10000, 10000, 10000, 30000, 90000);
                    var body = Measure("encode-prototype-actor-body", () => ModelCodec.EncodeActors(encoded, modelPolicy));
                    samples[samples.Count - 1].WireBytes = body.Length;
                    long deltaBytes = messages.Sum(x => (long)PNBuffWireCodec.Encode(x).Length);
                    File.AppendAllText(Path.Combine(Folder, "DeltaSamples.jsonl"), JsonConvert.SerializeObject(new { label, repeat, rawMessages = messages.Count, encodedBytes = deltaBytes, observedSeconds = 1,
                        caveat = "includes initial apply burst; no production coalescer; header repeated in embedded Buff envelopes" }) + "\n");
                    foreach (var loader in group) loader.GetComponent<PNBuffManager>().Publisher.Message -= messages.Add;
                    residentPeak = Math.Max(residentPeak, group.Count);
                    Measure("unload-total", () => { foreach (var loader in group) { loader.Unload(); loader.gameObject.SetActive(false); } return 0; });
                    if (EveRegistrySession.Context.Entities.Count != 0) throw new Exception("Directory not empty after unload.");
                }
                foreach (var loader in group) { roots.Remove(loader.gameObject); UnityEngine.Object.DestroyImmediate(loader.gameObject); }
            }
            // Real current-loader resident replacement cost. Not a Prepared/Commit measurement.
            actors = 64; buffs = 0; repeat = 0; label = "resident-2N-current-loader-control";
            var old = Enumerable.Range(0, 64).Select(i => Create(i % 4 == 0)).ToArray();
            var replacement = Enumerable.Range(0, 64).Select(i => Create(i % 4 == 0)).ToArray();
            Measure("load-2N-control", () => { foreach (var loader in old.Concat(replacement)) loader.Load(hub); return 0; });
            residentPeak = Math.Max(residentPeak, EveRegistrySession.Context.Entities.Count);
            if (residentPeak != 128) throw new Exception("2N control did not retain 128 current-loader objects.");
            Measure("unload-2N-control", () => { foreach (var loader in old.Concat(replacement)) loader.Unload(); return 0; });
        }
        finally
        {
            try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); }
            finally { foreach (var root in roots) if (root != null) UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(preview); }
            File.WriteAllText(Path.Combine(Folder, "NativeSamples.json"), JsonConvert.SerializeObject(samples, Formatting.Indented));
            if (original.isDirty != dirty) throw new Exception("Authored scene dirty state changed.");
        }
        var provenance = new { fixtureVersion = 1, unity = Application.unityVersion, backend = "Unity Editor Mono",
            machine = SystemInfo.processorType, memoryMiB = SystemInfo.systemMemorySize, rules = "project_n:66", modApi = 5,
            contentFingerprint = Convert.ToBase64String(ModelCodec.Hash(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(compatibility.ContentHashes.OrderBy(x => x.Key))))),
            enabledMods = Array.Empty<string>(), allocationCounterAvailable, actorMix = "25% PNPlayerStat+Ability, 75% PNEnemyStat (N=1 is a player); TimeScale/Control/Buff; headless presentation; no Move/Skill/visual prefab",
            counts = new[] { 1, 16, 64 }, buffs = new[] { 0, 4 }, repeats = 3, residentPeak,
            caveat = "first-touch is not asset-cold; old synchronous loader only; captures current directory/Buff presentation, not full world/Stat/Move; no new commit, capture boundary, transport or production limits measured" };
        File.WriteAllText(Path.Combine(Folder, "Provenance.json"), JsonConvert.SerializeObject(provenance, Formatting.Indented));
        string result = "PASS native baseline: " + samples.Count + " timing samples; resident control peak=" + residentPeak + "; directory/session/preview cleaned; no production policy selected";
        File.WriteAllText(Path.Combine(Folder, "NativeResult.txt"), result); return result;
    }
}

// Actual Editor Play frame sample. Inclusive markers are retained raw; no overlapping timer subtraction.
public sealed class PNLifecycleFrameBaselineReview : MonoBehaviour
{
    private const string LaunchKey = "PNLifecycleBaseline.Launch", AuditKey = "PNLifecycleBaseline.Audit";
    private ProfilerRecorder main, wait, alloc;
    private readonly List<object> frames = new List<object>();
    private Scene scene;
    public static string Launch()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Idle Edit required.");
        var active = SceneManager.GetActiveScene(); if (active.isDirty) throw new InvalidOperationException("Fixture does not save a dirty authored scene.");
        Directory.CreateDirectory(PNLifecycleBaselineReview.Folder);
        SessionState.SetString(AuditKey, active.path + "|" + active.isDirty + "|" + Time.timeScale + "|" + QualitySettings.vSyncCount + "|" + Application.targetFrameRate);
        SessionState.SetBool(LaunchKey, true); EditorApplication.EnterPlaymode(); return "Entering Play for 180 raw baseline frame samples";
    }
    [InitializeOnLoadMethod] private static void Register()
    { EditorApplication.playModeStateChanged -= Changed; EditorApplication.playModeStateChanged += Changed; }
    private static void Changed(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(LaunchKey, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += () =>
        {
            var preview = SceneManager.CreateScene("Disposable lifecycle frame baseline");
            var root = new GameObject("Lifecycle baseline runner"); SceneManager.MoveGameObjectToScene(root, preview);
            root.AddComponent<PNLifecycleFrameBaselineReview>().scene = preview;
        };
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(LaunchKey, false); var active = SceneManager.GetActiveScene();
            File.WriteAllText(Path.Combine(PNLifecycleBaselineReview.Folder, "FrameAudit.txt"), "before=" + SessionState.GetString(AuditKey, "") + "\nafter=" + active.path + "|" + active.isDirty + "|" + Time.timeScale + "|" + QualitySettings.vSyncCount + "|" + Application.targetFrameRate +
                "\nGame/Simulator and Play Maximized untouched; authored scene not saved or explicitly reloaded; temporary runners=" + Resources.FindObjectsOfTypeAll<PNLifecycleFrameBaselineReview>().Length);
        }
    }
    private IEnumerator Start()
    {
        main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
        wait = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "WaitForTargetFPS", 1);
        alloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
        for (int i = 0; i < 30; i++) yield return null;
        long previous = Stopwatch.GetTimestamp();
        for (int i = 0; i < 180; i++)
        {
            yield return null; long now = Stopwatch.GetTimestamp();
            frames.Add(new { frame = i, wallMs = (now - previous) * 1000d / Stopwatch.Frequency,
                mainThreadInclusiveMs = main.Valid ? main.LastValue / 1000000d : -1,
                waitForTargetFPSMs = wait.Valid ? wait.LastValue / 1000000d : -1,
                gcBytes = alloc.Valid ? alloc.LastValue : -1 }); previous = now;
        }
        File.WriteAllText(Path.Combine(PNLifecycleBaselineReview.Folder, "PlayFrames.json"), JsonConvert.SerializeObject(new {
            unity = Application.unityVersion, scene = SceneManager.GetActiveScene().path, session = EveRegistrySession.State.ToString(),
            readyActors = EveRegistrySession.IsReady ? EveRegistrySession.Context.Entities.Snapshot().Count : 0,
            caveat = "authored scene baseline, no actor swarm; inclusive main/wall timers can include idle/editor; no active critical-path bound inferred", frames }, Formatting.Indented));
        main.Dispose(); wait.Dispose(); alloc.Dispose();
        File.WriteAllText(Path.Combine(PNLifecycleBaselineReview.Folder, "FrameResult.txt"), "PASS 180 actual Play frame samples; raw inclusive timings only, not production baseline gate");
        SceneManager.UnloadSceneAsync(scene); EditorApplication.delayCall += EditorApplication.ExitPlaymode;
    }
    private void OnDestroy() { main.Dispose(); wait.Dispose(); alloc.Dispose(); }
}
#endif
