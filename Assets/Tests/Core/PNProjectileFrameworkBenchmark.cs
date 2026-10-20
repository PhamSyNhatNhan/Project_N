using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

// EN: Opt-in standalone Player benchmark of the real projectile framework (the game's own manager, pool, oriented-box contacts, BoxCast,
//     homing). It is not production code. Enabled only by the command-line flag -pn-projectile-framework-benchmark.
// VI: Benchmark Player tường minh cho framework đạn thật (manager của chính game); chỉ bật bằng cờ dòng lệnh.
public sealed class PNProjectileFrameworkBenchmark : MonoBehaviour
{
    private const int Warmup = 30, Samples = 240;
    private static readonly double TickMs = 1000d / Stopwatch.Frequency;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!Environment.GetCommandLineArgs().Contains("-pn-projectile-framework-benchmark")) return;
        var host = new GameObject("PNProjectileFrameworkBenchmark"); DontDestroyOnLoad(host); host.AddComponent<PNProjectileFrameworkBenchmark>();
    }
    private static string Arg(string name) => Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith(name, StringComparison.Ordinal))?.Substring(name.Length);
    private IEnumerator Start()
    {
        string output = Arg("-pn-projectile-framework-output=") ?? "ProjectileFrameworkResult.json";
        var lines = new List<string>(); Exception failure = null;
        yield return RunAll(lines, e => failure = e);
        var json = new StringBuilder();
        json.Append("{\"unity\":\"").Append(Application.unityVersion).Append("\",\"cpu\":\"").Append(SystemInfo.processorType).Append("\",\"cores\":").Append(SystemInfo.processorCount)
            .Append(",\"development\":").Append(UnityEngine.Debug.isDebugBuild ? "true" : "false").Append(",\"batch\":").Append(Application.isBatchMode ? "true" : "false")
            .Append(",\"caveats\":\"Mono normal Player (no IL2CPP), the game's own runtime session in an empty scene, 64 box targets, 4 walls, uncapped frame rate with Time.captureFramerate=60 (dt 1/60). Times are manager.Advance only (clock, sub-steps, contacts, hits, BoxCast, candidate refresh, pooling); top-up creation is timed separately. No rendering of projectiles, no damage in the launches, definitions are the shipped defaults (hitLimit 1, so a hit ends a projectile and the top-up replaces it). Not a mobile measurement.\"");
        json.Append(",\"error\":").Append(failure == null ? "null" : "\"" + failure.ToString().Replace("\"", "'").Replace("\n", " ").Replace("\r", " ") + "\"").Append(",\"scenarios\":[").Append(string.Join(",", lines)).Append("]}");
        File.WriteAllText(output, json.ToString());
        Application.Quit(failure == null ? 0 : 1);
    }
    private IEnumerator RunAll(List<string> lines, Action<Exception> fail)
    {
        EveGameRuntime runtime = PNGameBootstrap.Runtime; Scene scene = default; var objects = new List<GameObject>(); PNProjectileManager manager = null; bool began = false;
        Exception error = null;
        if (runtime == null) { fail(new InvalidOperationException("The game runtime was not created by the bootstrap.")); yield break; }
        try { runtime.BeginSession(EveSessionRole.Offline); began = true; } catch (Exception e) { fail(e); yield break; }
        for (int i = 0; i < 600 && !EveRegistrySession.IsReady; i++) yield return null;
        if (!EveRegistrySession.IsReady) { fail(new InvalidOperationException("The session did not become ready.")); yield break; }
        Time.captureFramerate = 60;
        EveHotPathMetrics.Enabled = Environment.GetCommandLineArgs().Contains("-pn-projectile-metrics"); EveHotPathMetrics.TraceAllocations = Environment.GetCommandLineArgs().Contains("-pn-projectile-alloc");
        try
        {
            var hub = EveRegistrySession.Current; var session = EveRegistrySession.Context;
            manager = runtime.GetComponent<PNProjectileManager>();
            if (manager == null) throw new InvalidOperationException("The runtime has no projectile manager: BindSession wiring is missing.");
            manager.ManualAdvance = true;
            scene = SceneManager.CreateScene("ProjectileBenchmark", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            int enemy = LayerMask.NameToLayer("Enemy"), wall = LayerMask.NameToLayer("Wall");
            // 64 targets on an 8x8 grid inside a 36x36 arena, ringed by four walls.
            for (int i = 0; i < 64; i++)
            {
                var obj = new GameObject("target " + i); obj.SetActive(false); SceneManager.MoveGameObjectToScene(obj, scene);
                obj.transform.position = new Vector3(-14f + (i % 8) * 4f, 0, -14f + (i / 8) * 4f); obj.layer = enemy;
                obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(995000 + i), EveId.Parse("project_n:dummy"), 0);
                obj.AddComponent<PNEnemyStat>(); obj.AddComponent<BoxCollider>().size = Vector3.one; obj.AddComponent<EveEntityLoader>().Load(hub); obj.SetActive(true); objects.Add(obj);
            }
            foreach (var spec in new[] { (new Vector3(0, 0, 18), new Vector3(40, 4, 1)), (new Vector3(0, 0, -18), new Vector3(40, 4, 1)), (new Vector3(18, 0, 0), new Vector3(1, 4, 40)), (new Vector3(-18, 0, 0), new Vector3(1, 4, 40)) })
            {
                var obj = new GameObject("wall"); SceneManager.MoveGameObjectToScene(obj, scene); obj.layer = wall; obj.transform.position = spec.Item1; obj.AddComponent<BoxCollider>().size = spec.Item2; objects.Add(obj);
            }
            Physics.SyncTransforms();
            var source = new PNBuffSource(session.SessionId, new EveEntityId(990001));
            var scenarios = new[]
            {
                new Scenario { Name = "directional_1000", Id = "project_n:directional", Count = 1000, Cap = 1000 },
                new Scenario { Name = "directional_500_mobile_cap", Id = "project_n:directional", Count = 500, Cap = 500 },
                new Scenario { Name = "homing_1000", Id = "project_n:homing", Count = 1000, Cap = 1000 },
            };
            var random = new System.Random(12345); const float dt = 1f / 60f;
            foreach (var scenario in scenarios)
            {
                manager.Initialize(session, new PNProjectileProfile { MaxActive = scenario.Cap });
                var times = new List<double>(Samples); var creates = new List<double>(Samples); int created = 0, rejected = 0; long activeSum = 0; long gcBefore = 0; int gen0 = 0;
                for (int frame = 0; frame < Warmup + Samples; frame++)
                {
                    if (frame == Warmup) { gcBefore = GC.GetTotalMemory(false); gen0 = GC.CollectionCount(0); }
                    long start = Stopwatch.GetTimestamp();
                    int topUp = scenario.Count - manager.ActiveCount;
                    for (int i = 0; i < topUp; i++)
                    {
                        var position = new Vector3((float)(random.NextDouble() * 30 - 15), 0, (float)(random.NextDouble() * 30 - 15));
                        double angle = random.NextDouble() * Math.PI * 2; var direction = new Vector3((float)Math.Cos(angle), 0, (float)Math.Sin(angle));
                        var receipt = manager.Create(new PNProjectileLaunch(EveId.Parse(scenario.Id), null, source, 1 << enemy), new PNProjectileSpawn(scene, position, direction));
                        if (receipt.Status == PNProjectileStatus.Created) created++; else rejected++;
                    }
                    double createMs = (Stopwatch.GetTimestamp() - start) * TickMs;
                    long advanceStart = Stopwatch.GetTimestamp(); manager.Advance(dt); double advanceMs = (Stopwatch.GetTimestamp() - advanceStart) * TickMs;
                    if (frame >= Warmup) { times.Add(advanceMs); creates.Add(createMs); activeSum += manager.ActiveCount; }
                    yield return null;
                }
                times.Sort(); creates.Sort();
                double Pct(List<double> v, double p) => v[Math.Min(v.Count - 1, (int)(v.Count * p))];
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "{{\"name\":\"{0}\",\"cap\":{1},\"targetActive\":{2},\"avgActive\":{3:F0},\"advanceMsP50\":{4:F3},\"advanceMsP95\":{5:F3},\"advanceMsMax\":{6:F3},\"createMsP50\":{7:F3},\"createMsP95\":{8:F3},\"created\":{9},\"rejected\":{10},\"gen0Collections\":{11},\"managedDeltaKb\":{12},\"pooledObjects\":{13}}}",
                    scenario.Name, scenario.Cap, scenario.Count, activeSum / (double)Samples, Pct(times, .5), Pct(times, .95), times.Last(), Pct(creates, .5), Pct(creates, .95), created, rejected,
                    GC.CollectionCount(0) - gen0, (GC.GetTotalMemory(false) - gcBefore) / 1024, manager.PooledObjectCount));
                if (EveHotPathMetrics.Enabled)
                {
                    var metrics = EveHotPathMetrics.Drain().Where(x => x.Key.StartsWith("projectile.")).OrderBy(x => x.Key).Select(x => string.Format(CultureInfo.InvariantCulture, "\"{0}\":{{\"calls\":{1},\"totalMsPerFrame\":{2:F3},\"exclusiveMsPerFrame\":{3:F3},\"allocKbPerFrame\":{4:F2}}}", x.Key, x.Value.Calls, x.Value.Ticks * TickMs / (Warmup + Samples), x.Value.ExclusiveTicks * TickMs / (Warmup + Samples), x.Value.AllocatedBytes / 1024d / (Warmup + Samples)));
                    lines[lines.Count - 1] = lines[lines.Count - 1].TrimEnd('}') + ",\"metrics\":{" + string.Join(",", metrics) + "}}";
                }
                manager.Shutdown(); yield return null;
            }
        }
        finally
        {
            Time.captureFramerate = 0;
            foreach (var obj in objects) if (obj != null) UnityEngine.Object.Destroy(obj);
            if (began) { try { runtime.EndSession(); } catch (Exception e) { error = e; } }
        }
        if (error != null) fail(error);
    }
    private sealed class Scenario { public string Name, Id; public int Count, Cap; }
}
