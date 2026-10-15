#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PNStep3bBatchChecks
{
    private sealed class Empty : PNBuffBehavior { }
    public static void Run()
    {
        const string folder=".utmp/Step3b";
        Directory.CreateDirectory(folder);
        string batchHost=null;
        try
        {
            var scene=SceneManager.GetActiveScene(); bool dirty=scene.isDirty;
            // A fresh batch Editor starts in an untitled empty scene; save only
            // this owned scene so Unity permits the additive fixture build.
            if(UnityEngine.Application.isBatchMode && string.IsNullOrEmpty(scene.path) && !dirty)
            {
                batchHost="Assets/Tests/Generated/Step3bBatch_"+Guid.NewGuid().ToString("N")+".unity";
                if(!EditorSceneManager.SaveScene(scene,batchHost)) throw new Exception("Cannot save owned batch scene.");
            }
            OverflowNative();
            IntegrityNative();
            File.WriteAllText(folder+"/Native.txt", PNBuffEditorChecks.Run()+"\n"+PNLifecycleRuntimeEditorChecks.Run()+"\n"+PNElementalBuffEditorChecks.Run()+"\n"+PNDamageFlowEditorChecks.Run());
            if(SceneManager.GetActiveScene()!=scene || scene.isDirty!=dirty) throw new Exception("Native checks changed authored scene state.");
            File.WriteAllText(folder+"/BuildNormal.txt", PNLifecyclePolicyPlayerBuild.Build());
            File.WriteAllText(folder+"/BuildDevelopment.txt", PNLifecyclePolicyPlayerBuild.BuildDevelopment());
            File.WriteAllText(folder+"/BatchResult.txt", "PASS native checks and normal/Development builds; authored scene preserved");
            if(batchHost!=null) AssetDatabase.DeleteAsset(batchHost);
            if(UnityEngine.Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch(Exception e) { File.WriteAllText(folder+"/BatchFailure.txt",e.ToString()); if(batchHost!=null) AssetDatabase.DeleteAsset(batchHost); if(UnityEngine.Application.isBatchMode) EditorApplication.Exit(1); else throw; }
    }
    public static void OverflowNative()
    {
        var hub=new EveRegistryHub(); new EveBuiltInContentModule().Register(hub); new PNContentModule().Register(hub);
        var id=EveId.Parse("test:step3b_countdown");
        hub.Buffs().Register(new PNBuffDefinition(id,EveId.Parse("test:step3b_impl"),_=>new Empty(),null).WithCountdown(PNBuffCountdown.BottomRight)); hub.Seal();
        var session=Guid.NewGuid();var target=new EveEntityId(1);int checks=0;
        foreach(double rate in new[]{1e-12,1d,1e12})
        using(var replica=new PNBuffReplica(hub,session,target))
        {
            var s=new PNBuffWireState(1,1,id,id,new EveEntityId(2),1,false,default,PNBuffClock.Entity,0,0,rate,9223372036854775808d);
            replica.ApplyLifecycle(new PNBuffWireMessage(session,target,1,0,PNBuffWireKind.Snapshot,new[]{s}),0);
            if(replica.PresentationSnapshot()[0].Numbers.BottomRight!=null)throw new Exception("Native overflow must hide.");checks++;
            replica.Advance(8192/rate);
            if(!replica.PresentationSnapshot()[0].Numbers.BottomRight.HasValue)throw new Exception("Native clock-only overflow recovery failed.");checks++;
        }
        File.WriteAllText(".utmp/Step3b/NativeOverflow.txt","PASS "+checks+" native overflow checks; no replacement state packet");
    }
    public static void IntegrityNative()
    {
        var original=SceneManager.GetActiveScene();bool dirty=original.isDirty;int checks=0;
        var docs=new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath,"BaseContent"),Path.Combine(Application.persistentDataPath,"Mods"),Array.Empty<string>()).ReadDocuments().ToArray();
        var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNPolicyContent()},docs);
        var preview=EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin();EveRegistrySession.Start(hub,EveSessionRole.Offline,EveContentCompatibility.Create(hub,docs,"test:step3b_integrity"));
            for(int scenario=0;scenario<3;scenario++)
            {
                var root=new GameObject("Owned integrity negative check "+scenario);root.SetActive(false);SceneManager.MoveGameObjectToScene(root,preview);
                root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(987100+scenario),EveId.Parse("project_n:dummy"),0);
                root.AddComponent<EveTimeScale>();var stat=root.AddComponent<PNPolicyStat>();stat.Popups=false;
                var loader=root.AddComponent<EveEntityLoader>();loader.Load(hub);root.SetActive(true);
                var manager=root.GetComponent<PNBuffManager>();manager.RenderPresentation=false;manager.Advance(0);
                using(var integrity=new PNPolicyIntegrity(manager,4,_=>"dot"))
                {
                    var source=PNBuffSource.Capture(stat);var handles=new PNBuffHandle[4];
                    for(int i=0;i<4;i++)handles[i]=manager.Buffs.Apply(new PNBuffApplyRequest(PNPolicyContent.BuffId("dot",i),source)).Handle;
                    manager.Advance(.5);integrity.Validate(handles);
                    if(integrity.Hits!=4 || integrity.Cues!=4)throw new Exception("Native DOT must produce four actual received hits and four tick cues.");checks++;
                    if(scenario==0)
                    {
                        integrity.ExpectStackDelta(1,1);manager.Buffs.ChangeStacks(handles[1],1);manager.Advance(.5);integrity.Validate(handles);
                        if(integrity.Hits!=8 || integrity.Cues!=8)throw new Exception("Scheduled stack change must retain exact hit cadence.");checks++;
                    }
                    else
                    {
                        if(scenario==1)manager.Buffs.ChangeStacks(handles[0],1);
                        else stat.ReceiveDamage(stat.CreateDamage(0,false,0,PNContentIds.True));
                        bool rejected=false;
                        try{integrity.Validate(handles);}catch(InvalidOperationException e){rejected=e.Message.Contains(scenario==1?"stack differs":"Actual DamageReceived");}
                        if(!rejected)throw new Exception("Native validator accepted an unexpected stack or extra combat hit.");checks++;
                    }
                }
                loader.Unload();UnityEngine.Object.DestroyImmediate(root);
            }
        }
        finally{try{if(EveRegistrySession.State!=EveSessionState.Stopped)EveRegistrySession.End();}finally{EditorSceneManager.ClosePreviewScene(preview);}}
        if(SceneManager.GetActiveScene()!=original || original.isDirty!=dirty)throw new Exception("Integrity native checks changed authored scene.");checks++;
        File.WriteAllText(".utmp/Step3b/NativeIntegrity.txt","PASS "+checks+" native real DamageReceived/clock/stack checks, including unexpected-stack and extra-hit rejection");
    }
}
#endif
