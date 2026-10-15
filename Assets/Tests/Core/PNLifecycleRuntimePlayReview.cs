#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using EveEngine;
using EveEngine.Experimental.Lifecycle;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PNLifecycleRuntimePlayReview : MonoBehaviour
{
    private const string Key = "PNLifecycleRuntimePlay.Launch", Audit = "PNLifecycleRuntimePlay.Audit";
    public static string Status = "Idle";
    private Scene disposable;
    private readonly List<GameObject> roots = new List<GameObject>();
    private readonly List<string> evidence = new List<string>();
    private int checks;
    public static string Launch()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || scene.isDirty) throw new InvalidOperationException("Clean idle Edit required; fixture never saves scene.");
        Directory.CreateDirectory(".utmp/LifecycleRuntimeReview");
        SessionState.SetString(Audit, scene.path + "|" + scene.isDirty + "|" + Time.timeScale + "|" + QualitySettings.vSyncCount + "|" + Application.targetFrameRate);
        SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode(); return "Entering Play for runtime publication and automatic Stopping cleanup";
    }
    [InitializeOnLoadMethod] private static void Register() { EditorApplication.playModeStateChanged -= Changed; EditorApplication.playModeStateChanged += Changed; }
    private static void Changed(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += () =>
        {
            var scene = SceneManager.CreateScene("Disposable lifecycle Play fixture");
            var root = new GameObject("Lifecycle Play reviewer"); SceneManager.MoveGameObjectToScene(root, scene); root.AddComponent<PNLifecycleRuntimePlayReview>().disposable = scene;
        };
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key, false); var scene = SceneManager.GetActiveScene();
            File.WriteAllText(".utmp/LifecycleRuntimeReview/PlayAudit.txt", "before=" + SessionState.GetString(Audit, "") + "\nafter=" + scene.path + "|" + scene.isDirty + "|" + Time.timeScale + "|" + QualitySettings.vSyncCount + "|" + Application.targetFrameRate + "\nGame/Simulator and Play Maximized untouched; no original scene saved or explicitly reloaded");
        }
    }
    private void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; evidence.Add("PASS " + label); }
    private EveEntityLoader Actor(long id)
    {
        var root = new GameObject("Temporary runtime lifecycle actor"); roots.Add(root); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, disposable); root.transform.position = new Vector3(100 + id % 3, 0, 0);
        root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse("project_n:dummy"), 0); root.AddComponent<PNEnemyStat>(); root.AddComponent<EveTimeScale>(); root.AddComponent<PNBuffManager>().RenderPresentation = false;
        var visible = GameObject.CreatePrimitive(PrimitiveType.Cube); visible.transform.SetParent(root.transform, false);
        return root.AddComponent<EveEntityLoader>();
    }
    private IEnumerator Start()
    {
        var work = Sequence();
        while (true)
        {
            object next; try { if (!work.MoveNext()) break; next = work.Current; } catch (Exception error) { Status = "FAIL " + error; evidence.Add(Status); break; } yield return next;
        }
        (work as IDisposable)?.Dispose();
        foreach (var root in roots) if (root != null) { root.GetComponent<EveEntityLoader>()?.Unload(); Destroy(root); }
        File.WriteAllLines(".utmp/LifecycleRuntimeReview/PlayResults.txt", evidence);
        SceneManager.UnloadSceneAsync(disposable); EditorApplication.delayCall += EditorApplication.ExitPlaymode;
    }
    private IEnumerator Sequence()
    {
        yield return null;
        if (!EveRegistrySession.IsReady) throw new Exception("Authored Play runtime is not Ready.");
        var session = EveRegistrySession.Context;
        if (session.Cleanup != null) throw new Exception("Play fixture requires unconfigured cleanup session.");
        var stopwatch = Stopwatch.StartNew(); var queue = session.ConfigureCleanup(new CleanupPolicy(8, 8, 4, 1, 32, 1, 2, 1), () => stopwatch.Elapsed.TotalMilliseconds, () => 0);
        var first = Actor(779001); var second = Actor(779002); first.Prepare(session.Registry); second.Prepare(session.Registry);
        Check(!first.IsReady && !second.IsReady && first.GetComponent<PNBuffManager>().Presentation == null, "Prepared runtime objects stay unpublished across frames");
        yield return null; Check(!first.IsReady && !first.GetComponent<PNStat>().IsReady, "Unity Update does not publish Prepared modules");
        queue.Track(first, () => Destroy(first.gameObject)); queue.Track(second, () => Destroy(second.gameObject));
        EveEntityLoader.CommitBatch(new[] { first, second }); first.gameObject.SetActive(true); second.gameObject.SetActive(true);
        Check(first.IsReady && second.IsReady, "actual Play batch becomes Ready before activation callbacks");
        yield return null;
        queue.Retire(first); Check(!first.IsReady && !first.gameObject.activeInHierarchy && queue.ResidentCount == 2, "real render/gameplay hidden while retirement remains resident");
        EveRegistrySession.End(); Check(EveRegistrySession.State == EveSessionState.Stopping && !second.gameObject.activeInHierarchy, "EndSession hides tracked objects and leaves bounded cleanup pending");
        for (int i = 0; i < 100 && EveRegistrySession.State != EveSessionState.Stopped; i++) yield return null;
        Check(EveRegistrySession.State == EveSessionState.Stopped && queue.ResidentCount == 0 && session.Entities.Count == 0, "persistent Unity pump drains Stopping with no Ready session");
        yield return null;
        Check(first == null && second == null, "owned temporary roots returned/destroyed only after cleanup");
        Status = "PASS " + checks + " actual Play publication/automatic cleanup checks"; evidence.Add(Status);
    }
}
#endif
