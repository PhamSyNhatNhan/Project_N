#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using EveEngine;
using UnityEngine;

// Puppet strings drawn by PNVisualStringSystem: frames (attached, lowering, cut, hidden, facing left, run-time API with a blend), ONE draw for every string, CPU per frame, allocation,
// cleanup. Frames go to G:/ProjectN_Camera/VisualGpu/Strings and are looked at by a person.
public static class PNVisualStringsCheck
{
    private const string Output = "G:/ProjectN_Camera/VisualGpu/Strings";
    private const int W = 1920, H = 1080;
    private static int checks;
    private static void Require(bool condition, string message) { if (!condition) throw new Exception("Strings: " + message); checks++; }

    private static Texture2D Shot(PNVisualBakeCheck.Rig rig, string name, Rect region)
    {
        PNVisualStringSystem.Draw(); rig.Camera.Render(); var prev = RenderTexture.active; RenderTexture.active = rig.Target;
        var t = new Texture2D((int)region.width, (int)region.height, TextureFormat.RGBA32, false); t.ReadPixels(region, 0, 0); t.Apply(); RenderTexture.active = prev;
        if (name != null) File.WriteAllBytes(Output + "/" + name + ".png", t.EncodeToPNG());
        return t;
    }
    private static double Mad(Texture2D a, Texture2D b)
    {
        var pa = a.GetPixels32(); var pb = b.GetPixels32(); double sum = 0; int n = 0;
        for (int i = 0; i < pa.Length; i++) { int d = Math.Abs(pa[i].r - pb[i].r) + Math.Abs(pa[i].g - pb[i].g) + Math.Abs(pa[i].b - pb[i].b); if (d > 0) { sum += d / 3.0; n++; } }
        return n == 0 ? 0 : sum / n;
    }
    private static int Differing(Texture2D a, Texture2D b)
    {
        var pa = a.GetPixels32(); var pb = b.GetPixels32(); int n = 0;
        for (int i = 0; i < pa.Length; i++) if (Math.Abs(pa[i].r - pb[i].r) + Math.Abs(pa[i].g - pb[i].g) + Math.Abs(pa[i].b - pb[i].b) > 12) n++;
        return n;
    }
    private static void Step(PNVisualBakeCheck.Rig rig, float dt) { foreach (var a in rig.Actors) a.Tick(dt); }
    private static void Close(PNVisualBakeCheck.Rig rig) { rig.Camera.orthographicSize = .9f; rig.Camera.transform.position = new Vector3(.2f, .6f, -5); }
    private static void Game(PNVisualBakeCheck.Rig rig) { rig.Camera.orthographicSize = 2.04f; rig.Camera.transform.position = new Vector3(0, 0, -5); }

    public static string Run()
    {
        Directory.CreateDirectory(Output); checks = 0; var sb = new StringBuilder(); PNVisualSystem.Prune();
        var oldInk = PNVisualPaperSettings.Ink; var oldMode = PNVisualPaperSettings.Mode;
        try
        {
            PNVisualPaperSettings.Ink = PNInkMode.Baked;
            using (var rig = new PNVisualBakeCheck.Rig(true, 1, W, H))
            {
                var actor = rig.Actors[0]; PNVisualStringSystem.CameraOverride = rig.Camera; PNVisualStringSystem.FixedTime = 1.2f; PNVisualPaperSettings.Mode = PNPaperShadowMode.Off; actor.Tick(0);
                Require(actor.HasStrings && actor.Strings.Count == 2, "the Starweaver comes with its two strings (head and tassel)");
                Require(actor.TryGetSocketLocal("head_top", out _) && actor.TryGetSocketLocal("tail_tip", out _), "both sockets exist on the puppet");

                // ---- attached ----
                actor.HideStrings(); Close(rig); var closeNone = Shot(rig, null, new Rect(0, 0, W, H));
                actor.ShowStrings(); var closeOn = Shot(rig, "1_attached_close", new Rect(0, 0, W, H));
                Require(PNVisualStringSystem.LastStrings == 2 && PNVisualStringSystem.LastVertices >= 2 * 2 * 25, "two strings were built into the batch: " + PNVisualStringSystem.LastStrings + " strings, " + PNVisualStringSystem.LastVertices + " vertices");
                Require(PNVisualStringSystem.Renderer != null && PNVisualStringSystem.Renderer.enabled, "the batch renderer is on");
                int differing = Differing(closeNone, closeOn); sb.AppendLine("attached close-up: " + differing + " pixels differ from the no-strings frame");
                Require(differing > 800, "the strings are visible in the frame (" + differing + " pixels)");
                Game(rig); Shot(rig, "1_attached_game", new Rect(0, 0, W, H));
                actor.SetFacingLeft(true); actor.Tick(0); Close(rig); Shot(rig, "1_attached_left_close", new Rect(0, 0, W, H));
                actor.SetFacingLeft(false); actor.Tick(0);

                // ---- lowering (three moments), cut (three moments), hidden ----
                Close(rig);
                actor.LowerStrings(1f); foreach (var moment in new[] { .25f, .3f, .3f }) { actor.Tick(moment); Shot(rig, "2_lowering_" + Mathf.RoundToInt(actor.Strings.State(0).PhaseTime * 100) + "cs", new Rect(0, 0, W, H)); }
                Step(rig, .5f); Require(actor.Strings.State(0).Phase == PNVisualStringPhase.Attached, "the lowered strings end attached");
                actor.CutStrings(); foreach (var moment in new[] { .15f, .2f, .2f }) { actor.Tick(moment); Shot(rig, "3_cut_" + Mathf.RoundToInt(actor.Strings.State(0).PhaseTime * 100) + "cs", new Rect(0, 0, W, H)); }
                Step(rig, 1f); var hidden = Shot(rig, "4_hidden", new Rect(0, 0, W, H));
                Require(actor.Strings.State(0).Phase == PNVisualStringPhase.Hidden && PNVisualStringSystem.LastStrings == 0, "the cut strings end hidden and the batch is empty");
                Require(PNVisualStringSystem.Renderer == null || !PNVisualStringSystem.Renderer.enabled, "a hidden string switches the batch renderer off (no draw)");
                actor.ShowStrings(); Step(rig, 0);

                // ---- run-time API ----
                var hub = rig.Hub; EveId Id(string n) => EveId.Parse("project_n:" + n);
                int third = actor.AddString(new PNVisualStringEntry("feet", null, Id("slack"), Id("dark_cord"), 0f, 0f, 0f), .6f);
                Require(third == 2 && actor.Strings.Count == 3, "a third string can be added at run time");
                Step(rig, .7f); Shot(rig, "5_api_added", new Rect(0, 0, W, H));
                actor.SetStringsColor(Id("dark_cord"), 1f); Step(rig, .5f); var mid = Shot(rig, "5_api_color_mid", new Rect(0, 0, W, H));
                Step(rig, .6f); var done = Shot(rig, "5_api_color_done", new Rect(0, 0, W, H));
                Require(Mad(mid, done) > 0 && Differing(closeOn, done) > 400, "changing the colour blends (the mid frame differs from the end) and the end differs from the white strings");
                actor.SetStringShape(0, Id("drape"), .6f); Step(rig, .8f); Shot(rig, "5_api_shape", new Rect(0, 0, W, H));
                actor.RemoveString(third, true); Step(rig, .3f); Require(actor.Strings.Count == 3, "a removed string falls away first (the handle is busy until it is gone)");
                Step(rig, 1f); Require(actor.Strings.Count == 2, "and then the handle is free");
                PNVisualStringSet.ResetWarnings(); int warnings = 0; Application.LogCallback count = (c, st, t) => { if (t == LogType.Warning && (c.Contains("at most 4 strings") || c.Contains("invalid handle"))) warnings++; };
                Application.logMessageReceived += count;
                try
                {
                    int a1 = actor.AddString(new PNVisualStringEntry("head_top", EveId.Parse("project_n:puppet_main"), null, null)); int a2 = actor.AddString(new PNVisualStringEntry("head_top", EveId.Parse("project_n:puppet_main"), null, null));
                    int overflow = actor.AddString(new PNVisualStringEntry("head_top", EveId.Parse("project_n:puppet_main"), null, null));
                    Require(a1 >= 0 && a2 >= 0 && overflow == -1, "a fifth string is refused (limit 4)");
                    actor.SetStringColor(99, Id("silver"));
                }
                finally { Application.logMessageReceived -= count; }
                Require(warnings == 2, "the overflow and the invalid handle each warn once (" + warnings + ")");
            }
        }
        finally { PNVisualStringSystem.CameraOverride = null; PNVisualStringSystem.FixedTime = -1f; PNVisualPaperSettings.Ink = oldInk; PNVisualPaperSettings.Mode = oldMode; }
        Require(PNVisualSystem.Count == 0 && PNVisualStringSystem.Renderer == null, "after the last puppet the strings host is released");

        // ---- crowds: one draw for every string, CPU per frame, no allocation ----
        foreach (int count in new[] { 16, 112 })
            using (var rig = new PNVisualBakeCheck.Rig(true, count, W, H))
            {
                PNVisualStringSystem.CameraOverride = rig.Camera; PNVisualStringSystem.FixedTime = 1f; PNVisualPaperSettings.Mode = PNPaperShadowMode.Off; Game(rig);
                for (int i = 0; i < count; i++) { rig.Actors[i].GetComponent<PNCameraFacingTarget>().Align(); rig.Actors[i].transform.position = new Vector3(-3.3f + 6.6f * ((i * 0.618f) % 1f), -.9f + .12f * (i % 9), 0); rig.Actors[i].Tick(.1f); }
                PNVisualStringSystem.Draw();
                Require(PNVisualStringSystem.LastStrings == count * 2, count + " puppets: all " + count * 2 + " strings are in the batch (" + PNVisualStringSystem.LastStrings + ")");
                int renderers = Resources.FindObjectsOfTypeAll<MeshRenderer>().Count(r => r != null && r.sharedMaterial != null && r.sharedMaterial.name == "PN Visual Strings");
                Require(renderers == 1, count + " puppets: ONE renderer draws every string (" + renderers + ")");
                Shot(rig, "6_crowd_" + count, new Rect(0, 0, W, H));
                for (int i = 0; i < 20; i++) { Step(rig, .016f); PNVisualStringSystem.Draw(); }
                var clock = Stopwatch.StartNew(); for (int i = 0; i < 100; i++) PNVisualStringSystem.Draw(); double ms = clock.Elapsed.TotalMilliseconds / 100;
                long start = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 300; i++) { Step(rig, .016f); PNVisualStringSystem.Draw(); }
                long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
                sb.AppendLine(count + " puppets (" + PNVisualStringSystem.LastStrings + " strings, " + PNVisualStringSystem.LastVertices + " vertices): strings build " + ms.ToString("0.000") + " ms per frame; allocated over 300 frames (tick + draw): " + bytes + " bytes");
                Require(bytes == 0, count + " puppets: no allocation per frame (" + bytes + " bytes)");
                PNVisualStringSystem.CameraOverride = null; PNVisualStringSystem.FixedTime = -1f;
            }
        Require(PNVisualSystem.Count == 0, "no actors left registered");
        return "PASS " + checks + " strings checks\n" + sb;
    }
}
#endif
