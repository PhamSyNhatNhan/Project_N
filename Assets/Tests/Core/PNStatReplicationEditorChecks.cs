#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine.UIElements;
using EveEngine;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PNStatReplicationEditorChecks
{
    public static string Run()
    {
        if (Application.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Stopped Edit required.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var preview = EditorSceneManager.NewPreviewScene(); var roots = new List<GameObject>(); int checks = 0;
        var lines = new List<string>(); double time = 0;
        var docs = new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath,"BaseContent"),
            Path.Combine(Application.persistentDataPath,"Mods"),Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()},docs);
        var compatibility = EveContentCompatibility.Create(hub,docs,"test:stat1_native");
        void Check(bool ok,string message) { if(!ok)throw new Exception(message); checks++;lines.Add("PASS "+message); }
        EveEntityLoader Factory(bool player = false)
        {
            var root = new GameObject("Disposable Stat1 actor"); roots.Add(root);root.SetActive(false);SceneManager.MoveGameObjectToScene(root,preview);
            root.AddComponent<EveEntityIdentity>();root.AddComponent<CapsuleCollider>();root.AddComponent<EveTimeScale>();
            if(player)root.AddComponent<PNPlayerStat>();else root.AddComponent<PNEnemyStat>();root.AddComponent<PNBuffManager>().RenderPresentation=false;
            return root.AddComponent<EveEntityLoader>();
        }
        void Begin(EveSessionRole role) { EveRegistrySession.Begin();EveRegistrySession.Start(hub,role,compatibility); }
        void Finish()
        {
            if(EveRegistrySession.State!=EveSessionState.Stopped)EveRegistrySession.End();
            for(int i=0;i<2000 && EveRegistrySession.State==EveSessionState.Stopping;i++)EveRegistrySession.PumpCleanup();
            if(EveRegistrySession.State!=EveSessionState.Stopped)throw new Exception("Cleanup did not finish.");
        }
        var frames=new List<PNStatFrame>();Guid hostId=Guid.Empty;long generation=0;
        try
        {
            Begin(EveSessionRole.Host);var session=EveRegistrySession.Context;hostId=session.SessionId;
            var hostActor=Factory();hostActor.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(881001),EveId.Parse("project_n:dummy"),0);
            hostActor.Load(hub);hostActor.gameObject.SetActive(true);generation=hostActor.Generation;
            var hostStat=hostActor.GetComponent<PNStat>();var part=hostActor.GetComponent<PNStatReplication>();double hostMax=hostStat.GetMax(PNContentIds.Health);
            frames.Add(part.CaptureState());
            hostStat.SetCurrent(PNContentIds.Health,hostMax*.4);part.Advance(.05);frames.Add(part.AuthoritativeState);
            Check(frames[1].Revision>frames[0].Revision && Math.Abs(frames[1].Resources[PNContentIds.Health].Current-hostMax*.4)<1e-8,"actual host actor samples the changed Stat part into a newer frame");
            hostStat.SetCurrent(PNContentIds.Health,hostMax*.2);part.Advance(.05);frames.Add(part.AuthoritativeState);
            Check(frames[2].Revision>frames[1].Revision,"latest host frame supersedes earlier frames");
            hostStat.ReceiveDamage(new PNDamage(hostId,hostStat.EntityId,hostMax*10,new[]{PNContentIds.Physical}));
            Check(part.AuthoritativeState.Dead,"combat death captured before next sample");
            part.Advance(.05);frames.Add(part.AuthoritativeState);
            Finish();
            Begin(EveSessionRole.Client);session=EveRegistrySession.Context;
            session.Admission.AdmitHost(hostId,compatibility,7);
            {
                var actorLoader=Factory();actorLoader.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(881001),EveId.Parse("project_n:dummy"),0,generation);
                actorLoader.Prepare(hub);
                var baselineStage=actorLoader.GetComponent<PNStatReplication>().StageState(frames[0],hostId,generation);
                EveEntityLoader.CommitBatch(new[]{actorLoader},()=>baselineStage.Commit());baselineStage.Published();baselineStage.Dispose();actorLoader.gameObject.SetActive(true);
                var actor=actorLoader;var stat=actor.GetComponent<PNStat>();var clientPart=actor.GetComponent<PNStatReplication>();
                double initialized=stat.GetCurrent(PNContentIds.Health);
                Check(PNVitals.TryRead(stat,PNContentIds.Health,out var hp,out var max,out var dead) && hp==initialized && !dead,"native staged baseline Ready view");
                clientPart.ApplyState(frames[1],hostId,generation);
                Check(PNVitals.TryRead(stat,PNContentIds.Health,out hp,out max,out dead) && Math.Abs(hp-max*.4)<1e-8 && stat.GetCurrent(PNContentIds.Health)==initialized,"native actor update changes the view without client PNStat mutation");
                clientPart.ApplyState(frames[2],hostId,generation);
                Check(PNVitals.TryRead(stat,PNContentIds.Health,out hp,out max,out dead) && Math.Abs(hp-max*.2)<1e-8,"native latest frame converges the view");
                var playerBaseline=clientPart.View;
                int died=0;stat.Died+=_=>died++;clientPart.ApplyState(frames[3],hostId,generation);
                Check(PNVitals.TryRead(stat,PNContentIds.Health,out hp,out max,out dead) && dead && hp==0 && died==0,"native life update no client gameplay Died");
                Check(session.LocalOwnerId==7 && session.ConnectionFailureCode==null,"admitted identity retained and client session remains active");
                var local=Factory(true);var remote=Factory(true);
                local.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(881011),EveId.Parse("project_n:dummy"),7);
                remote.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(881012),EveId.Parse("project_n:dummy"),8);
                local.Load(hub);remote.Load(hub);
                Check(!PNVitals.TryRead(local.GetComponent<PNStat>(),PNContentIds.Health,out _,out _,out _),"Ready actor without Stat baseline remains Unknown");
                foreach(var player in new[]{local,remote})
                {
                    var staged=player.GetComponent<PNStatReplication>().StageState(playerBaseline,hostId,player.Generation);
                    staged.Commit();staged.Published();staged.Dispose();player.gameObject.SetActive(true);
                }
                var uiObject=new GameObject("Disposable Stat UI");roots.Add(uiObject);SceneManager.MoveGameObjectToScene(uiObject,preview);
                var ui=uiObject.AddComponent<PNUiRuntime>(); ui.Bind(EveRegistrySession.Context);
                if(ui.HudRoot==null)typeof(PNUiRuntime).GetMethod("Build",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(ui,null);
                ui.RefreshUI();
                Check(ui.HudPlayer==local.GetComponent<PNStat>() && Math.Abs(ui.HudRoot.Q<ProgressBar>("player-hp").value-20)<.01,"native HUD selects admitted owner and reads replicated HP");
                var views=(System.Collections.IDictionary)typeof(PNUiRuntime).GetField("worlds",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(ui);
                bool localGrid=false,remoteVitals=false;
                foreach(System.Collections.DictionaryEntry item in views)
                {
                    var row=item.Value;var rowStat=(PNStat)row.GetType().GetField("stat",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(row);
                    var rowRoot=(VisualElement)row.GetType().GetField("root",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(row);
                    if(rowStat==local.GetComponent<PNStat>())localGrid=rowRoot.Q("ability-grid").style.display.value==DisplayStyle.Flex;
                    if(rowStat==remote.GetComponent<PNStat>())remoteVitals=rowRoot.Q("enemy-vitals").style.display.value==DisplayStyle.Flex && rowRoot.Q("ability-grid").style.display.value==DisplayStyle.None;
                }
                Check(localGrid && remoteVitals,"native teammate vitals visible and Ability grid owner-only");
                ui.Unbind(); UnityEngine.Object.DestroyImmediate(uiObject);

            }
            Finish();
            Check(SceneManager.GetActiveScene()==original && original.isDirty==dirty,"original scene/dirty preserved");
        }
        finally
        {
            try{Finish();}finally{foreach(var root in roots)if(root!=null)UnityEngine.Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(preview);}
            Directory.CreateDirectory(".utmp/Stat1");File.WriteAllLines(".utmp/Stat1/NativeChecks.txt",lines);
        }
        return "PASS "+checks+" native Stat1 actual-adapter record/replay checks";
    }
}
#endif
