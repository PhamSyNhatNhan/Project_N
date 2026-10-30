#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

//: the pre-baked overlap shadow (mode Baked) against the dynamic one (mode Actors) and no shadow (mode Off): frames, checks, and GPU/CPU timing.
public static class PNVisualShadeCheck
{
    private const string Output = "G:/ProjectN_Camera/VisualGpu/Shade";
    private const int W = 1920, H = 1080;
    private static int checks;
    private static void Require(bool condition, string message) { if (!condition) throw new Exception("Shade: " + message); checks++; }

    public static void Batch()
    {
        Directory.CreateDirectory(Output);
        try { var r = Run(); File.WriteAllText(Output + "/result.txt", r); UnityEngine.Debug.Log(r); EditorApplication.Exit(0); }
        catch (Exception e) { File.WriteAllText(Output + "/result.txt", e.ToString()); UnityEngine.Debug.LogException(e); EditorApplication.Exit(1); }
    }

    private static Texture2D Shot(PNVisualBakeCheck.Rig rig, string name, Rect region)
    {
        rig.Camera.Render(); var prev = RenderTexture.active; RenderTexture.active = rig.Target;
        var t = new Texture2D((int)region.width, (int)region.height, TextureFormat.RGBA32, false); t.ReadPixels(region, 0, 0); t.Apply(); RenderTexture.active = prev;
        File.WriteAllBytes(Output + "/" + name + ".png", t.EncodeToPNG()); return t;
    }
    private static int Diff(Texture2D a, Texture2D b)
    {
        var pa = a.GetPixels32(); var pb = b.GetPixels32(); int n = 0;
        for (int i = 0; i < pa.Length; i++) if (Math.Abs(pa[i].r - pb[i].r) + Math.Abs(pa[i].g - pb[i].g) + Math.Abs(pa[i].b - pb[i].b) > 12) n++;
        return n;
    }
    // The shade tile is another texture sampled at another sub-texel position, so alpha may differ by a few levels; the silhouette must not move.
    private static int AlphaDiff(Texture2D a, Texture2D b) { var pa = a.GetPixels32(); var pb = b.GetPixels32(); int n = 0; for (int i = 0; i < pa.Length; i++) if (Math.Abs(pa[i].a - pb[i].a) > 8) n++; return n; }

    public static string Run()
    {
        Directory.CreateDirectory(Output);
        checks = 0; var sb = new StringBuilder(); sb.AppendLine("GPU: " + SystemInfo.graphicsDeviceName + " | " + SystemInfo.graphicsDeviceType);
        // ---- frames and checks (game scale + close-up) ----
        foreach (var scale in new[] { (name: "close", ortho: .68f, w: 768, h: 1024, cam: new Vector3(.02f, .48f, -5)), (name: "game", ortho: 2.04f, w: W, h: H, cam: new Vector3(0, 0, -5)) })
        {
            using (var rig = new PNVisualBakeCheck.Rig(true, 1, scale.w, scale.h))
            {
                rig.Camera.orthographicSize = scale.ortho; rig.Camera.transform.position = scale.cam; var a = rig.Actors[0];
                var full = new Rect(0, 0, scale.w, scale.h);
                PNVisualPaperSettings.Mode = PNPaperShadowMode.Off; a.Tick(0); var off = Shot(rig, scale.name + "_off", full);
                PNVisualPaperSettings.Mode = PNPaperShadowMode.Actors; a.Tick(0); var dyn = Shot(rig, scale.name + "_dynamic", full);
                PNVisualPaperSettings.Mode = PNPaperShadowMode.Baked; a.Tick(0); var baked = Shot(rig, scale.name + "_baked", full);
                Require(a.ShadeActive && a.ShadeTiles > 0 && !a.ShadowActive, scale.name + ": Baked mode uses the shade tiles and no dynamic casters (" + a.ShadeTiles + " tiles)");
                Require(!a.Renderer.HasPropertyBlock(), scale.name + ": a pre-baked actor carries no property block");
                Require(a.Renderer.sharedMaterial.GetTexture("_ShadeTex") != null, scale.name + ": the shade texture is bound");
                int dOffDyn = Diff(off, dyn), dOffBaked = Diff(off, baked), dBakedDyn = Diff(baked, dyn);
                sb.AppendLine(scale.name + ": pixels differing: off vs dynamic " + dOffDyn + ", off vs baked " + dOffBaked + ", baked vs dynamic " + dBakedDyn);
                Require(dOffBaked > 50, scale.name + ": the baked shadow is visible against no shadow");
                if (scale.name == "game") Require(dBakedDyn < dOffDyn, scale.name + ": at the reference scale the baked shadow is closer to the dynamic one than no shadow is");   // a close-up is not comparable: the dynamic offset is in screen pixels, the baked one is fixed at the reference scale
                int alphaMoved = AlphaDiff(off, baked); sb.AppendLine(scale.name + ": pixels whose alpha differs by more than 8: " + alphaMoved); Require(alphaMoved < 120, scale.name + ": the baked shadow does not move the silhouette");
                PNVisualPaperSettings.Mode = PNPaperShadowMode.Actors; a.Tick(0); Require(a.ShadowActive && !a.ShadeActive, scale.name + ": back to dynamic");
                Require(Diff(Shot(rig, scale.name + "_dynamic2", full), dyn) < 10, scale.name + ": switching back restores the dynamic frame");
            }
        }
        // ---- GPU / CPU ----
        var table = new List<string>();
        foreach (var order in Environment.GetEnvironmentVariable("PN_BAKE_SKIP_GPU") == "1" ? new PNPaperShadowMode[0][] { } : new[] { new[] { PNPaperShadowMode.Off, PNPaperShadowMode.Actors, PNPaperShadowMode.Baked }, new[] { PNPaperShadowMode.Baked, PNPaperShadowMode.Actors, PNPaperShadowMode.Off } })
        {
            using (var rig = new PNVisualBakeCheck.Rig(true, 112, W, H))
            {
                var one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                double[] Time(int renders)
                {
                    rig.Camera.Render(); RenderTexture.active = rig.Target; one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                    var sw = Stopwatch.StartNew(); for (int r = 0; r < renders; r++) rig.Camera.Render();
                    double submit = sw.Elapsed.TotalMilliseconds; RenderTexture.active = rig.Target; one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                    return new[] { submit / renders, sw.Elapsed.TotalMilliseconds / renders };
                }
                double Median(List<double> l) { l.Sort(); return l[l.Count / 2]; }
                foreach (var scene in new[] { (n: 16, o: 2.04f, sp: .9f, name: "16 actors game scale"), (n: 112, o: 2.04f, sp: .5f, name: "112 actors game scale") })
                    foreach (var mode in order)
                    {
                        rig.Camera.orthographicSize = scene.o; float halfW = scene.o * W / H; int cols = Math.Max(1, Mathf.FloorToInt(2 * halfW / scene.sp)), rows = Math.Max(1, Mathf.FloorToInt(2 * scene.o / scene.sp));
                        PNVisualPaperSettings.Mode = mode;
                        for (int i = 0; i < rig.Actors.Count; i++)
                        {
                            var a = rig.Actors[i]; bool on = i < scene.n; a.gameObject.SetActive(on); if (!on) continue;
                            int cx = i % cols, cy = (i / cols) % rows; a.transform.position = new Vector3(-halfW + scene.sp * (cx + .5f), scene.o - scene.sp * (cy + .5f) - scene.sp * .5f, 0); a.Tick(0);
                        }
                        var sub = new List<double>(); var tot = new List<double>(); Time(4); int renders = scene.n >= 56 ? 20 : 40;
                        for (int rep = 0; rep < 7; rep++) { var t = Time(renders); sub.Add(t[0]); tot.Add(t[1]); }
                        table.Add(scene.name + " | " + mode.ToString().PadRight(7) + " | submit " + Median(sub).ToString("F3") + " | total " + Median(tot).ToString("F3"));
                    }
            }
        }
        table.Sort(); sb.AppendLine(); sb.AppendLine("GPU (ms/frame, 1080p, baked ink atlas): scene | shadow mode | CPU submit | total to GPU done (each row twice: forward and reversed)");
        foreach (var line in table) sb.AppendLine("  " + line);
        PNVisualPaperSettings.Mode = PNPaperShadowMode.Actors; PNVisualPaperSettings.BakeInk = true;
        return "PASS " + checks + " shade checks\n" + sb;
    }
}
#endif
