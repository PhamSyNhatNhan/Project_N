#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PNBuffFaultStat : PNEnemyStat
{
    public bool FailHealthRead;
    public override bool IsDead => FailHealthRead ? throw new OverflowException("Expected Buff automatic-update fixture fault") : base.IsDead;
}
public sealed class PNBuffLeaseProbe : PNBuffBehavior
{
    public readonly List<IDisposable> Leases = new List<IDisposable>();
    public void Grant(PNBuffContext context)
    { Leases.Add(context.ModifyStats(new[] { new EveStatModifier(PNContentIds.Health, 3, 0) })); Leases.Add(context.ModifyStats(new[] { new EveStatModifier(PNContentIds.Health, .2, 1) })); }
    public override void OnApply(PNBuffContext context, JToken input) { Grant(context); context.Lock(new[] { PNControlIds.Move }); }
    public override void OnResume(PNBuffContext context) => Grant(context);
}
public sealed class PNBuffCoopNativeModule : EveIContentModule
{
    public void Register(EveRegistryHub hub)
    { hub.Buffs().Register(new PNBuffDefinition(EveId.Parse("test:lease_probe"), EveId.Parse("test:lease_probe_impl"), _ => new PNBuffLeaseProbe(), null)); }
}
public static class PNBuffCoopEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Stopped Edit session required.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var docs = new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNBuffCoopNativeModule() }, docs);
        var preview = EditorSceneManager.NewPreviewScene(); int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
        PNStat Entity(long id, bool fault = false)
        {
            var root = new GameObject("Buff coop disposable " + id); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, preview);
            root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse("project_n:dummy"), 0);
            root.AddComponent<CapsuleCollider>(); var stat = fault ? (PNStat)root.AddComponent<PNBuffFaultStat>() : root.AddComponent<PNEnemyStat>();
            var loader = root.AddComponent<EveEntityLoader>(); loader.Load(hub); root.SetActive(true); root.GetComponent<PNBuffManager>().Advance(0); return stat;
        }
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:buff_coop"));
            var source = Entity(987001); var target = Entity(987002, true); var other = Entity(987003);
            var manager = target.GetComponent<PNBuffManager>(); var buffs = manager.Buffs;
            var receipt = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("test:lease_probe"), PNBuffSource.Capture(source)));
            var keys = target.Set.SourceSnapshot().Keys.Where(x => x.Path.StartsWith("buff_source_")).OrderBy(x => x).ToArray();
            Check(keys.Length == 2 && keys[0].Path.EndsWith("00000000000000000001") && keys[1].Path.EndsWith("00000000000000000002"), "Native deterministic separate lease keys: " + receipt.Status + " " + receipt.Error + " keys=" + string.Join(",", keys.Select(x => x.ToString())));
            buffs.Suspend(true); Check(target.Set.SourceSnapshot().Keys.All(x => !x.Path.StartsWith("buff_source_")), "Suspend releases Buff modifier sources");
            buffs.Suspend(false); var resumed = target.Set.SourceSnapshot().Keys.Where(x => x.Path.StartsWith("buff_source_")).ToArray();
            Check(resumed.Length == 2 && resumed.All(x => !keys.Contains(x)), "Resume preserves instance source counter");
            buffs.Remove(receipt.Handle); Check(target.Set.SourceSnapshot().Keys.All(x => !x.Path.StartsWith("buff_source_")), "Removal releases resumed leases");
            var burn = other.GetComponent<PNBuffManager>().Buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:burn"), PNBuffSource.Capture(source)));
            var otherManager = other.GetComponent<PNBuffManager>();
            Check(other.GetComponentsInChildren<PNBuffElementVisual>().Length == 1, "Host creates elemental presenter through common replica");
            var visual = other.GetComponentInChildren<PNBuffElementVisual>(); var remote = new PNBuffReplica(hub, source.SessionId, other.EntityId, other);
            try
            {
                remote.Receive(otherManager.Publisher.Snapshot(), 0);
                Check(remote.Snapshot().Count == otherManager.Replica.Snapshot().Count && other.GetComponentInChildren<PNBuffElementVisual>().GrantCount == 2, "Proxy consumes snapshot message without gameplay Buff execution");
                var state = remote.Snapshot()[0]; var snapshot = new PNBuffWireMessage(source.SessionId, other.EntityId, remote.Epoch, remote.Sequence + 1, PNBuffWireKind.Snapshot, new[] { state });
                remote.Receive(snapshot, 0); Check(other.GetComponentInChildren<PNBuffElementVisual>() == visual, "Resync retains existing grouped renderer");
                remote.Receive(new PNBuffWireMessage(source.SessionId, other.EntityId, remote.Epoch, remote.Sequence + 1, PNBuffWireKind.Snapshot), 0);
                Check(other.GetComponentInChildren<PNBuffElementVisual>() == visual && visual.GrantCount == 1, "Snapshot removal releases only proxy contribution");
            }
            finally { remote.Dispose(); }
            // Expected exception is recorded once by the production manager boundary.
            buffs.Apply(new PNBuffApplyRequest(EveId.Parse("test:lease_probe"), PNBuffSource.Capture(source)));
            ((PNBuffFaultStat)target).FailHealthRead = true; manager.AdvanceAutomatically(0);
            Check(manager.RuntimeFailure is OverflowException && !manager.IsReady, "Automatic runtime fault retires only target collection");
            manager.AdvanceAutomatically(0);
            Check(target.Set.SourceSnapshot().Keys.All(x => !x.Path.StartsWith("buff_source_")), "Fault cleanup releases Stat effects");
            Check(otherManager.IsReady && otherManager.Buffs.Contains(burn.Handle), "Other target and shared coordinator survive fault");
            ((PNBuffFaultStat)target).FailHealthRead = false; target.GetComponent<EveEntityLoader>().Unload(); target.gameObject.SetActive(false);
            target.GetComponent<EveEntityLoader>().Load(hub); target.gameObject.SetActive(true); Check(manager.RuntimeFailure == null && manager.IsReady, "Unload/load resets manager fault");
            otherManager.Buffs.Remove(burn.Handle); Check(other.GetComponentsInChildren<PNBuffElementVisual>().Length == 0, "Final removal releases shared visual");
        }
        finally
        {
            try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            if (original.isDirty != dirty) throw new Exception("Original scene dirty state changed.");
        }
        return "PASS " + checks + " native Buff coop/lease/fault checks; one expected automatic-update exception";
    }
}
#endif

