#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;

public sealed class PNTimeScaleVisualPlayReview : MonoBehaviour
{
    public static string Status = "Idle";
    private readonly List<string> evidence = new List<string>();
    private GameObject sourceRoot, managerRoot;
    private PNAreaManager manager;
    private bool background, cleaned;
    private float originalScale;
    public static string Run()
    {
        if (!Application.isPlaying || EditorApplication.isPaused || !EveRegistrySession.IsReady) throw new InvalidOperationException("Ready unpaused Play required.");
        Status = "Running"; new GameObject("Disposable timescale visual Play review").AddComponent<PNTimeScaleVisualPlayReview>(); return Status;
    }
    private void Start() { background = Application.runInBackground; originalScale = Time.timeScale; Application.runInBackground = true; EditorApplication.update += Pump; StartCoroutine(Guarded()); }
    private void Pump() => EditorApplication.QueuePlayerLoopUpdate();
    private void Check(bool ok, string label) { if (!ok) throw new Exception(label); evidence.Add("PASS " + label); }
    private IEnumerator Guarded()
    {
        var iterator = Review();
        try
        {
            while (true)
            {
                object next; try { if (!iterator.MoveNext()) break; next = iterator.Current; }
                catch (Exception error) { Status = "FAIL " + error; evidence.Add(Status); yield break; }
                yield return next;
            }
            Status = "PASS " + evidence.Count + " timescale visual Play checks"; evidence.Add(Status);
        }
        finally { (iterator as IDisposable)?.Dispose(); Cleanup(); Directory.CreateDirectory(".utmp/TimeScaleReadableReview"); File.WriteAllLines(".utmp/TimeScaleReadableReview/PlayResults.txt", evidence); Destroy(gameObject); }
    }
    private IEnumerator Review()
    {
        managerRoot = new GameObject("Visual Play fixture manager"); manager = managerRoot.AddComponent<PNAreaManager>(); manager.Initialize(EveRegistrySession.Context);
        sourceRoot = new GameObject("Visual Play fixture source"); sourceRoot.SetActive(false); sourceRoot.transform.position = new Vector3(500,5,500);
        sourceRoot.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(992101), EveId.Parse("project_n:dummy"), 0);
        var sourceClock = sourceRoot.AddComponent<EveTimeScale>(); var source = sourceRoot.AddComponent<PNEnemyStat>(); sourceRoot.AddComponent<EveEntityLoader>().Load(EveRegistrySession.Current); sourceRoot.SetActive(true);
        var input = JObject.Parse("{'radius':2.5,'reduction':0.9,'minimumReduction':0.9,'duration':15,'dependent':false,'layerMask':0,'layerMode':'Include'}");
        var capture = PNBuffSource.Capture(source);
        var first = manager.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"), input, capture, gameObject.scene, sourceRoot.transform.position));
        Check(first.Status == PNAreaStatus.Created, "Actual Play pooled particle region created");
        var region = gameObject.scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNTimeScaleArea>(true)).First(x => x.Handle.Equals(first.Handle) && x.IsRunning);
        var system = region.GetComponentInChildren<ParticleSystem>(); var dustObject = system.gameObject;
        yield return new WaitForSecondsRealtime(.7f);
        Check(region.DustCount > 0 && region.DustCount <= 20 && system.isPaused && !system.main.useUnscaledTime, "Actual Game Update manually simulates bounded particles without automatic double stepping");
        float particleTime = region.DustTime; int particleCount = region.DustCount;
        Time.timeScale = 0; yield return new WaitForSecondsRealtime(.3f);
        Check(region.DustTime == particleTime && region.DustCount == particleCount && first.Handle.IsAlive, "Game pause freezes particles and area together");
        Time.timeScale = originalScale;
        sourceClock.AddOwnedModifier(EveId.Parse("test:source_zero"), EveTimeModifierKind.Scale, -1, source);
        yield return new WaitForSecondsRealtime(.3f);
        Check(sourceClock.Scale == 0 && region.DustTime > particleTime, "Particles use Game clock independently of source entity zero scale");
        sourceRoot.GetComponent<EveEntityLoader>().Unload(); particleTime = region.DustTime;
        yield return new WaitForSecondsRealtime(.2f);
        Check(first.Handle.IsAlive && region.DustTime > particleTime, "Independent visual region continues after source unload");
        first.Handle.End(); yield return new WaitForSecondsRealtime(.4f);
        Check(manager.BorrowedCount == 0 && system.particleCount == 0 && !dustObject.activeInHierarchy, "Fade completes then clears particles and returns pooled region");
        var second = manager.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"), input, capture, gameObject.scene, new Vector3(500,5,500)));
        var reused = gameObject.scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNTimeScaleArea>(true)).First(x => x.IsRunning && x.Handle.Equals(second.Handle));
        Check(reused.GetComponentInChildren<ParticleSystem>().gameObject == dustObject && reused.DustTime == 0 && reused.DustCount == 0, "Actual pool reuse retains particle object and resets clock/state");
        manager.Shutdown(); yield return null;
        Check(dustObject == null && manager.PooledObjectCount == 0, "Pool disposal releases particle GameObject and material");
        foreach (var floor in UnityEngine.Object.FindObjectsByType<ErModeGroundVisual>())
        {
            var depth = floor.GetComponentsInChildren<MeshRenderer>().First(x => x.name == "Owned floor depth geometry");
            Check(depth.sharedMaterial.FindPass("VFXFloorDepth") >= 0 && depth.sharedMaterial.FindPass("DepthOnly") < 0 && floor.GetComponent<SpriteRenderer>().sharedMaterial.shader.name == "Universal Render Pipeline/2D/Sprite-Unlit-Default", "Actual scene floor contributes only private VFX depth and preserves sprite rendering");
        }
    }
    private void Cleanup()
    {
        if (cleaned) return; cleaned = true; EditorApplication.update -= Pump; manager?.Shutdown();
        if (sourceRoot != null) { sourceRoot.GetComponent<EveEntityLoader>()?.Unload(); Destroy(sourceRoot); }
        if (managerRoot != null) Destroy(managerRoot);
        Time.timeScale = originalScale; Application.runInBackground = background;
    }
    private void OnDestroy() => Cleanup();
}
#endif
