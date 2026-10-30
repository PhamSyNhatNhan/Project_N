#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using EveEngine;
using UnityEngine;

// The ink look is a setting: baked into the atlas or live. This checks the atlas SET (baked + raw), the rebuild with its epoch, the re-attach of running actors, the key-actor flip,
// the shadow choices on both flavours, pooled reuse after a rebuild, cleanup of the old resources and allocations. Frames go to G:/ProjectN_Camera/VisualGpu/Rebind.
public static class PNVisualRebindCheck
{
    private const string Output = "G:/ProjectN_Camera/VisualGpu/Rebind";
    private const int W = 1920, H = 1080;
    private static int checks;
    private static void Require(bool condition, string message) { if (!condition) throw new Exception("Rebind: " + message); checks++; }
    private static readonly Rect Left = new Rect(250, 200, 360, 700), Middle = new Rect(790, 200, 340, 700), Right = new Rect(1310, 200, 360, 700);

    private static Texture2D Shot(PNVisualBakeCheck.Rig rig, string name, Rect region)
    {
        rig.Camera.Render(); var prev = RenderTexture.active; RenderTexture.active = rig.Target;
        var t = new Texture2D((int)region.width, (int)region.height, TextureFormat.RGBA32, false); t.ReadPixels(region, 0, 0); t.Apply(); RenderTexture.active = prev;
        if (name != null) File.WriteAllBytes(Output + "/" + name + ".png", t.EncodeToPNG());
        return t;
    }
    private static double Mad(Texture2D a, Texture2D b)
    {
        var pa = a.GetPixels32(); var pb = b.GetPixels32(); double sum = 0; int n = 0;
        for (int i = 0; i < pa.Length; i++) { int d = Math.Abs(pa[i].r - pb[i].r) + Math.Abs(pa[i].g - pb[i].g) + Math.Abs(pa[i].b - pb[i].b); if (d > 0 || pa[i].r > 20) { sum += d / 3.0; n++; } }
        return n == 0 ? 0 : sum / n;
    }
    private static void Rebuild(PNVisualBakeCheck.Rig rig)
    {
        PNVisualRuntime.BeginRebuild(rig.Hub); int steps = 0;
        Require(PNVisualRuntime.RebuildActive && PNVisualRuntime.RebuildTotal > 0, "a rebuild is queued");
        while (!PNVisualRuntime.RebuildStep(3)) { steps++; Require(steps < 5000, "the rebuild finishes"); }
        Require(!PNVisualRuntime.RebuildActive, "the rebuild is done");
    }
    private static int AtlasTextures() => Resources.FindObjectsOfTypeAll<Texture2D>().Count(t => t != null && t.width == 2048 && t.height == 1024);
    private static bool Baked(PNVisualActor a) => a.Renderer.sharedMaterial.IsKeywordEnabled("PN_BAKED");

    public static string Run()
    {
        Directory.CreateDirectory(Output); checks = 0; var sb = new StringBuilder(); PNVisualSystem.Prune();
        var oldInk = PNVisualPaperSettings.Ink; var oldMode = PNVisualPaperSettings.Mode; bool oldBake = PNVisualPaperSettings.BakeInk;
        try
        {
            PNVisualPaperSettings.Ink = PNInkMode.Baked;
            using (var rig = new PNVisualBakeCheck.Rig(true, 3, W, H))
            {
                rig.Camera.orthographicSize = 2.04f;
                float[] xs = { -2f, 0f, 2f };
                for (int i = 0; i < 3; i++) { rig.Actors[i].GetComponent<PNCameraFacingTarget>().Align(); rig.Actors[i].transform.position = new Vector3(xs[i], 0, 0); rig.Actors[i].SetFlash(.0f); }
                rig.Actors[1].KeyActor = true; rig.Actors[1].SetFade(.85f);
                foreach (var a in rig.Actors) { a.Play("idle", true); a.Tick(.55f); }
                Require(rig.Actors.All(a => !a.LiveInk && Baked(a)), "baked ink: every actor on the baked atlas");
                for (int i = 0; i < 3; i++) sb.AppendLine("actor " + i + " at " + rig.Actors[i].transform.position + " bounds " + rig.Actors[i].Renderer.bounds.center + " size " + rig.Actors[i].Renderer.bounds.size);
                int epoch0 = PNVisualRuntime.Epoch; int tex0 = AtlasTextures();
                var baseSocket = new Vector3[3]; for (int i = 0; i < 3; i++) { Require(rig.Actors[i].TryGetSocketLocal("head_top", out baseSocket[i]) || rig.Actors[i].Animator.SocketPosition.Length > 0, "a socket is readable"); }
                var animPose = rig.Actors.Select(a => a.Animator.SocketPosition.ToArray()).ToArray();
                var before = new[] { Shot(rig, "1_baked_left", Left), Shot(rig, "1_baked_middle", Middle), Shot(rig, "1_baked_right", Right) };
                Shot(rig, "1_baked_full", new Rect(0, 0, W, H));

                // ---- live: player and bosses ----
                PNVisualPaperSettings.Ink = PNInkMode.LiveKeyActors; Rebuild(rig);
                Require(PNVisualRuntime.Epoch == epoch0 + 1, "the epoch moved by one");
                Require(rig.Actors.All(a => a.AtlasEpoch == PNVisualRuntime.Epoch), "every actor is on the new epoch");
                Require(!rig.Actors[0].LiveInk && Baked(rig.Actors[0]) && rig.Actors[1].LiveInk && !Baked(rig.Actors[1]) && !rig.Actors[2].LiveInk, "only the key actor is live under live key actors");
                for (int i = 0; i < 3; i++) { var now = rig.Actors[i].Animator.SocketPosition; for (int k = 0; k < now.Length; k++) Require(Mathf.Abs(now[k] - animPose[i][k]) < 1e-5f, "pose unchanged by the rebuild (actor " + i + ")"); }
                var live = new[] { Shot(rig, "2_livekey_left", Left), Shot(rig, "2_livekey_middle", Middle), Shot(rig, "2_livekey_right", Right) };
                Shot(rig, "2_livekey_full", new Rect(0, 0, W, H));
                double dl = Mad(before[0], live[0]), dr = Mad(before[2], live[2]), dm = Mad(before[1], live[1]);
                sb.AppendLine("live key actors: MAD left " + dl.ToString("0.00") + " | middle (key, live) " + dm.ToString("0.00") + " | right " + dr.ToString("0.00"));
                Require(dl < .5 && dr < .5, "non-key actors look the same after the rebuild");
                Require(dm > 0, "the key actor now draws its ink live");
                Require(dm < 30, "the live key actor still looks like the baked one (same preset), MAD " + dm.ToString("0.0"));
                // the key flag flips without a rebuild
                rig.Actors[2].KeyActor = true; Require(rig.Actors[2].LiveInk && !Baked(rig.Actors[2]), "a new key actor goes live at once");
                rig.Actors[2].KeyActor = false; Require(!rig.Actors[2].LiveInk && Baked(rig.Actors[2]), "and back to baked");
                var settled = Shot(rig, null, Right); Require(Mad(before[2], settled) < .5, "the right actor is unchanged after the flip back");

                // ---- live: everything, shadow baked ----
                PNVisualPaperSettings.Mode = PNPaperShadowMode.Baked; PNVisualPaperSettings.Ink = PNInkMode.LiveAll; Rebuild(rig);
                Require(rig.Actors.All(a => a.LiveInk && !Baked(a)), "live all: every actor on the raw atlas");
                foreach (var a in rig.Actors) a.Tick(0);
                Require(rig.Actors.All(a => a.ShadeActive && a.ShadeTiles > 0), "baked shadow works with live ink (shade tiles from the raw atlas)");
                Require(rig.Actors.All(a => !a.ShadowActive), "baked shadow has no dynamic casters");
                var liveAll = Shot(rig, "3_liveall_shadowbaked_full", new Rect(0, 0, W, H));
                PNVisualPaperSettings.Mode = PNPaperShadowMode.Off; foreach (var a in rig.Actors) a.Tick(0);
                var liveOff = Shot(rig, "3_liveall_shadowoff_full", new Rect(0, 0, W, H));
                Require(Mad(liveAll, liveOff) > 0, "the baked shadow changes the live frame");
                PNVisualPaperSettings.Mode = PNPaperShadowMode.Actors; foreach (var a in rig.Actors) a.Tick(0);
                Require(rig.Actors.All(a => a.ShadowActive), "dynamic shadow works with live ink");

                // ---- pooled reuse after a rebuild: a new actor binds to the current epoch and flavour ----
                var appearance = rig.Hub.Get<PNVisualAppearanceDefinition>().Get(EveId.Parse("project_n:starweaver"));
                var pooled = new GameObject("pooled"); var pa = pooled.AddComponent<PNVisualActor>(); pa.Bind(appearance, rig.Hub, Vector3.zero);
                Require(pa.LiveInk && pa.AtlasEpoch == PNVisualRuntime.Epoch, "an actor bound after the rebuild uses the new set"); UnityEngine.Object.DestroyImmediate(pooled);

                // ---- back to baked: the first frame returns, the old textures are gone ----
                PNVisualPaperSettings.Mode = PNPaperShadowMode.Actors; PNVisualPaperSettings.Ink = PNInkMode.Baked; Rebuild(rig);
                foreach (var a in rig.Actors) a.Tick(0);
                Require(rig.Actors.All(a => !a.LiveInk && Baked(a)), "baked again");
                var back = new[] { Shot(rig, "4_baked_again_left", Left), Shot(rig, "4_baked_again_middle", Middle), Shot(rig, "4_baked_again_right", Right) };
                for (int i = 0; i < 3; i++) Require(Mad(before[i], back[i]) < .5, "the baked frame returns after the round trip (region " + i + ")");
                Require(AtlasTextures() == tex0, "the old atlases were destroyed: " + AtlasTextures() + " atlas-sized textures, " + tex0 + " at the start");

                // ---- no allocation per frame after a rebuild ----
                for (int i = 0; i < 20; i++) foreach (var a in rig.Actors) a.Tick(.016f);
                long start = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 300; i++) foreach (var a in rig.Actors) a.Tick(.016f);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - start; sb.AppendLine("allocated over 300 frames x 3 actors: " + bytes + " bytes");
                Require(bytes == 0, "no allocation per frame after the rebuilds: " + bytes);
            }
            Require(PNVisualSystem.Count == 0, "no actors left registered");
            Require(PNVisualRuntime.Epoch >= 3, "three rebuilds moved the epoch");
        }
        finally { PNVisualPaperSettings.Ink = oldInk; PNVisualPaperSettings.Mode = oldMode; PNVisualPaperSettings.BakeInk = oldBake; PNVisualRuntime.Release(); PNVisualPacks.Clear(); }
        return "PASS " + checks + " rebind checks\n" + sb;
    }
}
#endif
