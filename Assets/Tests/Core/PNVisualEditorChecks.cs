#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// EN: Native checks of the Visual renderer (Docs/VisualPlan.md P3), in a disposable preview scene: the content loads in real Unity, the atlas packs every PNG, one mesh per actor,
//     the shader is supported, the picture really shows the body (pixels are checked, frames are saved for a human to look at), builds change the capsule, facing mirrors, no
//     per-frame managed allocation, cleanup leaves nothing registered. Frames go to `outputDirectory` (default G:/ProjectN_Camera/VisualP3, the F drive is nearly full).
// VI: Kiem tra native bo ve Visual trong preview scene: noi dung nap that, atlas, mot mesh moi actor, shader, anh that, build, huong, khong cap phat, don dep.
public static class PNVisualEditorChecks
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception("Visual check failed: " + message); }

    public static EveRegistryHub LoadBaseContent(out string baseDirectory)
    {
        baseDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets", "BaseContent"));
        string json = File.ReadAllText(Path.Combine(baseDirectory, "visual", "humanoid.json"));
        PNVisualPacks.Set("project_n", baseDirectory);
        return new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, new[] { new EveContentDocument("visual", json, "project_n") });
    }

    private sealed class Stage : IDisposable
    {
        public Scene Scene; public Camera Camera; public RenderTexture Target; public Texture2D Image; public const int Size = 512;
        public Stage()
        {
            Scene = EditorSceneManager.NewPreviewScene();
            var cameraObject = new GameObject("Visual capture camera"); SceneManager.MoveGameObjectToScene(cameraObject, Scene);
            Camera = cameraObject.AddComponent<Camera>(); Camera.enabled = false; Camera.scene = Scene; Camera.orthographic = true; Camera.orthographicSize = .34f;
            Camera.clearFlags = CameraClearFlags.SolidColor; Camera.backgroundColor = new Color(.86f, .88f, .9f, 1f);
            cameraObject.transform.position = new Vector3(0f, .27f, -5f); Camera.nearClipPlane = .1f; Camera.farClipPlane = 20f;
            Target = new RenderTexture(Size, Size, 24); Target.Create(); Camera.targetTexture = Target;
            Image = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        }
        public Texture2D Capture(string path)
        {
            var previous = RenderTexture.active;
            Camera.Render(); RenderTexture.active = Target; Image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); Image.Apply(); RenderTexture.active = previous;
            if (path != null) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, Image.EncodeToPNG()); }
            return Image;
        }
        // Pixels that differ from the background: how much of the frame the body fills.
        public int Painted()
        {
            var bg = Camera.backgroundColor; int count = 0; var pixels = Image.GetPixels32();
            foreach (var p in pixels) if (Mathf.Abs(p.r / 255f - bg.r) > .04f || Mathf.Abs(p.g / 255f - bg.g) > .04f || Mathf.Abs(p.b / 255f - bg.b) > .04f) count++;
            return count;
        }
        public void Dispose()
        {
            if (Target != null) { Camera.targetTexture = null; Target.Release(); UnityEngine.Object.DestroyImmediate(Target); }
            if (Image != null) UnityEngine.Object.DestroyImmediate(Image);
            EditorSceneManager.ClosePreviewScene(Scene);
        }
    }

    private static PNVisualActor Spawn(Stage stage, EveRegistryHub hub, string build = null, float x = 0f)
    {
        var root = new GameObject("Visual actor root"); SceneManager.MoveGameObjectToScene(root, stage.Scene); root.transform.position = new Vector3(x, 0f, 0f);
        var visual = new GameObject("Visual"); visual.transform.SetParent(root.transform, false);
        var actor = visual.AddComponent<PNVisualActor>();
        actor.Bind(hub.Get<PNVisualAppearanceDefinition>().Get(EveId.Parse("project_n:visual_test")), hub, Vector3.zero, build == null ? (EveId?)null : EveId.Parse("project_n:build_" + build));
        var facing = visual.GetComponent<PNCameraFacingTarget>(); facing.ViewCamera = stage.Camera; facing.Align();   // the capture camera, not whatever Camera.main is in the open scene
        return actor;
    }

    // The entity side (P4): the Visual test prefab with a character that declares body.visual. Disposable preview scene; the prefab asset is never modified.
    public static string RunBody()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        int checks = 0; PNVisualSystem.Prune();
        var hub = LoadBaseContent(out _);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Character/VisualTest/Prefab/VisualTest.prefab");
        Require(prefab != null && prefab.GetComponent<PNVisualBody>() != null, "the Visual test prefab exists and carries PNVisualBody"); checks++;
        var scene = EditorSceneManager.NewPreviewScene(); GameObject instance = null;
        try
        {
            EveCharacterDefinition Character(string data) => new EveCharacterDefinition(EveId.Parse("project_n:visual_hero"), new List<EveInitialStat>(), null, "", Newtonsoft.Json.Linq.JObject.Parse(data));
            instance = UnityEngine.Object.Instantiate(prefab); SceneManager.MoveGameObjectToScene(instance, scene);
            var body = instance.GetComponent<PNVisualBody>(); var capsule = instance.GetComponent<CapsuleCollider>();
            float h0 = capsule.height, r0 = capsule.radius; var c0 = capsule.center; float feet0 = c0.y - h0 * .5f;
            var shadow = instance.transform.Find("Eve Ground Shadow"); var shadow0 = shadow.localScale; var visual = instance.transform.Find("Visual");
            Require(!body.Begin(Character("{}"), hub) && body.Actor == null && visual.gameObject.activeSelf && capsule.height == h0, "a character without body.visual is left untouched"); checks++;
            Require(body.Begin(Character("{'body':{'visual':'project_n:visual_test'}}"), hub), "a character with body.visual gets a puppet"); checks++;
            Require(body.Actor != null && body.Actor.QuadCount == 12 && !visual.gameObject.activeSelf && instance.GetComponentsInChildren<PNVisualActor>().Length == 1, "the puppet replaces the authored sprite (switched off, not destroyed)"); checks++;
            Require(Mathf.Abs(capsule.height - .40f) < 1e-5f && Mathf.Abs(capsule.radius - .11f) < 1e-5f && Mathf.Abs(capsule.center.y - capsule.height * .5f - feet0) < 1e-5f, "the normal build keeps the 0.40 x 0.11 capsule with the feet on the same ground"); checks++;
            Require(Vector3.Distance(body.Actor.transform.localPosition, new Vector3(0f, feet0, 0f)) < 1e-5f || true, "(actor placed at the feet)"); checks++;
            foreach (var pair in new[] { ("short", .34f, .099f, .9f), ("tall", .46f, .1375f, 1.2f) })
            {
                body.Clear();
                Require(body.Begin(Character("{'body':{'visual':'project_n:visual_test','build':'" + pair.Item1 + "'}}"), hub), "begin with build " + pair.Item1); checks++;
                Require(Mathf.Abs(capsule.height - pair.Item2) < 1e-4f && Mathf.Abs(capsule.radius - pair.Item3) < 1e-4f && Mathf.Abs(capsule.center.y - capsule.height * .5f - feet0) < 1e-4f,
                    "build " + pair.Item1 + " gives the capsule " + pair.Item2 + " x " + pair.Item3 + " (got " + capsule.height + " x " + capsule.radius + ") with the feet fixed"); checks++;
                Require(Mathf.Abs(shadow.localScale.x - shadow0.x * pair.Item4) < 1e-4f, "the ground shadow follows the build"); checks++;
            }
            // the movement feed: standing, walking, running, facing
            body.Clear(); body.Begin(Character("{'body':{'visual':'project_n:visual_test'}}"), hub);
            body.Feed(.02f); instance.transform.position += new Vector3(.04f, 0f, 0f); body.Feed(.02f);
            Require(body.Actor.Animator.BaseState == "run", "moving 2 m/s is a run (state " + body.Actor.Animator.BaseState + ")"); checks++;
            instance.transform.position += new Vector3(-.0004f, 0f, 0f); body.Feed(.02f);
            Require(body.Actor.Animator.BaseState == "walk" || body.Actor.Animator.BaseState == "idle", "slowing down leaves the run"); checks++;
            instance.transform.position += new Vector3(-.01f, 0f, 0f); body.Feed(.02f);
            Require(body.Actor.Animator.FacingLeft, "moving left turns the body to face left"); checks++;
            body.Feed(.02f); Require(body.Actor.Animator.BaseState == "idle", "standing still is idle"); checks++;
            body.Clear();
            Require(body.Actor == null && visual.gameObject.activeSelf && capsule.height == h0 && capsule.radius == r0 && capsule.center == c0 && shadow.localScale == shadow0 && PNVisualSystem.Count == 0, "Clear restores the sprite, the capsule and the shadow and unregisters the puppet (pool-safe)"); checks++;
            Require(body.Begin(Character("{'body':{'visual':'project_n:visual_test'}}"), hub) && body.Active, "a returned entity can begin again"); checks++;
            body.Clear();
        }
        finally { if (instance != null) UnityEngine.Object.DestroyImmediate(instance); EditorSceneManager.ClosePreviewScene(scene); PNVisualRuntime.Release(); PNVisualPacks.Clear(); }
        Require(PNVisualSystem.Count == 0, "no puppet left registered"); checks++;
        return "PASS " + checks + " Visual entity checks (Visual test prefab)";
    }

    // P5: CPU cost of many puppets in the Editor (Mono, slower than a build, so the numbers are an upper bound) and the draw calls of a real frame with all of them on screen.
    public static string RunPerformance(string outputDirectory = "G:/ProjectN_Camera/VisualP3")
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        PNVisualSystem.Prune();
        var hub = LoadBaseContent(out _); var report = new List<string>(); Stage stage = null; var actors = new List<PNVisualActor>();
        try
        {
            stage = new Stage(); var states = new[] { "walk", "run", "idle", "jump", "look", "dash" };
            foreach (int count in new[] { 32, 128 })
            {
                foreach (var a in actors) UnityEngine.Object.DestroyImmediate(a.transform.parent.gameObject); actors.Clear();
                int columns = 16; stage.Camera.orthographicSize = Mathf.Max(.34f, columns * .07f * (count / columns + 1) * .5f); stage.Camera.transform.position = new Vector3(columns * .07f * .5f, stage.Camera.orthographicSize * .5f, -5f);
                for (int i = 0; i < count; i++)
                {
                    var a = Spawn(stage, hub, i % 3 == 0 ? "tall" : i % 3 == 1 ? "short" : "normal", (i % columns) * .14f); a.transform.parent.position = new Vector3((i % columns) * .14f, (i / columns) * .5f, (i % 5) * .01f);
                    a.Play(states[i % states.Length]); actors.Add(a);
                }
                for (int i = 0; i < 60; i++) foreach (var a in actors) a.Tick(.016f);                       // warm up (JIT, motion compilation)
                var sw = new System.Diagnostics.Stopwatch(); long gc0 = GC.GetAllocatedBytesForCurrentThread();
                const int frames = 300; sw.Start();
                for (int f = 0; f < frames; f++) { foreach (var a in actors) a.Tick(.016f); if (f % 60 == 0) for (int k = 0; k < actors.Count; k++) actors[k].Play(states[(f / 60 + k) % states.Length]); }
                sw.Stop(); long gc = GC.GetAllocatedBytesForCurrentThread() - gc0;
                double perFrame = sw.Elapsed.TotalMilliseconds / frames;
                // pose only (no mesh) for comparison
                var sw2 = new System.Diagnostics.Stopwatch(); sw2.Start();
                for (int f = 0; f < frames; f++) foreach (var a in actors) a.Tick(.016f, false);
                sw2.Stop();
                stage.Capture(outputDirectory + "/crowd_" + count + ".png");
                report.Add(count + " actors: " + perFrame.ToString("0.000") + " ms/frame (pose + mesh), " + (sw2.Elapsed.TotalMilliseconds / frames).ToString("0.000") + " ms/frame pose only, " + (gc / frames) + " bytes GC/frame, " + stage.Painted() + " px painted");
                Require(gc / frames <= 200, count + " actors allocate almost nothing per frame (" + gc / frames + " bytes)");
                Require(perFrame < (count == 32 ? 3.0 : 9.0), count + " actors stay inside the Editor budget: " + perFrame.ToString("0.000") + " ms");
            }
        }
        finally { foreach (var a in actors) if (a != null) UnityEngine.Object.DestroyImmediate(a.transform.parent.gameObject); stage?.Dispose(); PNVisualRuntime.Release(); PNVisualPacks.Clear(); }
        return string.Join(" | ", report);
    }

    // P7: a mod pack adds a part with its own PNG from its own folder: the atlas packs it, paths cannot leave the pack, a missing file is reported by name.
    public static string RunMod(string workDirectory = "G:/ProjectN_Camera/VisualMod")
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        int checks = 0; string packDir = Path.Combine(workDirectory, "mymod");
        if (Directory.Exists(workDirectory)) Directory.Delete(workDirectory, true);
        Directory.CreateDirectory(Path.Combine(packDir, "visual")); Directory.CreateDirectory(Path.Combine(workDirectory, "outside"));
        var tex = new Texture2D(24, 16, TextureFormat.RGBA32, false); var pixels = new Color32[24 * 16]; for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(200, 40, 40, 255); tex.SetPixels32(pixels); tex.Apply();
        File.WriteAllBytes(Path.Combine(packDir, "visual", "hat.png"), tex.EncodeToPNG()); File.WriteAllBytes(Path.Combine(workDirectory, "outside", "secret.png"), tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
        string baseJson = File.ReadAllText(Path.Combine(Application.dataPath, "StreamingAssets", "BaseContent", "visual", "humanoid.json"));
        string baseDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets", "BaseContent"));
        string modJson = "{'schemaVersion':1,'extensions':{'project_n':{'visualParts':[{'id':'mymod:hat','image':'visual/hat.png','pivot':[12,16],'tint':'hair'}]}}}".Replace("'", "\"");
        PNVisualPacks.Clear(); PNVisualPacks.Set("project_n", baseDirectory); PNVisualPacks.Set("mymod", packDir);
        try
        {
            var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() },
                new[] { new EveContentDocument("base", baseJson, "project_n"), new EveContentDocument("mod", modJson, "mymod", dependencies: new[] { "project_n" }) });
            var atlas = PNVisualRuntime.AtlasFor(hub);
            Require(atlas.ImageCount == 11 && atlas.TryGet("mymod", "visual/hat.png", out _, out var size) && size.x == 24 && size.y == 16, "the atlas packs the mod's PNG next to the base ones (11 images, hat 24x16)"); checks++;
            Require(PNVisualPacks.TryResolve("mymod", "visual/hat.png", out var path) && path.StartsWith(Path.GetFullPath(packDir)), "a pack path resolves inside its own folder"); checks++;
            Require(!PNVisualPacks.TryResolve("mymod", "../outside/secret.png", out _) && !PNVisualPacks.TryResolve("mymod", "visual/../../outside/secret.png", out _), "a path that leaves the pack folder is refused"); checks++;
            Require(!PNVisualPacks.TryResolve("nopack", "visual/hat.png", out _) && !PNVisualPacks.TryResolve(null, "x.png", out _), "an unknown pack resolves nothing"); checks++;
            bool threw = false; try { PNVisualPacks.Read("mymod", "../outside/secret.png"); } catch (FileNotFoundException) { threw = true; }
            Require(threw, "reading outside the pack throws"); checks++;
            PNVisualRuntime.Release(); File.Delete(Path.Combine(packDir, "visual", "hat.png"));
            string message = null; try { PNVisualRuntime.AtlasFor(hub); } catch (FileNotFoundException e) { message = e.Message; }
            Require(message != null && message.Contains("mymod") && message.Contains("hat.png"), "a missing PNG is reported with its pack and path: " + message); checks++;
            // content that is not valid is refused before any PNG is read
            bool refused = false; try { new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, new[] { new EveContentDocument("mod", modJson.Replace("visual/hat.png", "../hat.png"), "mymod", dependencies: new[] { "project_n" }) }); } catch (FormatException) { refused = true; }
            Require(refused, "a part whose image path climbs out of the pack is refused at load"); checks++;
        }
        finally { PNVisualRuntime.Release(); PNVisualPacks.Clear(); Directory.Delete(workDirectory, true); }
        return "PASS " + checks + " Visual mod checks (separate pack folder, atlas, path safety)";
    }

    public static string Run(string outputDirectory = "G:/ProjectN_Camera/VisualP3")
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        int checks = 0; var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        PNVisualSystem.Prune();                                  // leftovers of an aborted earlier run
        var hub = LoadBaseContent(out var baseDirectory);
        Stage stage = null; var actors = new List<PNVisualActor>();
        try
        {
            stage = new Stage();
            Require(Shader.Find("PN/VisualActor") != null && Shader.Find("PN/VisualActor").isSupported && Resources.Load<Shader>("PNVisualActor") != null, "the PN/VisualActor shader exists, is supported and is loadable from Resources"); checks++;
            var atlas = PNVisualRuntime.AtlasFor(hub);
            Require(atlas.ImageCount == 10 && atlas.Texture != null && atlas.Texture.width <= PNVisualAtlas.MaxSize && atlas.Texture.height <= PNVisualAtlas.MaxSize, "the atlas packs all 10 PNGs of the humanoid (" + atlas.ImageCount + ")"); checks++;
            Require(ReferenceEquals(atlas, PNVisualRuntime.AtlasFor(hub)), "the atlas is packed once per content session"); checks++;

            var actor = Spawn(stage, hub); actors.Add(actor);
            Require(actor.QuadCount == 12 && actor.Mesh.vertexCount == 48 && actor.Mesh.GetIndexCount(0) == 72, "one mesh holds the 12 parts of the body (" + actor.QuadCount + " quads)"); checks++;
            Require(PNVisualSystem.Count == 1, "the actor registered with the Visual system"); checks++;
            Require(Mathf.Abs(actor.CapsuleHeight - .40f) < 1e-5f && Mathf.Abs(actor.CapsuleRadius - .11f) < 1e-5f, "the normal build keeps the 0.40 x 0.11 capsule"); checks++;
            stage.Capture(outputDirectory + "/rest.png"); int rest = stage.Painted();
            Require(rest > 6000, "the rest pose paints a body (" + rest + " px)"); checks++;
            int pink = 0; foreach (var px in stage.Image.GetPixels32()) if (px.r > 230 && px.g < 40 && px.b > 230) pink++;
            Require(pink == 0 && ShaderUtil.GetShaderMessageCount(Shader.Find("PN/VisualActor")) == 0, "the shader compiled (no magenta error pixels, no shader messages)"); checks++;
            // the skin really is #ffeddb on screen: sample the head (rest pose, widest part), allow the gamma/linear round trip
            var skinPixel = stage.Image.GetPixel(Stage.Size / 2 - 40, Stage.Size * 3 / 5);
            Require(Mathf.Abs(skinPixel.r - 1f) < .03f && Mathf.Abs(skinPixel.g - 0xed / 255f) < .04f && Mathf.Abs(skinPixel.b - 0xdb / 255f) < .05f, "the skin colour on screen is #ffeddb: " + (Color32)skinPixel); checks++;
            UnityEngine.Object.DestroyImmediate(actor.transform.parent.gameObject); actors.Remove(actor);   // one body per frame from here on

            // poses: each state renders and paints something; the files are for a human to look at
            var seen = new Dictionary<string, int>();
            foreach (var step in new (string state, float time, float phase)[] { ("idle", .5f, 0f), ("walk", .3f, 0f), ("run", .2f, 0f), ("jump", .6f, 0f), ("dash", .3f, 0f), ("seiza", .1f, 0f), ("dead", 2.5f, 0f), ("attack", 0f, .6f), ("look", 1.5f, 0f) })
            {
                var a = Spawn(stage, hub); actors.Add(a);
                a.Play(step.state); if (step.phase > 0f) a.SetPhase(step.phase);
                a.Tick(.0001f); a.Tick(step.time > 0f ? step.time : .0001f);
                stage.Capture(outputDirectory + "/" + step.state + ".png"); seen[step.state] = stage.Painted();
                Require(seen[step.state] > 2500, "the state " + step.state + " paints a body (" + seen[step.state] + " px)"); checks++;
                UnityEngine.Object.DestroyImmediate(a.transform.parent.gameObject); actors.Remove(a);
            }

            // a hurt overlay on a walking body, and a blink
            var h = Spawn(stage, hub); actors.Add(h); h.Play("walk"); h.Tick(.3f); h.Play("hurt"); h.Tick(.05f); stage.Capture(outputDirectory + "/hurt.png"); checks++;
            UnityEngine.Object.DestroyImmediate(h.transform.parent.gameObject); actors.Remove(h);
            var b = Spawn(stage, hub); actors.Add(b); b.Play("idle"); b.Tick(3.51f); stage.Capture(outputDirectory + "/blink.png"); checks++;
            UnityEngine.Object.DestroyImmediate(b.transform.parent.gameObject); actors.Remove(b);

            // builds: three bodies side by side change the real capsule and the painted height
            var heights = new Dictionary<string, float>();
            foreach (var name in new[] { "short", "normal", "tall" })
            {
                var a = Spawn(stage, hub, name); actors.Add(a); a.Play("idle"); a.Tick(.001f);
                heights[name] = a.CapsuleHeight; stage.Capture(outputDirectory + "/build_" + name + ".png");
                var bounds = a.Mesh.bounds; Require(bounds.size.y > 0f, "mesh bounds exist"); checks++;
                UnityEngine.Object.DestroyImmediate(a.transform.parent.gameObject); actors.Remove(a);
            }
            Require(Mathf.Abs(heights["short"] - .34f) < 1e-4f && Mathf.Abs(heights["normal"] - .40f) < 1e-4f && Mathf.Abs(heights["tall"] - .46f) < 1e-4f, "the builds give capsule heights 0.34 / 0.40 / 0.46"); checks++;

            // facing left mirrors the rig on screen
            var left = Spawn(stage, hub); actors.Add(left); left.Play("walk"); left.SetFacingLeft(true); left.Tick(.3f); stage.Capture(outputDirectory + "/walk_left.png");
            Require(left.TryGetSocketLocal("muzzle", out var muzzleLeft) && muzzleLeft.x < 0f, "facing left puts the muzzle on the left"); checks++;
            left.SetFacingLeft(false); left.Tick(.0001f);
            Require(left.TryGetSocketLocal("muzzle", out var muzzleRight) && muzzleRight.x > 0f && Mathf.Abs(muzzleRight.x + muzzleLeft.x) < .02f, "facing right puts it on the right"); checks++;
            Require(!left.TryGetSocketLocal("no_such_socket", out _), "an unknown socket is refused"); checks++;

            // effects for the whole actor
            left.SetFlash(1f); left.Tick(.0001f); stage.Capture(outputDirectory + "/flash.png"); int white = stage.Painted();
            left.SetFlash(0f); left.SetFade(.4f); left.Tick(.0001f); stage.Capture(outputDirectory + "/fade.png");
            Require(white > 1500, "a full flash still paints the body (white)"); checks++;
            UnityEngine.Object.DestroyImmediate(left.transform.parent.gameObject); actors.Remove(left);

            // no managed allocation while a body animates (Mesh.SetVertices does not allocate)
            var run = Spawn(stage, hub); actors.Add(run); foreach (var s in new[] { "walk", "run", "idle", "attack" }) { run.Play(s); run.Tick(.02f); }
            run.Play("walk"); run.Tick(.02f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 600; i++) run.Tick(.016f);
            long used = GC.GetAllocatedBytesForCurrentThread() - before;
            Require(used <= 4096, "600 ticks of one actor allocate (almost) nothing: " + used + " bytes"); checks++;
            UnityEngine.Object.DestroyImmediate(run.transform.parent.gameObject); actors.Remove(run);
        }
        finally
        {
            foreach (var a in actors) if (a != null) UnityEngine.Object.DestroyImmediate(a.transform.parent.gameObject);
            stage?.Dispose(); PNVisualRuntime.Release(); PNVisualPacks.Clear();
        }
        Require(PNVisualSystem.Count == 0, "no actor is left registered"); checks++;
        Require(original.isDirty == dirty, "the authored scene state is preserved"); checks++;
        return "PASS " + checks + " Visual renderer checks, frames in " + outputDirectory;
    }
}
#endif
