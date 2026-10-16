#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine.UIElements;
using EveEngine;
using EveEngine.Experimental.Lifecycle;
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
        var policy = new ModelPolicy(8,1048576,65536,1024,1024,128,1048576,1048576,32,8,3,5000,5000,5000,5000,20000,60000);
        var cleanup = new CleanupPolicy(24,24,16,2,32,1,2,1);
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
        var wire=new List<byte[]>();var ends=new List<int>();Guid hostId=Guid.Empty;
        try
        {
            Begin(EveSessionRole.Host);var session=EveRegistrySession.Context;hostId=session.SessionId;
            session.ConfigureSnapshots(8,new EveEntityPreparationBudget(4,32,2),()=>time);
            var hostActor=Factory();hostActor.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(881001),EveId.Parse("project_n:dummy"),0);
            hostActor.Load(hub);hostActor.gameObject.SetActive(true);
            var hostStat=hostActor.GetComponent<PNStat>();var part=hostActor.GetComponent<PNStatReplication>();double hostMax=hostStat.GetMax(PNContentIds.Health);
            using(var host=new EveActorHostConnection(session,1,policy,()=>time,compatibility))
            {
                host.Track(hostActor);host.CaptureSnapshot();
                void Record()
                {
                    int guard=2000;while(session.Snapshots.PendingJobs>0 && guard-->0)session.Snapshots.AdvanceFrame();
                    if(guard<=0)throw new Exception("Snapshot did not drain.");
                    while(host.Send(16,b=>{wire.AddRange(b);return true;}))host.Protocol.ReleaseAccepted((int)host.Protocol.AcceptedBytes);
                    ends.Add(wire.Count);
                }
                Record();host.Applied(new AppliedAck(1,host.Protocol.SnapshotId,host.Protocol.EndSequence));
                hostStat.SetCurrent(PNContentIds.Health,hostMax*.4);part.Advance(.05);Record();
                Check(ends[1]>ends[0],"actual actor stream emits changed Stat part");
                int retained=host.Protocol.BufferedMessages;hostStat.SetCurrent(PNContentIds.Health,hostMax*.3);part.Advance(.05);
                byte[][] refused=null;host.Send(16,b=>{refused=b.Select(x=>(byte[])x.Clone()).ToArray();return false;});
                hostStat.SetCurrent(PNContentIds.Health,hostMax*.2);part.Advance(.05);byte[][] retried=null;
                host.Send(16,b=>{retried=b.Select(x=>(byte[])x.Clone()).ToArray();return false;});
                Check(refused.Length==retried.Length && refused.Zip(retried,(a,b)=>a.SequenceEqual(b)).All(x=>x),"stalled transport retries identical pending bytes despite newer Stat");
                Check(host.Protocol.BufferedMessages>=retained+2,"required state queue pressure visible on stalled peer");
                // Saturation during a stalled send must enter bounded snapshot recovery.
                for(int i=0;i<policy.MaximumQueuedMessages+3 && host.Protocol.Phase==HostPhase.Live;i++)
                { hostStat.SetCurrent(PNContentIds.Health,hostMax*(.31+.01*(i%3)));part.Advance(.05); }
                Check(host.Protocol.Phase==HostPhase.WaitCapture && host.Protocol.BufferedMessages==0,"actual Stat queue saturation clears bounded backlog into recovery");
                hostStat.SetCurrent(PNContentIds.Health,hostMax*.2);part.Advance(.05);
                host.AdvanceFrame();Record();
                Check(host.Protocol.Phase==HostPhase.AwaitApplied,"recovery snapshot reaches Applied boundary");
                host.Applied(new AppliedAck(1,host.Protocol.SnapshotId,host.Protocol.EndSequence));
                hostStat.ReceiveDamage(new PNDamage(hostId,hostStat.EntityId,hostMax*10,new[]{PNContentIds.Physical}));
                Check(part.AuthoritativeState.Dead,"combat death captured before next sample");
                part.Advance(.05);Record();
            }
            Finish();
            Begin(EveSessionRole.Client);session=EveRegistrySession.Context;
            session.Coop.AdmitHost(hostId,compatibility,7);session.ConfigureCleanup(cleanup,()=>time,()=>10);
            using(var client=new EveActorClientConnection(session,hostId,1,policy,()=>time,m=>Factory(),l=>UnityEngine.Object.DestroyImmediate(l.gameObject),compatibility))
            {
                int cursor=0;
                void Replay(int end)
                {
                    while(cursor<end)client.Receive(wire[cursor++]);
                    int guard=2000;
                    while((client.Protocol.Phase==ClientPhase.Preparing || client.Protocol.NeedsPreparation().Count>0) && guard-->0)
                    { time+=.01;client.AdvanceFrame(new EveEntityPreparationBudget(4,32,2)); }
                    if(guard<=0 || client.Protocol.Phase==ClientPhase.Closed)throw new Exception("Client replay failed: "+client.LastError);
                }
                Replay(ends[0]);var actor=session.Entities.Snapshot().Single();var stat=actor.GetComponent<PNStat>();
                double initialized=stat.GetCurrent(PNContentIds.Health);
                Check(PNVitals.TryRead(stat,PNContentIds.Health,out var hp,out var max,out var dead) && hp==initialized && !dead,"native staged baseline Ready view");
                Replay(ends[1]);
                Check(PNVitals.TryRead(stat,PNContentIds.Health,out hp,out max,out dead) && Math.Abs(hp-max*.4)<1e-8 && stat.GetCurrent(PNContentIds.Health)==initialized,"native actor delta updates view without client PNStat mutation");
                Replay(ends[2]);actor=session.Entities.Snapshot().Single();stat=actor.GetComponent<PNStat>();
                Check(PNVitals.TryRead(stat,PNContentIds.Health,out hp,out max,out dead) && Math.Abs(hp-max*.2)<1e-8,"native recovery baseline converges latest state after stall");
                var playerBaseline=PNStatCodec.Encode(stat.GetComponent<PNStatReplication>().View);
                int died=0;stat.Died+=_=>died++;Replay(ends[3]);
                Check(PNVitals.TryRead(stat,PNContentIds.Health,out hp,out max,out dead) && dead && hp==0 && died==0,"native life delta no client gameplay Died");
                Check(session.LocalOwnerId==7 && session.ConnectionFailureCode==null,"admitted identity retained and actual client adapter remains active");
                var local=Factory(true);var remote=Factory(true);
                local.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(881011),EveId.Parse("project_n:dummy"),7);
                remote.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(881012),EveId.Parse("project_n:dummy"),8);
                local.Load(hub);remote.Load(hub);
                Check(!PNVitals.TryRead(local.GetComponent<PNStat>(),PNContentIds.Health,out _,out _,out _),"Ready actor without Stat baseline remains Unknown");
                foreach(var player in new[]{local,remote})
                {
                    var staged=player.GetComponent<PNStatReplication>().StageReplication(playerBaseline,hostId,player.Generation);
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
