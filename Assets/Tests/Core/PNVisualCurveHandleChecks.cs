#if UNITY_EDITOR
using System;
using System.IO;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: Native check of the bezier curve handles (Docs/VisualCurveHandlesPlan.md H4): the test creature's `bounce` motion carries handles; the same content with the handles removed plays beside it.
//     The real actor must follow PNVisualAnimator.Sample exactly on the handled curve, overshoot where the handles say so, agree at both ends, and draw. Frames in G:/ProjectN_Camera/CurveHandles.
// VI: Kiem tra native tay nam bezier: chuyen dong `bounce` cua sinh vat thu co tay nam, ban khong tay nam chay ben canh.
public static partial class PNVisualEditorChecks
{
    // Worst case for the cost of handles: every key of every track curve in the humanoid content carries the handle [0.85, d/3, 0.15, -d/3]: cx far from the straight 1/3, 2/3 so Newton needs several steps (with 1/3, 2/3 it converges at once and the measurement would be too kind).
    // Plain and handled crowds are measured alternately (four rounds each) so drift of the machine shows up in both. Pose only (Tick without the mesh) isolates the curves; pose + mesh is the whole cost.
    public static string RunHandlePerformance(string outputDirectory = "G:/ProjectN_Camera/CurveHandles/Performance")
    {
        if (UnityEditor.EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        PNVisualSystem.Prune();
        string baseDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets", "BaseContent"));
        string json = File.ReadAllText(Path.Combine(baseDirectory, "visual", "humanoid.json"));
        var handled = JObject.Parse(json); int converted = 0, keysTotal = 0;
        foreach (JObject motion in (JArray)handled["extensions"]["project_n"]["visualMotions"])
            foreach (var bone in ((JObject)motion["tracks"]).Properties())
                foreach (var kind in ((JObject)bone.Value).Properties())
                    if (kind.Value["keys"] is JArray keys)
                        for (int i = 0; i < keys.Count; i++)
                        {
                            keysTotal++; if (i == keys.Count - 1) continue;
                            float v = keys[i][1].Type == JTokenType.String ? 0f : keys[i][1].Value<float>(), next = keys[i + 1][1].Value<float>(), d = (keys[i][1].Type == JTokenType.String ? next : next) - v;
                            ((JArray)keys[i]).Add(new JArray(.85f, d / 3f, .15f, -d / 3f)); converted++;
                        }
        var report = new System.Collections.Generic.List<string>(); Stage stage = null; var actors = new System.Collections.Generic.List<PNVisualActor>();
        (double mesh, double pose, long gc) Measure(EveRegistryHub hub, int count)
        {
            foreach (var a in actors) UnityEngine.Object.DestroyImmediate(a.transform.parent.gameObject); actors.Clear();
            var states = new[] { "walk", "run", "idle", "jump", "look", "dash" }; int columns = 16;
            stage.Camera.orthographicSize = Mathf.Max(.34f, columns * .07f * (count / columns + 1) * .5f); stage.Camera.transform.position = new Vector3(columns * .07f * .5f, stage.Camera.orthographicSize * .5f, -5f);
            for (int i = 0; i < count; i++) { var a = Spawn(stage, hub, i % 3 == 0 ? "tall" : i % 3 == 1 ? "short" : "normal", (i % columns) * .14f); a.transform.parent.position = new Vector3((i % columns) * .14f, (i / columns) * .5f, (i % 5) * .01f); a.Play(states[i % states.Length]); actors.Add(a); }
            for (int i = 0; i < 60; i++) foreach (var a in actors) a.Tick(.016f);
            long gc0 = GC.GetAllocatedBytesForCurrentThread(); const int frames = 300; var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int f = 0; f < frames; f++) { foreach (var a in actors) a.Tick(.016f); if (f % 60 == 0) for (int k = 0; k < actors.Count; k++) actors[k].Play(states[(f / 60 + k) % states.Length]); }
            sw.Stop(); long gc = GC.GetAllocatedBytesForCurrentThread() - gc0; var sw2 = System.Diagnostics.Stopwatch.StartNew();
            for (int f = 0; f < frames; f++) foreach (var a in actors) a.Tick(.016f, false);
            sw2.Stop(); return (sw.Elapsed.TotalMilliseconds / frames, sw2.Elapsed.TotalMilliseconds / frames, gc / frames);
        }
        try
        {
            stage = new Stage(); var modules = new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() };
            double[] plainMesh = new double[4], plainPose = new double[4], handledMesh = new double[4], handledPose = new double[4]; long worstGc = 0;
            for (int round = 0; round < 4; round++)
            {
                foreach (var which in new[] { false, true })
                {
                    foreach (var a in actors) UnityEngine.Object.DestroyImmediate(a.transform.parent.gameObject); actors.Clear(); PNVisualRuntime.Release(); PNVisualPacks.Clear(); PNVisualPacks.Set("project_n", baseDirectory);
                    var hub = new EveContentBootstrap().Build(modules, new[] { new EveContentDocument("visual", which ? handled.ToString() : json, "project_n") });
                    var m = Measure(hub, 128); worstGc = Math.Max(worstGc, m.gc);
                    if (which) { handledMesh[round] = m.mesh; handledPose[round] = m.pose; } else { plainMesh[round] = m.mesh; plainPose[round] = m.pose; }
                }
            }
            double Mean(double[] x) { double s = 0; foreach (var v in x) s += v; return s / x.Length; }
            report.Add(converted + " of " + keysTotal + " keys carry a handle; 128 actors, mean of 4 rounds: plain " + Mean(plainMesh).ToString("0.000") + " ms/frame (pose + mesh), " + Mean(plainPose).ToString("0.000") + " pose only; handled "
                + Mean(handledMesh).ToString("0.000") + " ms/frame (pose + mesh), " + Mean(handledPose).ToString("0.000") + " pose only; difference " + (Mean(handledMesh) - Mean(plainMesh)).ToString("0.000") + " ms/frame (pose + mesh), "
                + (Mean(handledPose) - Mean(plainPose)).ToString("0.000") + " pose only; worst GC " + worstGc + " bytes/frame");
            Require(converted > 1000, "the handled humanoid really has handles on its keys (" + converted + ")");
            Require(worstGc <= 200, "128 actors on handled curves allocate almost nothing per frame (" + worstGc + " bytes)");
            Require(Mean(handledMesh) - Mean(plainMesh) <= .3, "handles add at most 0.3 ms/frame for 128 actors (" + (Mean(handledMesh) - Mean(plainMesh)).ToString("0.000") + ")");
        }
        finally { foreach (var a in actors) if (a != null) UnityEngine.Object.DestroyImmediate(a.transform.parent.gameObject); stage?.Dispose(); PNVisualRuntime.Release(); PNVisualPacks.Clear(); }
        return string.Join(" | ", report);
    }

    public static string RunHandles(string outputDirectory = "G:/ProjectN_Camera/CurveHandles/Handles")
    {
        if (UnityEditor.EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        PNVisualSystem.Prune();
        int checks = 0; Stage stage = null; GameObject rootA = null, rootB = null;
        string baseDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets", "BaseContent"));
        string json = File.ReadAllText(Path.Combine(baseDirectory, "visual", "creature_test.json"));
        JObject WithoutHandles()
        {
            var j = JObject.Parse(json);
            foreach (JObject motion in (JArray)j["extensions"]["project_n"]["visualMotions"])
                foreach (var bone in ((JObject)motion["tracks"]).Properties())
                    foreach (var kind in ((JObject)bone.Value).Properties())
                        if (kind.Value["keys"] is JArray keys) foreach (JArray key in keys) while (key.Count > 2) key.RemoveAt(key.Count - 1);
            return j;
        }
        try
        {
            stage = new Stage(); stage.Camera.orthographicSize = .5f; stage.Camera.transform.position = new Vector3(0f, .3f, -5f);
            var hubB = LoadWithDocuments(baseDirectory, ("creature", WithoutHandles().ToString())); var actorB = SpawnAppearance(stage, hubB, "project_n:creature_test", out rootB, 3f);
            var hubA = LoadWithDocuments(baseDirectory, ("creature", json)); var actorA = SpawnAppearance(stage, hubA, "project_n:creature_test", out rootA);
            var rig = hubA.Get<PNVisualRigDefinition>().Get(EveId.Parse("project_n:creature_test")).Resolve(id => hubA.Get<PNVisualRigDefinition>().Get(id));
            int body = rig.BoneIndex("body"); float ppm = rig.Definition.PixelsPerMeter;
            PNVisualCurve curve = null;
            foreach (var track in hubA.Get<PNVisualMotionDefinition>().Get(EveId.Parse("project_n:creature_test_bounce")).Tracks) if (track.Bone == "body" && track.Kind == PNVisualTrackKind.Y) curve = track.Curve;
            Require(curve != null && curve.Handles != null && curve.HasHandle[0] && curve.HasHandle[1] && !curve.HasHandle[2], "the bounce motion's body y curve carries two handles"); checks++;
            var plain = hubB.Get<PNVisualMotionDefinition>().Get(EveId.Parse("project_n:creature_test_bounce")); PNVisualCurve plainCurve = null;
            foreach (var track in plain.Tracks) if (track.Bone == "body" && track.Kind == PNVisualTrackKind.Y) plainCurve = track.Curve;
            Require(plainCurve != null && plainCurve.Handles == null, "the same content without handles has none"); checks++;
            foreach (var actor in new[] { actorA, actorB }) { actor.Play("bounce", true); actor.Animator.Evaluate(); }
            float YOf(PNVisualActor actor) => -actor.Animator.BoneAffine[body * 6 + 5] * ppm + 250f;       // the body's y offset in art px (its rest joint is at y = -250)
            // follow the motion frame by frame; compare the real actor with Sample on the same curve
            float time = 0f, worst = 0f, peakA = float.MinValue, peakB = float.MinValue; var checkAt = new[] { .1f, .25f, .4f, .5f, .7f, 1f }; int next = 0; Vector3[] midA = null, midB = null;
            for (int i = 0; i < 60; i++)
            {
                actorA.Tick(1f / 60f); actorB.Tick(1f / 60f); time += 1f / 60f;
                float want = PNVisualAnimator.Sample(curve, Mathf.Min(time, 1f), 0f), got = YOf(actorA);
                worst = Mathf.Max(worst, Mathf.Abs(got - want)); peakA = Mathf.Max(peakA, got); peakB = Mathf.Max(peakB, YOf(actorB));
                if (next < checkAt.Length && time >= checkAt[next] - 1e-4f) { if (checkAt[next] == .25f) { midA = actorA.Mesh.vertices; midB = actorB.Mesh.vertices; stage.Capture(outputDirectory + "/bounce_quarter.png"); } next++; }
            }
            Require(worst < .05f, "the actor's body follows Sample on the handled curve frame by frame (largest miss " + worst.ToString("0.0000") + " px)"); checks++;
            Require(peakA > 125f && peakB <= 120.1f, "the handles overshoot the low key (peak " + peakA.ToString("0.0") + " px) where the same motion without them never passes it (" + peakB.ToString("0.0") + ")"); checks++;
            int differing = 0; float gap = midA != null ? MaxDiff(midA, midB, out differing, 1e-3f) : 0f;
            Require(gap > .003f && differing > 4, "mid-motion the mesh differs from the version without handles (" + differing + " vertices, up to " + (gap * 1000f).ToString("0.0") + " mm)"); checks++;
            Require(Mathf.Abs(YOf(actorA) - 100f) < .05f && Mathf.Abs(YOf(actorB) - 100f) < .05f, "both end on the last key"); checks++;
            actorA.Play("bounce", true); actorA.Animator.Evaluate(); Settle(actorA, .4f); stage.Capture(outputDirectory + "/bounce_overshoot.png");
            Require(stage.Painted() > 9000, "the creature paints while bouncing"); checks++;
            // no allocation per frame while a handled curve plays
            actorA.Play("bounce", true); for (int i = 0; i < 100; i++) actorA.Tick(.016f);
            long gc0 = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 600; i++) { if (i % 60 == 0) actorA.Play("bounce", true); actorA.Tick(.016f); }
            Require((GC.GetAllocatedBytesForCurrentThread() - gc0) / 600 <= 200, "a handled motion allocates almost nothing per frame"); checks++;
        }
        finally
        {
            foreach (var go in new[] { rootA, rootB }) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            stage?.Dispose(); PNVisualRuntime.Release(); PNVisualPacks.Clear();
        }
        Require(PNVisualSystem.Count == 0, "no puppet left registered"); checks++;
        return "PASS " + checks + " Visual curve handle checks (frames in " + outputDirectory + ")";
    }
}
#endif
