#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using EveEngine.Experimental.Lifecycle;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PNBuffTeardownEditorChecks
{
    public static string Run()
    {
        if (Application.isPlaying || EveRegistrySession.State != EveSessionState.Stopped)
            throw new InvalidOperationException("Idle Edit required.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"),
            Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        var evidence = new List<string>(); int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; evidence.Add("PASS " + name); }
        void Reject(Action action, string name)
        { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } Check(rejected, name); }
        for (int variant = 0; variant < 5; variant++)
        {
            var scene = EditorSceneManager.NewPreviewScene(); EveEntityLoader loader = null; PNAreaManager areas = null;
            try
            {
                EveRegistrySession.Begin();
                EveRegistrySession.Start(hub, variant == 4 ? EveSessionRole.Client : EveSessionRole.Host,
                    EveContentCompatibility.Create(hub, docs, "test:buff-teardown"));
                var session = EveRegistrySession.Context;
                var root = new GameObject("Buff teardown manager"); SceneManager.MoveGameObjectToScene(root, scene);
                areas = root.AddComponent<PNAreaManager>(); areas.Initialize(session);
                var go = new GameObject("Buff teardown target"); go.SetActive(false); SceneManager.MoveGameObjectToScene(go, scene); go.layer = 31;
                go.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(990030 + variant), EveId.Parse("project_n:dummy"), 0);
                go.AddComponent<CapsuleCollider>().height = 1; var clock = go.AddComponent<EveTimeScale>();
                var stat = go.AddComponent<PNEnemyStat>(); loader = go.AddComponent<EveEntityLoader>(); loader.Load(hub); go.SetActive(true);
                go.GetComponent<PNBuffManager>().Advance(0);
                var buffs = go.GetComponent<PNBuffManager>().Buffs; int errors = 0; buffs.Failed += _ => errors++;
                if (variant == 4)
                {
                    using (var clientTarget = new PNBuffCollection(hub, session.SessionId, services: new PNBuffServices(stat)))
                        Reject(() => clientTarget.ReleaseOwned(default), "Client load cannot borrow detached default mutation permission");
                    Reject(() => buffs.ReleaseOwned(default), "Client cannot release owned Buff while active");
                    loader.InvalidateAndHide();
                    Reject(() => buffs.ReleaseOwned(default), "Client cannot gain cleanup authority through invalidation");
                    continue;
                }
                var source = new PNBuffSource(session.SessionId, stat.EntityId);
                var request = new PNBuffApplyRequest(EveId.Parse("base:timescale_reduction"), source,
                    JObject.Parse("{'reduction':0.5,'minimumReduction':0.2,'duration':20}"));
                if (variant == 0)
                {
                    var owned = buffs.Apply(request).Handle;
                    Check(owned.IsAlive, "Held-release fixture has a live Buff before hold");
                    using (go.GetComponent<EveEntityIdentity>().HoldGameplay())
                    {
                        Reject(() => buffs.Apply(request), "Held entity still rejects ordinary Apply");
                        PNBuffReceipt queued = null;
                        buffs.Coordinator.Submit(() =>
                        {
                            queued = buffs.ReleaseOwned(owned);
                            Check(queued.Status == PNBuffStatus.Pending && owned.IsAlive, "Held release queues at callback boundary");
                        });
                        Check(queued.Status == PNBuffStatus.Removed && !owned.IsAlive && clock.Scale == 1, "Queued owned release drains exact modifier");
                        Check(buffs.ReleaseOwned(owned).Status == PNBuffStatus.Rejected, "Repeated owned release is harmless");
                    }
                    using (var wrongSession = new PNBuffCollection(hub, Guid.NewGuid(), services: new PNBuffServices(stat)))
                        Reject(() => wrongSession.ReleaseOwned(default), "Another session cannot borrow target authority");
                    using var retained = new PNBuffCollection(hub, session.SessionId, services: new PNBuffServices(stat));
                    var oldCollection = buffs; var stale = buffs.Apply(request).Handle;
                    loader.Unload(); loader.Load(hub); buffs = go.GetComponent<PNBuffManager>().Buffs; buffs.Failed += _ => errors++;
                    Reject(() => retained.ReleaseOwned(default), "Undisposed retained collection cannot borrow a later load context");
                    Reject(() => oldCollection.ReleaseOwned(stale), "Disposed old load cannot borrow reloaded authority");
                    var current = buffs.Apply(request).Handle;
                    Check(buffs.ReleaseOwned(stale).Status == PNBuffStatus.Rejected && current.IsAlive, "Foreign old handle cannot remove new load Buff");
                    buffs.ReleaseOwned(current);
                }
                Physics.SyncTransforms();
                var input = JObject.Parse("{'radius':2,'reduction':0.5,'minimumReduction':0.2,'duration':20,'dependent':false,'layerMask':-2147483648,'layerMode':'Include'}");
                var area = areas.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"), input, source, scene, Vector3.zero));
                Check(area.Status == PNAreaStatus.Created && buffs.Snapshot().Count == 1 && clock.Scale == .5f, "Case " + variant + " admits real Area Buff");
                session.RegisterCleanup(areas.Shutdown);
                if (variant == 1) loader.InvalidateAndHide();
                if (variant == 2) loader.Unload();
                if (variant == 3)
                {
                    var queue = session.ConfigureCleanup(new CleanupPolicy(8, 8, 4, 100, 100, 100, 3, 1),
                        () => Time.realtimeSinceStartupAsDouble * 1000, () => 100);
                    Check(queue.Track(loader, () => { }), "Real cleanup queue tracks target"); queue.Retire(loader);
                }
                if (variant == 1 || variant == 3)
                {
                    Reject(() => buffs.Apply(request), "Case " + variant + " invalidation still blocks Apply");
                    Reject(() => buffs.Remove(buffs.Snapshot()[0].Handle, PNBuffEndReason.Bypass, true), "Case " + variant + " invalidation still blocks ordinary removal");
                }
                EveRegistrySession.End();
                Check(errors == 0 && session.PendingServiceCleanupCount == 0 && !area.Handle.IsAlive && buffs.Snapshot().Count == 0,
                    "Case " + variant + " Area release drains without lifetime/cleanup errors");
                if (variant == 3)
                {
                    Check(EveRegistrySession.State == EveSessionState.Stopping && session.Cleanup.ResidentCount == 1 && EveRegistrySession.LastError == null,
                        "Queued entity disposal retains normal pending stop without error");
                    for (int pump = 0; pump < 5 && EveRegistrySession.State != EveSessionState.Stopped; pump++) EveRegistrySession.PumpCleanup();
                    Check(session.Cleanup.ResidentCount == 0 && buffs.IsDisposed, "Cleanup pump drains original collection");
                }
                Check(EveRegistrySession.State == EveSessionState.Stopped, "Case " + variant + " finishes Stopped");
                EveRegistrySession.End(); Check(EveRegistrySession.State == EveSessionState.Stopped, "Case " + variant + " repeated End is safe");
            }
            finally
            {
                loader?.Unload(); areas?.Shutdown(); EveRegistrySession.End(); EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        Check(original == SceneManager.GetActiveScene() && original.isDirty == dirty, "Authored active scene and dirty state preserved");
        Directory.CreateDirectory(".utmp/BuffTeardownReview"); File.WriteAllLines(".utmp/BuffTeardownReview/NativeResults.txt", evidence);
        return "PASS " + checks + " native Buff teardown checks";
    }
}
#endif
