using System;
using System.Collections;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

// EN: Opt-in Player review that renders the lorica presentation on a real enemy (health bar overlay, break effect, recovery) to PNG files.
//     Not production code; enabled only by -pn-lorica-visual-review (+ -pn-lorica-visual-output=<directory>). It writes a throwaway content pack
//     into the Mods folder (character with lorica) and removes it afterwards. Run the exe in a window (no -batchmode).
// VI: Review Player tùy chọn: render phần hiển thị lorica trên enemy thật ra PNG. Tự tạo rồi xoá một gói content tạm trong thư mục Mods.
public sealed class PNLoricaVisualReview : MonoBehaviour
{
    private const string Pack = "lorica_review";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!Environment.GetCommandLineArgs().Contains("-pn-lorica-visual-review")) return;
        var host = new GameObject("PNLoricaVisualReview"); DontDestroyOnLoad(host); host.AddComponent<PNLoricaVisualReview>();
    }
    private static string Arg(string name) => Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith(name, StringComparison.Ordinal))?.Substring(name.Length);
    private readonly System.Collections.Generic.List<string> trace = new System.Collections.Generic.List<string>();
    private IEnumerator Start()
    {
        string output = Arg("-pn-lorica-visual-output=") ?? "LoricaVisual"; Directory.CreateDirectory(output);
        string folder = Path.Combine(Application.persistentDataPath, "Mods", Pack); Exception failure = null;
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "characters"));
            File.WriteAllText(Path.Combine(folder, "manifest.json"), "{\"schemaVersion\":1,\"namespace\":\"" + Pack + "\",\"version\":\"1.0.0\",\"dependencies\":{\"project_n\":\"1.0.0\"}}");
            File.WriteAllText(Path.Combine(folder, "characters", "enemy.json"), "{\"schemaVersion\":1,\"characters\":[{\"id\":\"" + Pack + ":enemy\",\"data\":{\"lorica\":{\"share\":0.6,\"breakSeconds\":2,\"recoverySeconds\":3}},\"stats\":[{\"statId\":\"base:health\",\"base\":100000},{\"statId\":\"base:lorica\",\"base\":100}],\"skill\":[]},{\"id\":\"" + Pack + ":plain\",\"data\":{},\"stats\":[{\"statId\":\"base:health\",\"base\":100000}],\"skill\":[]}]}");
        }
        catch (Exception e) { failure = e; }
        if (failure == null) yield return Run(output, e => failure = e);
        try { Directory.Delete(folder, true); } catch (Exception) { }
        File.WriteAllText(Path.Combine(output, "result.txt"), (failure == null ? "ok" : failure.ToString()) + Environment.NewLine + string.Join(Environment.NewLine, trace));
        Application.Quit(failure == null ? 0 : 1);
    }
    // A recognisable body (stripe, eyes, mouth) so a mirror shard that reflects the sprite can be told from a plain tint.
    private static Color Face(int x, int y)
    {
        if (Vector2.Distance(new Vector2(x, y), new Vector2(46, 76)) < 9 || Vector2.Distance(new Vector2(x, y), new Vector2(82, 76)) < 9) return new Color(.1f, .05f, .08f, 1);
        if (y > 38 && y < 46 && x > 40 && x < 88) return new Color(.1f, .05f, .08f, 1);
        if (x > 60 && x < 68) return new Color(.95f, .85f, .3f, 1);
        return new Color(.85f, .45f, .4f, 1);
    }
    private IEnumerator Run(string output, Action<Exception> fail)
    {
        var runtime = PNGameBootstrap.Runtime; bool began = false; Scene scene = default; var objects = new System.Collections.Generic.List<GameObject>();
        if (runtime == null) { fail(new InvalidOperationException("No game runtime.")); yield break; }
        // The bootstrap already loaded the content without the throwaway pack: unload it, load again, then start the session.
        try { runtime.UnloadContent(); } catch (Exception e) { fail(e); yield break; }
        for (int i = 0; i < 300 && EveRegistrySession.State == EveSessionState.Stopping; i++) yield return null;
        try { runtime.EnsureContentLoaded(); runtime.BeginSession(EveSessionRole.Offline); began = true; } catch (Exception e) { fail(e); yield break; }
        for (int i = 0; i < 600 && !EveRegistrySession.IsReady; i++) yield return null;
        if (!EveRegistrySession.IsReady) { fail(new InvalidOperationException("Session not ready.")); yield break; }
        Time.captureFramerate = 60;
        try
        {
            var hub = EveRegistrySession.Current;
            if (!hub.Characters.TryGet(EveId.Parse(Pack + ":enemy"), out _)) throw new InvalidOperationException("The review pack was not loaded.");
            scene = SceneManager.CreateScene("LoricaVisualReview", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var camera = new GameObject("review camera").AddComponent<Camera>(); SceneManager.MoveGameObjectToScene(camera.gameObject, scene); objects.Add(camera.gameObject);
            camera.tag = "MainCamera"; camera.transform.position = new Vector3(0, 1.5f, -5.2f); camera.transform.LookAt(new Vector3(0, .9f, 0)); camera.fieldOfView = 38; camera.clearFlags = CameraClearFlags.SolidColor; camera.allowHDR = true;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(ground, scene); ground.transform.position = new Vector3(0, -.05f, 0); ground.transform.localScale = new Vector3(8, .1f, 4); objects.Add(ground);
            var enemy = new GameObject("lorica enemy"); enemy.SetActive(false); SceneManager.MoveGameObjectToScene(enemy, scene); objects.Add(enemy); enemy.layer = LayerMask.NameToLayer("Enemy");
            enemy.transform.position = new Vector3(0, .5f, 0);
            bool plain = Environment.GetCommandLineArgs().Contains("-pn-lorica-plain");
            enemy.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(997000), EveId.Parse(Pack + (plain ? ":plain" : ":enemy")), 0);
            var stat = enemy.AddComponent<PNEnemyStat>(); enemy.AddComponent<EveTimeScale>(); enemy.AddComponent<SphereCollider>().radius = .4f;
            var visual = new GameObject("Visual"); visual.transform.SetParent(enemy.transform, false); var sprite = visual.AddComponent<SpriteRenderer>();
            var texture = new Texture2D(128, 128, TextureFormat.RGBA32, false); var pixels = new Color[128 * 128];
            for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++) { float d = Vector2.Distance(new Vector2(x, y), new Vector2(63.5f, 63.5f)); pixels[y * 128 + x] = d < 56 ? Face(x, y) : new Color(0, 0, 0, 0); }
            texture.SetPixels(pixels); texture.Apply(); sprite.sprite = Sprite.Create(texture, new Rect(0, 0, 128, 128), new Vector2(.5f, .5f), 128);
            enemy.AddComponent<EveEntityLoader>().Load(hub); enemy.SetActive(true);
            var session = EveRegistrySession.Context; var buffs = enemy.GetComponent<PNBuffManager>();
            trace.Add("loaded: lorica=" + stat.LoricaCurrent + "/" + (stat.HasLorica ? stat.GetMax(PNContentIds.Lorica) : 0) + " settings=" + stat.Lorica.Share + "," + stat.Lorica.BreakSeconds + "," + stat.Lorica.RecoverySeconds);
            for (int i = 0; i < 20; i++) yield return null;
            int shot = 0;
            IEnumerator Capture(string label, int pre)
            {
                for (int i = 0; i < pre; i++) yield return null;
                yield return new WaitForEndOfFrame();
                camera.backgroundColor = shot % 2 == 0 ? new Color(.05f, .06f, .1f) : new Color(.55f, .6f, .55f);
                var screen = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(output, (shot++).ToString("00") + "_" + label + ".png"), screen.EncodeToPNG()); Destroy(screen);
                trace.Add(label + ": lorica=" + stat.LoricaCurrent + " hp=" + stat.GetCurrent(PNContentIds.Health) + " buffs=" + string.Join(",", buffs.Buffs.Snapshot().Select(x => x.Id.Path)));
            }
            PNDamageResult Hit(double amount) => stat.ReceiveDamage(new PNDamage(stat.SessionId, new EveEntityId(1), amount, new[] { PNContentIds.Physical }).WithIFrameDuration(0d));
            if (plain)
            {
                // An enemy without lorica: only an ordinary Buff comes and goes. Consecutive frames, to see whether the health bar moves.
                camera.backgroundColor = new Color(.05f, .06f, .1f); int number = 0;
                IEnumerator Frame(string label)
                {
                    yield return new WaitForEndOfFrame(); var screen = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(output, "seq_" + (number++).ToString("000") + "_" + label + ".png"), screen.EncodeToPNG()); Destroy(screen);
                    trace.Add("seq " + number + " " + label + " buffs=" + string.Join(",", buffs.Buffs.Snapshot().Select(x => x.Id.Path)));
                }
                for (int i = 0; i < 30; i++) yield return Frame("none");
                var applied = buffs.Buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:burn"), PNBuffSource.Capture(stat)));
                for (int i = 0; i < 60; i++) yield return Frame("burn");
                buffs.Buffs.Remove(applied.Handle, PNBuffEndReason.Dispelled, true);
                for (int i = 0; i < 60; i++) yield return Frame("removed");
                yield break;
            }
            var look = Arg("-pn-lorica-shatter="); if (look != null) PNLoricaShatter.Look = look == "mirror" ? PNLoricaShatter.LookKind.Mirror : PNLoricaShatter.LookKind.Glass;
            if (Environment.GetCommandLineArgs().Contains("-pn-lorica-shatter-stress"))
            {
                // Cost of simultaneous breaks: average frame time while N shatters play at once (the cap is lifted for the measurement; vsync off).
                Time.captureFramerate = 0; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; PNLoricaShatter.MaxActive = 99;
                var bounds = sprite.bounds; var toward = -camera.transform.forward * .3f;
                if (Environment.GetCommandLineArgs().Contains("-pn-lorica-shatter-warm")) { var t0 = Time.realtimeSinceStartup; int pumps = 0; PNShaderWarmup.Begin(session); PNShaderWarmup.ScanScene(scene); while (!PNShaderWarmup.Pump()) { pumps++; yield return null; } trace.Add("warm-up took " + ((Time.realtimeSinceStartup - t0) * 1000f).ToString("0.0") + "ms over " + pumps + " frames, " + PNShaderWarmup.Compiled + " variant requests"); }
                foreach (int n in new[] { 0, 1, 4, 8, 16 })
                {
                    for (int i = 0; i < 90; i++) yield return null;
                    for (int k = 0; k < n; k++) PNLoricaShatter.Play(scene, bounds.center + new Vector3((k % 8 - 3.5f) * .5f, (k / 8) * .5f, 0), bounds.size, null, sprite.sortingLayerID, sprite.sortingOrder + 5, toward, sprite.sprite, sprite.flipX);
                    float total = 0f, worst = 0f; int frames = 0;
                    for (int i = 0; i < 40; i++) { yield return null; float dt = Time.unscaledDeltaTime; total += dt; if (dt > worst) worst = dt; frames++; }
                    trace.Add("stress " + PNLoricaShatter.Look + " n=" + n + " avg=" + (total / frames * 1000f).ToString("0.00") + "ms worst=" + (worst * 1000f).ToString("0.00") + "ms");
                }
                yield break;
            }
            yield return Capture("full", 0);
            yield return Capture("full_dark", 20);
            Hit(100); yield return Capture("after_one_hit", 3);
            Hit(60); yield return Capture("low", 3);
            Hit(100);                                  // breaks
            yield return Capture("break_00", 0); yield return Capture("break_03", 2); yield return Capture("break_06", 2); yield return Capture("break_09", 2); yield return Capture("break_13", 3);
            yield return Capture("break_18", 4); yield return Capture("break_26", 7); yield return Capture("break_38", 11); yield return Capture("break_55", 16);
            yield return Capture("broken_after_fade", 60);
            for (int i = 0; i < 100; i++) yield return null;   // the 2 s break ends (frames are 1/60 s)
            yield return Capture("recovery", 6); yield return Capture("recovery_later", 40);
            if (Environment.GetCommandLineArgs().Contains("-pn-lorica-sequence"))
            {
                // Consecutive frames around buff transitions (one Buff only), to measure whether the health bar moves when a Buff appears or disappears.
                int frame = 0; camera.backgroundColor = new Color(.05f, .06f, .1f);
                IEnumerator Step(string prefix)
                {
                    yield return new WaitForEndOfFrame();
                    var screen = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(output, "seq_" + (frame++).ToString("000") + "_" + prefix + ".png"), screen.EncodeToPNG()); Destroy(screen);
                    trace.Add("seq " + frame + " " + prefix + " buffs=" + string.Join(",", buffs.Buffs.Snapshot().Select(x => x.Id.Path)));
                }
                bool Has(EveId id) => buffs.Buffs.Snapshot().Any(x => x.Id == id);
                for (int i = 0; i < 400 && Has(PNContentIds.LoricaRecovery); i++) yield return Step("recovery_ends");
                for (int i = 0; i < 45; i++) yield return Step("after_recovery");
                for (int i = 0; i < 4; i++) yield return Step("before_break");
                Hit(100); Hit(100);
                for (int i = 0; i < 40; i++) yield return Step("break_applied");
                for (int i = 0; i < 400 && Has(PNContentIds.LoricaBreak); i++) yield return Step("break_ends");
                for (int i = 0; i < 60; i++) yield return Step("after_break");
            }
        }
        finally
        {
            Time.captureFramerate = 0;
            foreach (var obj in objects) if (obj != null) Destroy(obj);
            if (began) { try { runtime.EndSession(); } catch (Exception) { } }
        }
    }
}
