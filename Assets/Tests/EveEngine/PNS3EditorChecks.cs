#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

public sealed class PNS3AreaProbe : PNAreaInstance
{
    public static bool Fail, FailResources;
    public static int Cleanups;
    protected override void Initialize(PNAreaCreateRequest request) { }
    public override void Advance(float delta) { }
    protected override void Cleanup() { Cleanups++; if(Fail) throw new InvalidOperationException("expected S3 Area cleanup failure"); }
    public override void DisposePooledResources() { if(FailResources) throw new InvalidOperationException("expected S3 Area resource failure"); }
}
public sealed class PNS3AreaModule : EveIContentModule
{
    public void Register(EveRegistryHub hub) => hub.Get<PNAreaDefinition>().Register(new PNAreaDefinition(EveId.Parse("s3:area"), typeof(PNS3AreaProbe), new JObject(), (data, content) => {}, input => {}));
}
public sealed class PNS3NativeRuntime : EveGameRuntime
{
    protected override void Awake() { }
    protected override bool ModsAllowed => false;
    protected override IEnumerable<EveIContentModule> CreateContentModules()
    { foreach(var module in base.CreateContentModules()) yield return module; yield return new PNS3AreaModule(); }
}
public static class PNS3EditorChecks
{
    public static string Run()
    {
        if(Application.isPlaying || EveRegistrySession.State!=EveSessionState.Stopped) throw new InvalidOperationException("Idle Edit required.");
        var original=SceneManager.GetActiveScene(); bool dirty=original.isDirty; float time=Time.timeScale;
        var scene=EditorSceneManager.NewPreviewScene();
        var root=new GameObject("S3 disposable runtime"); SceneManager.MoveGameObjectToScene(root,scene);
        var runtime=root.AddComponent<PNS3NativeRuntime>();
        int checks=0; void Check(bool value,string name) { if(!value) throw new Exception(name); checks++; }
        try
        {
            runtime.EnsureContentLoaded(); var content=runtime.Content;
            for(int i=0;i<3;i++)
            {
                runtime.BeginSession(EveSessionRole.Offline); var session=EveRegistrySession.Context;
                var areas=PNAreaManager.Attach(runtime); PNUiRuntime.Attach(runtime); var ui=root.GetComponent<PNUiRuntime>();
                session.RegisterCleanup(areas.Shutdown); session.RegisterCleanup(ui.Unbind);
                PNUiRuntime.Attach(runtime); PNAreaManager.Attach(runtime);
                Check(root.GetComponents<PNUiRuntime>().Length==1 && root.GetComponents<PNAreaManager>().Length==1 && ui.HudRoot!=null,"services idempotent");
                ui.OpenMenu(); Check(ui.MenuOpen && Time.timeScale==0,"pause owns time");
                if(i==0)
                {
                    var receipt=areas.Create(new PNAreaCreateRequest(EveId.Parse("s3:area"),new JObject(),PNCombatSource.System(session.SessionId,new EveEntityId(-99)),scene,Vector3.zero));
                    Check(receipt.Status==PNAreaStatus.Created,"area fixture creation");
                    PNS3AreaProbe.Fail=true;
                    bool rejected=false; try {runtime.EndSession();}catch(AggregateException){rejected=true;}
                    Check(rejected && EveRegistrySession.State==EveSessionState.Stopping && areas.BorrowedCount==1 && areas.Session==session,"required Area failure retained");
                    Check(ui.HudRoot==null && !ui.MenuOpen && Time.timeScale==time,"UI and pause unbound despite Area failure");
                    rejected=false;try {runtime.BeginSession(EveSessionRole.Offline);}catch(InvalidOperationException){rejected=true;}
                    Check(rejected,"cleanup blocks next play");
                    PNS3AreaProbe.Fail=false; PNS3AreaProbe.FailResources=true;
                    rejected=false;try {EveRegistrySession.RetryCleanup();}catch(AggregateException){rejected=true;}
                    Check(rejected && EveRegistrySession.State==EveSessionState.Stopping,"resource cleanup also required");
                    int cleaned=PNS3AreaProbe.Cleanups;
                    PNS3AreaProbe.FailResources=false; EveRegistrySession.RetryCleanup();
                    Check(cleaned==PNS3AreaProbe.Cleanups,"successful gameplay cleanup not repeated for resource retry");
                }
                else runtime.EndSession();
                Check(!session.IsActive && ui.HudRoot==null && areas.Session==null && areas.PooledObjectCount==0 && runtime.Content==content && Time.timeScale==time,"end retains content and clears services");
            }
        }
        finally
        {
            PNS3AreaProbe.Fail=PNS3AreaProbe.FailResources=false;
            if(EveRegistrySession.State==EveSessionState.Stopping) EveRegistrySession.RetryCleanup();
            runtime.UnloadContent(); root.GetComponent<PNUiRuntime>()?.Unbind(); UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene);
        }
        Check(original.isDirty==dirty && SceneManager.GetActiveScene()==original && Time.timeScale==time,"original scene/time preserved");
        return "PASS "+checks+" S3 native UI/Area/session checks; dirty "+dirty+" -> "+original.isDirty;
    }
}
#endif
