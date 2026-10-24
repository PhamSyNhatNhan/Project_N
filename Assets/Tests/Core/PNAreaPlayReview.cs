#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PNAreaPlayReview : MonoBehaviour
{
    public static string Status = "Idle";
    private readonly List<GameObject> fixtures = new List<GameObject>();
    private readonly List<string> evidence = new List<string>();
    private bool background, cleaned;
    private float originalScale;
    private PNAreaManager areas;
    public static string Run()
    {
        if (!Application.isPlaying || EditorApplication.isPaused || !EveRegistrySession.IsReady) throw new InvalidOperationException("Ready Play required.");
        Status = "Running"; new GameObject("Disposable Area Play review").AddComponent<PNAreaPlayReview>(); return Status;
    }
    private void Start()
    { background = Application.runInBackground; originalScale = Time.timeScale; Application.runInBackground = true; EditorApplication.update += Pump; StartCoroutine(Guarded()); }
    private void Pump() => EditorApplication.QueuePlayerLoopUpdate();
    private void Check(bool condition, string message) { if (!condition) throw new Exception(message); evidence.Add("PASS " + message); }
    private PNStat Entity(long id, Vector3 position)
    {
        var root = new GameObject("Area Play fixture " + id); fixtures.Add(root); root.SetActive(false); root.transform.position = position; root.layer = 31;
        root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse("project_n:dummy"), 0); root.AddComponent<EveTimeScale>(); root.AddComponent<CapsuleCollider>().height = 1;
        var stat = root.AddComponent<PNEnemyStat>(); root.AddComponent<PNSkill>(); root.AddComponent<EveEntityLoader>().Load(EveRegistrySession.Current); root.SetActive(true); return stat;
    }
    private IEnumerator Guarded()
    {
        var iterator = Review();
        try
        {
            while (true)
            {
                object current;
                try { if (!iterator.MoveNext()) break; current = iterator.Current; }
                catch (Exception error) { Status = "FAIL " + error; evidence.Add(Status); yield break; }
                yield return current;
            }
            Status = "PASS " + evidence.Count + " area Play checks"; evidence.Add(Status);
        }
        finally { (iterator as IDisposable)?.Dispose(); Cleanup(); Directory.CreateDirectory(".utmp/AreaReview"); File.WriteAllLines(".utmp/AreaReview/PlayResults.txt", evidence); Destroy(gameObject); }
    }
    private IEnumerator Review()
    {
        var managerRoot = new GameObject("Area Play isolated manager"); fixtures.Add(managerRoot); areas = managerRoot.AddComponent<PNAreaManager>(); areas.Initialize(EveRegistrySession.Context);
        var position = new Vector3(70, 0, 70); var source = Entity(990101, position + Vector3.right * 10); var target = Entity(990102, position);
        var captured = PNCombatSource.Capture(source); var buffs = target.GetComponent<PNBuffManager>().Buffs; var clock = target.GetComponent<EveTimeScale>();
        JObject Input(double duration, bool dependent = false) => new JObject { ["radius"] = 2, ["reduction"] = 1, ["minimumReduction"] = .2, ["duration"] = duration, ["dependent"] = dependent, ["layerMask"] = 1 << 31, ["layerMode"] = "Include" };
        PNAreaReceipt Spawn(double duration = 2, bool dependent = false)
        {
            var receipt = areas.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"), Input(duration, dependent), captured, gameObject.scene, position, source));
            Check(receipt.Status == PNAreaStatus.Created, "Actual Play region created"); return receipt;
        }
        yield return null; Physics.SyncTransforms();
        var runtime = UnityEngine.Object.FindFirstObjectByType<EveGameRuntime>(); var runtimeManager = PNAreaManager.Attach(runtime);
        var payload = new JObject { ["operationId"] = "base:create_area", ["data"] = new JObject { ["areaId"] = "base:timescale_area", ["followCaster"] = false, ["input"] = Input(2) } };
        var actionData = new PNSkillData(new JObject(), new PNSkillActionData(EveId.Parse("base:area_action"), perform: payload));
        var context = new PNSkillActionContext(source.GetComponent<PNSkill>(), PNGameplayContentModule.EveBurstEntry, actionData, EveRegistrySession.Current, null, null, null, position: position);
        var action = (PNAreaAction)EveRegistrySession.Current.Actions().GetEffective(EveId.Parse("base:area_action")).Create();
        try
        {
            action.Start(context); action.Perform(context); var result = action.Result; action.Exit(context);
            Check(result.Status == PNAreaStatus.Created && result.Handle.IsAlive && clock.Scale == 0, "Registered Action and Operation transfer region to runtime manager");
            result.Handle.End(); yield return new WaitForSecondsRealtime(.4f);
            Check(runtimeManager.BorrowedCount == 0 && clock.Scale == 1, "Disposed granting Action leaves manager to own cleanup");
        }
        finally { action.Dispose(); }
        var dependent = Spawn(3, true); Check(clock.Scale == 0, "Actual Play target reaches zero scale");
        source.gameObject.SetActive(false); source.gameObject.SetActive(true);
        Check(!dependent.Handle.IsAlive && clock.Scale == 1, "Actual OnDisable ends dependent region before same-frame reenable");
        yield return new WaitForSecondsRealtime(.4f);
        Check(areas.BorrowedCount == 0, "Actual Update completes fade and pooling");
        var independent = Spawn(.4); var firstId = independent.Handle.InstanceId;
        var received = target.ReceiveDamage(source.CreateDamage(1, false, 0, PNContentIds.True));
        Check(received.AppliedDamage > 0 && clock.Scale == 0, "Damage received while entity timescale zero");
        Time.timeScale = 0; yield return new WaitForSecondsRealtime(.5f);
        Check(independent.Handle.IsAlive, "Game region lifetime pauses with game clock");
        Time.timeScale = originalScale; yield return new WaitForSecondsRealtime(.8f);
        Check(!independent.Handle.IsAlive && clock.Scale == 1 && areas.BorrowedCount == 0, "Region expires by Game clock at entity zero and returns pool");
        var reused = Spawn(); Check(reused.Handle.InstanceId != firstId && !independent.Handle.End() && areas.PooledObjectCount == 1, "Actual pooled reuse rejects old handle");
        using (buffs.Immune(tags: new[] { EveId.Parse("base:timescale") }, purge: true))
        { yield return new WaitForSecondsRealtime(.12f); Check(clock.Scale == 1, "Play immunity purges and blocks region grant"); }
        yield return new WaitForSecondsRealtime(.12f); Check(clock.Scale == 0, "Actual polling grants after immunity lease release");
        target.gameObject.SetActive(false); yield return new WaitForSecondsRealtime(.12f);
        Check(clock.Scale == 1, "Target disabled clears timescale modifiers");
        target.gameObject.SetActive(true); yield return new WaitForSecondsRealtime(.12f);
        Check(clock.Scale == 0, "Target reenable reenters existing region");
        source.GetComponent<EveEntityLoader>().Unload(); yield return new WaitForSecondsRealtime(.12f);
        Check(reused.Handle.IsAlive && clock.Scale == 0, "Independent region remains after source unload");
        reused.Handle.End(); yield return new WaitForSecondsRealtime(.4f);
        Check(clock.Scale == 1 && areas.BorrowedCount == 0, "Source-independent attribution cleanup succeeds");
        // A pending Buff Apply can be revoked before its receipt resolves.
        PNBuffReceipt pending = null;
        buffs.Coordinator.Submit(() => {
            pending = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:timescale_reduction_area"), captured, JObject.Parse("{'reduction':1,'minimumReduction':0}")));
            Check(pending.Status == PNBuffStatus.Pending, "Callback Buff Apply remains Pending");
            buffs.Coordinator.Submit(() => { if (pending.Handle.IsAlive) buffs.Remove(pending.Handle, PNBuffEndReason.Bypass, true); });
        });
        Check(!pending.Handle.IsAlive && clock.Scale == 1, "Pending Apply then exact deferred revoke leaves no modifier");
        var extraScene = SceneManager.CreateScene("Disposable area unload fixture", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        try
        {
            var local = areas.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"), Input(5), captured, extraScene, Vector3.zero));
            Check(local.Status == PNAreaStatus.Created && local.Handle.IsAlive, "Area can use an explicit additive physics scene");
            yield return SceneManager.UnloadSceneAsync(extraScene);
            Check(!local.Handle.IsAlive && areas.BorrowedCount == 0 && areas.PooledObjectCount == 1, "Scene unload retires only that scene's pooled areas");
        }
        finally { if (extraScene.IsValid() && extraScene.isLoaded) SceneManager.UnloadSceneAsync(extraScene); }
        // Keep a region alive during manager teardown and verify cleanup bypasses fade.
        var final = areas.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"), Input(5), captured, gameObject.scene, position));
        areas.Shutdown(); yield return null;
        Check(!final.Handle.IsAlive && areas.BorrowedCount == 0 && areas.PooledObjectCount == 0 && clock.Scale == 1, "Manager shutdown removes active grants and disposes pool immediately");
    }
    private void Cleanup()
    {
        if (cleaned) return; cleaned = true; EditorApplication.update -= Pump;
        areas?.Shutdown();
        foreach (var fixture in fixtures) if (fixture != null) { fixture.GetComponent<EveEntityLoader>()?.Unload(); Destroy(fixture); }
        fixtures.Clear(); Time.timeScale = originalScale; Application.runInBackground = background;
    }
    private void OnDestroy() => Cleanup();
}
#endif
