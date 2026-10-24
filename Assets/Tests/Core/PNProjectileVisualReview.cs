using System;
using System.Collections;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

// EN: Opt-in Player review that renders Eve's Skill projectile volley (real registered definition, real framework, real trail) to PNG files
//     on a dark and a light background. It is not production code; enabled only by -pn-projectile-visual-review.
//     Run the exe in a window (no -batchmode) with -pn-projectile-visual-output=<directory>.
// VI: Review Player tÃ¹y chá»n: render loáº¡t Ä‘áº¡n Skill cá»§a Eve (definition, framework vÃ  vá»‡t tháº­t) ra PNG trÃªn ná»n tá»‘i vÃ  ná»n sÃ¡ng.
public sealed class PNProjectileVisualReview : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!Environment.GetCommandLineArgs().Contains("-pn-projectile-visual-review")) return;
        var host = new GameObject("PNProjectileVisualReview"); DontDestroyOnLoad(host); host.AddComponent<PNProjectileVisualReview>();
    }
    private static string Arg(string name) => Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith(name, StringComparison.Ordinal))?.Substring(name.Length);
    private readonly System.Collections.Generic.List<string> trace = new System.Collections.Generic.List<string>();
    private IEnumerator Start()
    {
        string output = Arg("-pn-projectile-visual-output=") ?? "ProjectileVisual"; Directory.CreateDirectory(output);
        string status = "ok"; Exception failure = null; trace.Clear();
        yield return Run(output, e => failure = e);
        if (failure != null) status = failure.ToString();
        File.WriteAllText(Path.Combine(output, "result.txt"), status + Environment.NewLine + string.Join(Environment.NewLine, trace));
        Application.Quit(failure == null ? 0 : 1);
    }
    private IEnumerator Run(string output, Action<Exception> fail)
    {
        var runtime = PNGameBootstrap.Runtime; bool began = false; Scene scene = default; var objects = new System.Collections.Generic.List<GameObject>();
        if (runtime == null) { fail(new InvalidOperationException("No game runtime.")); yield break; }
        try { runtime.BeginSession(EveSessionRole.Offline); began = true; } catch (Exception e) { fail(e); yield break; }
        for (int i = 0; i < 600 && !EveRegistrySession.IsReady; i++) yield return null;
        if (!EveRegistrySession.IsReady) { fail(new InvalidOperationException("Session not ready.")); yield break; }
        Time.captureFramerate = 60;
        try
        {
            var hub = EveRegistrySession.Current; var session = EveRegistrySession.Context;
            var manager = runtime.GetComponent<PNProjectileManager>(); manager.ManualAdvance = true; manager.Initialize(session, new PNProjectileProfile { MaxActive = 50 });
            scene = SceneManager.CreateScene("ProjectileVisualReview", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            int enemy = LayerMask.NameToLayer("Enemy");
            var target = new GameObject("target"); target.SetActive(false); SceneManager.MoveGameObjectToScene(target, scene); target.transform.position = new Vector3(2, .4f, 6); target.layer = enemy; objects.Add(target);
            target.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(996000), EveId.Parse("project_n:dummy"), 0); target.AddComponent<PNEnemyStat>(); target.AddComponent<BoxCollider>().size = Vector3.one * .6f;
            target.AddComponent<EveEntityLoader>().Load(hub); target.SetActive(true);
            Physics.SyncTransforms();
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere); SceneManager.MoveGameObjectToScene(marker, scene); marker.transform.position = target.transform.position; marker.transform.localScale = Vector3.one * .3f; objects.Add(marker);
            var camera = new GameObject("review camera").AddComponent<Camera>(); SceneManager.MoveGameObjectToScene(camera.gameObject, scene); objects.Add(camera.gameObject);
            camera.tag = "MainCamera"; camera.transform.position = new Vector3(0, 7, -5); camera.transform.LookAt(new Vector3(1, 0, 3.5f)); camera.fieldOfView = 45; camera.clearFlags = CameraClearFlags.SolidColor; camera.allowHDR = true;
            // The same volley the Skill fires: toward the target, +-30 degrees, the middle one tilted 30 degrees up.
            var source = PNCombatSource.System(session.SessionId, new EveEntityId(996001));
            var launch = new PNProjectileLaunch(PNProjectileGameplayContent.EveSkillHoming, null, source, PNTargetLayers.Of(PNLayers.Enemy));
            var origin = Vector3.zero; var aim = (target.transform.position - origin); aim.y = 0; aim.Normalize();
            var directions = new[] { Quaternion.AngleAxis(-30, Vector3.up) * aim, Tilt(aim), Quaternion.AngleAxis(30, Vector3.up) * aim };
            foreach (var direction in directions) { var receipt = manager.Create(launch, new PNProjectileSpawn(scene, origin, direction, target.GetComponent<PNStat>())); trace.Add("create " + receipt.Status + " " + receipt.Error); }
            var shots = new[] { .05f, .15f, .3f, .45f, .55f, .7f }; int shot = 0; float time = 0;
            foreach (var background in new[] { new Color(.04f, .05f, .09f), new Color(.62f, .66f, .6f) })
            {
                camera.backgroundColor = background;
                if (shot > 0) { manager.Shutdown(); manager.Initialize(session, new PNProjectileProfile { MaxActive = 50 }); manager.ManualAdvance = true; foreach (var direction in directions) manager.Create(launch, new PNProjectileSpawn(scene, origin, direction, target.GetComponent<PNStat>())); time = 0; }
                int local = 0;
                while (local < shots.Length)
                {
                    manager.Advance(1f / 60f); time += 1f / 60f;
                    if (time + 1e-4f >= shots[local]) { yield return new WaitForEndOfFrame(); trace.Add("t=" + time.ToString("0.00") + " active=" + manager.ActiveCount + " " + string.Join(" ", manager.Snapshot().Select(x => x.Position.ToString("0.00") + ">" + x.Forward.ToString("0.00")))); Capture(camera, Path.Combine(output, (background.r < .3f ? "dark" : "light") + "_" + local + "_t" + shots[local].ToString("0.00") + ".png")); local++; shot++; }
                    yield return null;
                }
            }
        }
        finally
        {
            Time.captureFramerate = 0;
            foreach (var obj in objects) if (obj != null) Destroy(obj);
            if (began) { try { runtime.EndSession(); } catch (Exception) { } }
        }
    }
    private static Vector3 Tilt(Vector3 aim) { var side = Vector3.Cross(Vector3.up, aim); return Quaternion.AngleAxis(-30, side.normalized) * aim; }
    private static void Capture(Camera camera, string path)
    {
        var texture = RenderTexture.GetTemporary(960, 540, 24); var previous = camera.targetTexture; camera.targetTexture = texture; camera.Render(); camera.targetTexture = previous;
        var old = RenderTexture.active; RenderTexture.active = texture; var image = new Texture2D(960, 540, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); image.Apply();
        RenderTexture.active = old; RenderTexture.ReleaseTemporary(texture); File.WriteAllBytes(path, image.EncodeToPNG()); Destroy(image);
    }
}

