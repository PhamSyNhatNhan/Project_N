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
            File.WriteAllText(folder+"/Native.txt", PNBuffEditorChecks.Run()+"\n"+PNLifecycleRuntimeEditorChecks.Run()+"\n"+PNElementalBuffEditorChecks.Run()+"\n"+PNDamageFlowEditorChecks.Run());
            if(SceneManager.GetActiveScene()!=scene || scene.isDirty!=dirty) throw new Exception("Native checks changed authored scene state.");
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
}
#endif
