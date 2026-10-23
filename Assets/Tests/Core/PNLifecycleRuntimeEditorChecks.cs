#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using EveEngine.Experimental.Lifecycle;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PNLifecycleCommitProbe : MonoBehaviour, EveIEntityLoadStep, EveIEntityCommitStep
{
    public bool FailCommit, FailCleanup;
    public int Loads, Commits, Cleanups;
    public int LoadOrder => 20;
    public IEnumerable<Type> Dependencies => new[] { typeof(PNBuffManager) };
    public void Load(EveEntityLoadContext context) { if (context.CanUse) throw new Exception("Preparation was published."); Loads++; }
    public void CommitEntity(EveEntityLoadContext context) { Commits++; if (context.CanUse) throw new Exception("Commit published early."); if (FailCommit) throw new Exception("Expected commit fixture fault"); }
    public void EntityPublished(EveEntityLoadContext context) { if (!context.CanUse) throw new Exception("Notification ran before publication."); }
    public void Unload() { Cleanups++; if (FailCleanup) throw new Exception("Expected cleanup fixture fault"); }
}
public static class PNLifecycleRuntimeEditorChecks
{
    public static string Run()
    {
        if (Application.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Stopped Edit session required.");
        const string folder = ".utmp/LifecycleRuntimeReview"; Directory.CreateDirectory(folder);
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var preview = EditorSceneManager.NewPreviewScene(); var roots = new List<GameObject>(); var evidence = new List<string>(); int checks = 0; double time = 0;
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        var compatibility = EveContentCompatibility.Create(hub, docs, "test:lifecycle_runtime");
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; evidence.Add("PASS " + label); }
        void Reject(Action work, string label) { bool rejected = false; try { work(); } catch { rejected = true; } Check(rejected, label); }
        EveEntityLoader Factory(bool render = true)
        {
            var root = new GameObject("Disposable lifecycle runtime actor"); roots.Add(root); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, preview);
            root.AddComponent<EveEntityIdentity>(); root.AddComponent<CapsuleCollider>(); root.AddComponent<EveTimeScale>(); root.AddComponent<PNEnemyStat>(); root.AddComponent<PNAbilityManager>(); root.AddComponent<PNBuffManager>().RenderPresentation = render;
            root.AddComponent<PNLifecycleCommitProbe>(); return root.AddComponent<EveEntityLoader>();
        }
        void Begin(EveSessionRole role) { EveRegistrySession.Begin(); EveRegistrySession.Start(hub, role, compatibility); }
        void Finish() { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); int safety = 1000; while (EveRegistrySession.State == EveSessionState.Stopping && safety-- > 0) EveRegistrySession.PumpCleanup(); if (EveRegistrySession.State != EveSessionState.Stopped) throw new Exception("Fixture cleanup did not finish."); }
        var cleanupPolicy = new CleanupPolicy(24, 24, 16, 2, 32, 1, 2, 1);
        try
        {
            Begin(EveSessionRole.Host); var session = EveRegistrySession.Context;
            double visualTime = 0; int detachedReleased = 0;
            var detached = PNBuffDetachedEffects.Configure(session, 1, 1, () => visualTime, true);
            Check(detached.TryCreate(PNBuffEndReason.Expired, 100, resources => resources.Own(new PNBuffRelease(() => detachedReleased++))), "detached presentation owns independent resources");
            Check(!detached.TryCreate(PNBuffEndReason.Death, 1, resources => {}) && detached.DroppedCount == 1, "detached presentation drops new effect at cap");
            visualTime = 1; detached.Advance(); Check(detached.ActiveCount == 0 && detachedReleased == 1, "absolute detached lifetime clamps mod duration");
            Check(!detached.TryCreate(PNBuffEndReason.Unload, 1, resources => {}) && !detached.TryCreate(PNBuffEndReason.SessionEnded, 1, resources => {}), "silent cleanup reasons create no detached effect");
            detached.TryCreate(PNBuffEndReason.Death, 1, resources => resources.Own(new PNBuffRelease(() => detachedReleased++)));
            var a = Factory(); var b = Factory(); a.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(771001), EveId.Parse("project_n:dummy"), 0); b.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(771002), EveId.Parse("project_n:dummy"), 0);
            a.transform.position = new Vector3(2, 0, 1); b.transform.position = new Vector3(5, 0, 1);
            a.BeginPrepare(hub); Check(!a.PrepareNext() && !a.IsReady, "single step prepare does not publish");
            while (a.State == EveEntityLoadState.Preparing) a.PrepareNext(); b.Prepare(hub);
            Check(a.State == EveEntityLoadState.Prepared && !a.GetComponent<PNStat>().IsReady && a.GetComponent<PNStat>().IsInitialized, "Prepared Stat initialized but guarded");
            Check(session.Entities.Snapshot().Count == 0 && !a.GetComponent<PNControlManager>().IsReady && !a.GetComponent<PNAbilityManager>().IsReady && a.GetComponent<PNBuffManager>().Presentation == null, "Prepared directory/control/ability/Buff are unpublished");
            Reject(() => a.GetComponent<PNStat>().Evaluate(PNContentIds.AttackPower), "prepared Stat API rejects use");
            Reject(() => a.GetComponent<PNBuffManager>().Buffs.Apply(null), "prepared Buff API rejects use");
            a.gameObject.SetActive(true); Check(a.State == EveEntityLoadState.Prepared, "OnEnable does not commit Prepared"); a.gameObject.SetActive(false);
            a.Load(hub); Check(a.State == EveEntityLoadState.Prepared, "Load convenience does not implicitly commit existing Prepared");
            int notices = 0;
            using (EveEventManager.Global.Subscribe<EveEntityReady>(EveRuntimeEventIds.EntityReady, (c, n) => { notices++; Check(a.IsReady && b.IsReady && a.GetComponent<PNBuffManager>().Presentation != null && b.GetComponent<PNBuffManager>().Presentation != null, "Ready observer sees whole batch"); })) EveEntityLoader.CommitBatch(new[] { a, b });
            Check(notices == 2 && session.Entities.Snapshot().Count == 2, "atomic batch publishes both exactly once");
            a.gameObject.SetActive(true); b.gameObject.SetActive(true); var manager = a.GetComponent<PNBuffManager>(); manager.Advance(0); b.GetComponent<PNBuffManager>().Advance(0);
            var source = PNBuffSource.Capture(b.GetComponent<PNStat>());
            manager.Buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:burn"), source));
            Check(manager.Publisher.Epoch == a.Generation && manager.Replica.Epoch == a.Generation, "Buff epoch comes from actor generation");
            var replacement = Factory(); replacement.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(771001), EveId.Parse("project_n:dummy"), 0, 2); replacement.Prepare(hub);
            Check(!session.Entities.TryGet(new EveEntityId(771001), out _) && a.IsReady, "new reservation hides bare lookup without publishing candidate");
            var oldHealth = a.GetComponent<PNStat>().GetCurrent(PNContentIds.Health);
            a.GetComponent<PNStat>().ReceiveDamage(source.Snapshot.CreateDamage(1, false, 0, PNContentIds.Magic, PNContentIds.Fire));
            Check(a.GetComponent<PNStat>().GetCurrent(PNContentIds.Health) < oldHealth, "published old target receives damage while its replacement prepares");
            EveEntityLoader.CommitBatch(new[] { replacement }); replacement.gameObject.SetActive(true); replacement.GetComponent<PNBuffManager>().Advance(0);
            Check(!a.IsReady && !a.gameObject.activeInHierarchy && session.Entities.TryGet(new EveEntityId(771001), 2, out var found) && found == replacement, "generation replacement invalidates/hides old object");
            var fallbackTarget = Factory(false); fallbackTarget.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(771004), EveId.Parse("project_n:dummy"), 0); fallbackTarget.Load(hub); fallbackTarget.gameObject.SetActive(true);
            var fallbackBuffs = fallbackTarget.GetComponent<PNBuffManager>(); fallbackBuffs.Advance(0);
            EveHotPathMetrics.Enabled = true; EveHotPathMetrics.Drain();
            var dot = fallbackBuffs.Buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:burn"), source));
            Check(!EveHotPathMetrics.Drain().ContainsKey("buff.encode"), "solo presentation does not encode an unused network delta"); EveHotPathMetrics.Enabled = false;
            var originalCapture = b.GetComponent<PNStat>().CaptureBuffCombatSource(); double capturedAttack = originalCapture.Evaluate(PNContentIds.AttackPower);
            b.GetComponent<PNStat>().SetSource(EveId.Parse("test:copy_isolation"), new[] { new EveStatModifier(PNContentIds.AttackPower, 10, 0) });
            Check(originalCapture.Evaluate(PNContentIds.AttackPower) == capturedAttack, "bulk combat copy preserves isolation from later source mutation");
            fallbackBuffs.Buffs.Trigger(dot.Handle); var health = fallbackTarget.GetComponent<PNStat>().GetCurrent(PNContentIds.Health);
            var hiddenSource = Factory(false); hiddenSource.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(771002), EveId.Parse("project_n:dummy"), 0, 2); hiddenSource.Prepare(hub);
            Check(source.LoadedSource == b.GetComponent<PNStat>() && b.IsReady && !session.Entities.TryGet(new EveEntityId(771002), out _), "exact published source remains usable while bare lookup hides it during reservation");
            Check(b.GetComponent<PNStat>().CaptureBuffCombatSource().SourceId == new EveEntityId(771002), "combat capture accepts exact published generation during preparation");
            fallbackBuffs.Buffs.Trigger(dot.Handle);
            Check(fallbackTarget.GetComponent<PNStat>().GetCurrent(PNContentIds.Health) < health, "DOT live capture continues damage during source generation preparation");
            EveEntityLoader.CommitBatch(new[] { hiddenSource });
            Check(source.LoadedSource == null && !b.IsReady, "committed replacement invalidates original source without rebinding to new generation");
            health = fallbackTarget.GetComponent<PNStat>().GetCurrent(PNContentIds.Health); fallbackBuffs.Buffs.Trigger(dot.Handle);
            Check(fallbackTarget.GetComponent<PNStat>().GetCurrent(PNContentIds.Health) < health, "DOT uses last valid capture after source generation commit");
            hiddenSource.Unload();
            b.Unload(); b.GetComponent<EveEntityIdentity>().ResetIdentity(); b.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(771002), EveId.Parse("project_n:dummy"), 0, 3); b.Load(hub);
            Check(b.IsReady && source.LoadedSource == null, "source reference never rebinds to a pooled component with a new load/generation");
            Finish();
            Check(detached.IsDisposed && detachedReleased == 2, "session end releases detached resources independent of actor leases");

            Begin(EveSessionRole.Client); session = EveRegistrySession.Context;
            var c = Factory(false); c.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(771100), EveId.Parse("project_n:dummy"), 0); c.Prepare(hub); c.GetComponent<PNLifecycleCommitProbe>().FailCommit = true;
            Reject(() => EveEntityLoader.CommitBatch(new[] { c }), "commit hook failure refuses publication");
            Check(!session.IsActive && !c.IsReady && session.ConnectionFailureCode == "2003", "commit failure invalidates client before further dispatch"); c.Unload(); Finish();

            Begin(EveSessionRole.Host); session = EveRegistrySession.Context; session.ConfigureCleanup(cleanupPolicy, () => time, () => 0);
            var template = Factory(false); var pool = new EveEasyPooling(template.gameObject, 1, 1);
            try
            {
                var rented = pool.Get(); SceneManager.MoveGameObjectToScene(rented, preview); var loader = rented.GetComponent<EveEntityLoader>();
                loader.GetComponent<EveEntityIdentity>().Assign(session.Entities.AllocateHostId(), EveId.Parse("project_n:dummy"), 0); loader.Prepare(hub);
                Check(session.Cleanup.Track(loader, () => { loader.GetComponent<EveEntityIdentity>().ResetIdentity(); pool.Release(rented); }), "pool object reserved in actual cleanup accounting");
                EveEntityLoader.CommitBatch(new[] { loader }); rented.SetActive(true); session.Cleanup.Retire(loader);
                Check(pool.CountActive == 1 && !rented.activeInHierarchy && session.Cleanup.ResidentCount == 1, "retired object hidden but not prematurely returned to Unity pool");
                while (session.Cleanup.ResidentCount > 0) session.Cleanup.AdvanceFrame();
                var again = pool.Get(); var againLoader = again.GetComponent<EveEntityLoader>();
                Check(again == rented && !againLoader.GetComponent<EveEntityIdentity>().HasId && againLoader.State == EveEntityLoadState.Idle, "pool reuse starts with fully unloaded identity/state");
                againLoader.GetComponent<EveEntityIdentity>().Assign(session.Entities.AllocateHostId(), EveId.Parse("project_n:dummy"), 0); againLoader.Load(hub);
                Check(againLoader.IsReady && againLoader.GetComponent<PNBuffManager>().Buffs.Snapshot().Count == 0, "pooled loader reloads with fresh Buff state"); againLoader.Unload(); pool.Release(again);
            }
            finally { pool.Dispose(); }
            var quarantined = Factory(false); quarantined.GetComponent<EveEntityIdentity>().Assign(session.Entities.AllocateHostId(), EveId.Parse("project_n:dummy"), 0); quarantined.Prepare(hub); EveEntityLoader.CommitBatch(new[] { quarantined });
            var probe = quarantined.GetComponent<PNLifecycleCommitProbe>(); probe.FailCleanup = true;
            session.Cleanup.Track(quarantined, () => UnityEngine.Object.DestroyImmediate(quarantined.gameObject)); session.Cleanup.Retire(quarantined); EveRegistrySession.End();
            for (int i = 0; i < 50; i++) { time++; EveRegistrySession.PumpCleanup(); }
            Check(EveRegistrySession.State == EveSessionState.Stopping && session.Cleanup.Scheduler.StopState == CleanupStopState.Blocked && session.Entities.Count == 1, "real cleanup fault retains directory/object and blocks session restart");
            probe.FailCleanup = false; EveRegistrySession.RetryCleanup(); Finish();
            Check(session.Entities.Count == 0 && session.Cleanup.ResidentCount == 0, "explicit retry releases quarantined native object");
        }
        finally
        {
            try { Finish(); } finally { foreach (var root in roots) if (root != null) UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(preview); }
            File.WriteAllLines(Path.Combine(folder, "NativeResults.txt"), evidence);
            if (original.isDirty != dirty) throw new Exception("Original scene dirty state changed.");
        }
        string result = "PASS " + checks + " native Prepared/Commit, replacement, detached-effect, pool and cleanup checks; policy values are test inputs only";
        File.AppendAllText(Path.Combine(folder, "NativeResults.txt"), result); return result;
    }
}
#endif
