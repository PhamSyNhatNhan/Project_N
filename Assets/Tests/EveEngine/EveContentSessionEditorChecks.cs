#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using EveEngine;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public sealed class EveNativeContentProbeRuntime : EveGameRuntime
{
    public int Reads;
    protected override void Awake() { } // The fixture explicitly chooses content-only vs play initialization.
    protected override IEnumerable<EveContentDocument> ReadContentDocuments()
    {
        Reads++;
        return new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"),
            Path.Combine(Application.persistentDataPath, "NoNativeProbeMods"), Array.Empty<string>()).ReadDocuments();
    }
    protected override EveCodeModSession LoadCodeMods() => EveCodeModSession.Empty();
    protected override IEnumerable<EveOverrideSelection> ReadOverrideSelections() => Array.Empty<EveOverrideSelection>();
}

public sealed class EveNativeContentCleanupProbe : MonoBehaviour, EveIEntityLoadStep
{
    public bool Fail;
    public int Unloads;
    public int LoadOrder => 10;
    public IEnumerable<Type> Dependencies => new[] { typeof(EveStat) };
    public void Load(EveEntityLoadContext context) { }
    public void Unload() { Unloads++; if (Fail) throw new InvalidOperationException("Expected native cleanup failure."); }
}

public static class EveContentSessionEditorChecks
{
    public static string Run()
    {
        if (Application.isPlaying || EveRegistrySession.IsReady || EveRegistrySession.State == EveSessionState.Stopping)
            throw new InvalidOperationException("Idle Edit Mode required.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty; float scale = Time.timeScale;
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject root = null, actor = null;
        EveNativeContentProbeRuntime runtime = null;
        int checks = 0, ready = 0, contentReady = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
        void Reject(Action action, string name) { try { action(); } catch (InvalidOperationException) { checks++; return; } catch (AggregateException) { checks++; return; } throw new Exception(name); }
        using (EveEventManager.Global.Subscribe<EveRuntimeReady>(EveRuntimeEventIds.Ready, (_, __) => ready++))
        using (EveEventManager.Global.Subscribe<EveContentReady>(EveRuntimeEventIds.ContentReady, (_, __) => contentReady++))
        try
        {
            root = new GameObject("Temporary content session runtime"); SceneManager.MoveGameObjectToScene(root, preview);
            runtime = root.AddComponent<EveNativeContentProbeRuntime>();
            runtime.EnsureContentLoaded();
            var content = runtime.Content; var hub = content.Registry;
            Check(runtime.Reads == 1 && contentReady == 1 && ready == 0 && !EveRegistrySession.IsReady, "Content-only startup created a play.");
            Reject(() => { var context = EveRegistrySession.Context; }, "Content-ready exposed a fake play context.");
            runtime.BeginSession(EveSessionRole.Offline);
            var first = EveRegistrySession.Context;
            actor = new GameObject("Temporary content session actor"); actor.SetActive(false); SceneManager.MoveGameObjectToScene(actor, preview);
            var loader = actor.AddComponent<EveEntityLoader>(); var stat = actor.AddComponent<EveStat>();
            var settings = new SerializedObject(stat); settings.FindProperty("characterId").stringValue = "project_n:eve"; settings.ApplyModifiedPropertiesWithoutUndo();
            var probe = actor.AddComponent<EveNativeContentCleanupProbe>();
            loader.Load(hub);
            var oldContext = loader.LoadContext;
            Check(loader.IsReady && first.Entities.Count == 1, "Native entity did not publish in first play.");
            probe.Fail = true;
            Reject(runtime.EndSession, "Cleanup failure did not block session end.");
            Check(!first.IsActive && !oldContext.CanUse && EveRegistrySession.PendingCleanupCount == 1, "Failed native cleanup lost ownership.");
            Reject(() => runtime.BeginSession(EveSessionRole.Host), "Native start bypassed pending cleanup.");
            probe.Fail = false; EveRegistrySession.RetryCleanup();
            Check(loader.State == EveEntityLoadState.Idle && runtime.IsContentReady, "Retry destroyed reusable content.");
            loader.GetComponent<EveEntityIdentity>().ResetIdentity();
            runtime.BeginSession(EveSessionRole.Host);
            var second = EveRegistrySession.Context;
            Check(ReferenceEquals(hub, second.Registry) && second.SessionId != first.SessionId && runtime.Reads == 1, "Native Role switch reloaded content.");
            loader.GetComponent<EveEntityIdentity>().Assign(second.Entities.AllocateHostId(), EveId.Parse("project_n:eve"), 0);
            loader.Load(hub);
            Check(loader.IsReady && !oldContext.CanUse && loader.LoadContext.Session == second, "Native reuse admitted stale load context.");
            runtime.EndSession();
            Check(second.Entities.Count == 0 && ready == 2 && contentReady == 1 && !second.IsActive, "Native end/start event or cleanup count mismatch.");
            runtime.UnloadContent();
            Check(content.State == EveSessionState.Stopped && content.Registry == null, "Content unload kept its registry.");
            runtime.EnsureContentLoaded();
            Check(runtime.Reads == 2 && runtime.Content.GenerationId != content.GenerationId && !EveRegistrySession.IsReady, "Explicit reload failed or started gameplay.");
        }
        finally
        {
            if (actor != null) { var probe = actor.GetComponent<EveNativeContentCleanupProbe>(); if (probe != null) probe.Fail = false; }
            if (EveRegistrySession.State == EveSessionState.Stopping) EveRegistrySession.RetryCleanup();
            if (runtime != null) runtime.UnloadContent();
            if (actor != null) UnityEngine.Object.DestroyImmediate(actor);
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(preview);
        }
        Check(original.isDirty == dirty && SceneManager.GetActiveScene() == original && Time.timeScale == scale, "Native check modified authored scene/time.");
        return "PASS " + checks + " native content/play lifecycle checks; scene dirty " + dirty + " -> " + original.isDirty;
    }
}
#endif
