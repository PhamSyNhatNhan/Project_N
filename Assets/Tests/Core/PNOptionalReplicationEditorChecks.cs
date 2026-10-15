#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using EveEngine.Experimental.Lifecycle;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PNOptionalProbe : MonoBehaviour, EveIEntityReplicationPart, EveIReplicationDeltaClassifier
{
    public EveId ReplicationId => EveId.Parse("test:optional_probe");
    public bool CanCaptureReplication => true;
    public event Action<byte[]> ReplicationDelta;
    public int Applies, Last, Mode;
    public byte[] CaptureReplication()=>new byte[]{0,0};
    private sealed class Stage : EveIReplicaStage { public void Commit(){} public void Published(){} public void Dispose(){} }
    public EveIReplicaStage StageReplication(byte[] p,Guid session,long generation)=>new Stage();
    public void ApplyReplicationDelta(byte[] p,Guid session,long generation){Applies++;Last=p[1];}
    public EveReplicationTraffic ClassifyReplicationDelta(byte[] p)
    {
        if(Mode==1)throw new InvalidOperationException("Injected classifier fault");
        if(Mode==2)return (EveReplicationTraffic)77;
        if(p==null || p.Length!=2 || p[0]>1)throw new FormatException("Probe payload");
        return p[0]==1?EveReplicationTraffic.Optional:EveReplicationTraffic.Required;
    }
    public void Emit(byte[] p)=>ReplicationDelta?.Invoke(p);
}

public static class PNOptionalReplicationEditorChecks
{
    public static string Run(bool injectPresenterFault = true)
    {
        if(Application.isPlaying || EveRegistrySession.State!=EveSessionState.Stopped)throw new InvalidOperationException("Stopped Edit required");
        var original=SceneManager.GetActiveScene();bool dirty=original.isDirty;var preview=EditorSceneManager.NewPreviewScene();
        var roots=new List<GameObject>();var lines=new List<string>();double time=0;Guid hostId=Guid.Empty;bool originalCountOnly=EveHotPathMetrics.CountOnly;
        var docs=new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath,"BaseContent"),Path.Combine(Application.persistentDataPath,"Mods"),Array.Empty<string>()).ReadDocuments().ToArray();
        var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()},docs);
        var compatibility=EveContentCompatibility.Create(hub,docs,"test:optional_native");
        var policy=new ModelPolicy(8,1048576,65536,1024,1024,128,1048576,1048576,32,8,3,5000,5000,5000,5000,20000,60000);
        var optional=new OptionalReplicationPolicy(1,4096,8,8192,8192);
        void Check(bool ok,string message){if(!ok)throw new Exception(message);lines.Add("PASS "+message);}
        EveEntityLoader Factory() {
            var root=new GameObject("Disposable optional actor");root.SetActive(false);roots.Add(root);SceneManager.MoveGameObjectToScene(root,preview);
            root.AddComponent<EveEntityIdentity>();root.AddComponent<CapsuleCollider>();root.AddComponent<EveTimeScale>();root.AddComponent<PNEnemyStat>();
            root.AddComponent<PNBuffManager>().RenderPresentation=false;root.AddComponent<PNOptionalProbe>();return root.AddComponent<EveEntityLoader>();
        }
        void Begin(EveSessionRole role){EveRegistrySession.Begin();EveRegistrySession.Start(hub,role,compatibility);}
        void Finish(){if(EveRegistrySession.State!=EveSessionState.Stopped)EveRegistrySession.End();for(int i=0;i<2000 && EveRegistrySession.State==EveSessionState.Stopping;i++)EveRegistrySession.PumpCleanup();if(EveRegistrySession.State!=EveSessionState.Stopped)throw new Exception("Cleanup timeout");}
        var wire=new List<byte[]>();var earlyWire=new List<byte[]>();int earlyBaseline=0;var ends=new List<int>();long actorId=991001;int legacyCount=0;
        try {
            Begin(EveSessionRole.Host);var session=EveRegistrySession.Context;hostId=session.SessionId;session.ConfigureSnapshots(8,new EveEntityPreparationBudget(4,32,2),()=>time);
            var actor=Factory();actor.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(actorId),EveId.Parse("project_n:dummy"),0);actor.Load(hub);actor.gameObject.SetActive(true);
            var probe=actor.GetComponent<PNOptionalProbe>();probe.ReplicationDelta+=_=>legacyCount++;
            var buff=actor.GetComponent<PNBuffManager>();buff.Advance(0);
            buff.Buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:burn"),PNBuffSource.Capture(actor.GetComponent<PNStat>())));
            using(var host=new EveActorHostConnection(session,1,policy,()=>time,compatibility,optional)) {
                host.Track(actor);EveHotPathMetrics.CountOnly=true;EveHotPathMetrics.Drain();probe.Emit(new byte[]{1,9});
                Check(host.Protocol.OptionalDroppedNotLive==1 && host.Protocol.OptionalQueuedMessages==0,"phase gate before snapshot");
                Check(!EveHotPathMetrics.Drain().ContainsKey("wire.actor.encoded-bytes"),"early refusal avoids actor delta encoding");EveHotPathMetrics.CountOnly=originalCountOnly;
                void Record(){int guard=2000;while(session.Snapshots.PendingJobs>0 && guard-->0)session.Snapshots.AdvanceFrame();if(guard<=0)throw new Exception("Capture timeout");while(host.Send(16,b=>{wire.AddRange(b);return true;}))host.Protocol.ReleaseAccepted((int)host.Protocol.AcceptedBytes);ends.Add(wire.Count);}
                host.CaptureSnapshot();Record();host.Applied(new AppliedAck(1,host.Protocol.SnapshotId,host.Protocol.EndSequence));
                var reused=new byte[]{1,10};probe.Emit(reused);reused[1]=11;probe.Emit(reused);
                reused[0]=0;reused[1]=12;probe.Emit(reused);
                Check(host.Protocol.OptionalAdmitted==1 && host.Protocol.OptionalDroppedBudget==1,"per-peer optional budget while required survives");
                byte[][] pending=null,retry=null;
                host.Send(16,b=>{pending=b.Select(x=>(byte[])x.Clone()).ToArray();return false;});
                probe.Emit(new byte[]{1,13});host.Send(16,b=>{retry=b.Select(x=>(byte[])x.Clone()).ToArray();return false;});
                Check(pending.Length==retry.Length && pending.Zip(retry,(a,b)=>a.SequenceEqual(b)).All(x=>x),"actual adapter pending retry identical");
                Record();Check(host.Protocol.OptionalQueuedBytes==0,"accept frees optional queue occupancy");
                buff.Advance(1);Record();
                Check(host.Protocol.OptionalOffered>=4 && host.Protocol.OptionalAdmitted>=2,"real Buff tick uses optional classification");
                // Occupancy belongs to connection, survives actor unsubscribe until actual acceptance.
                probe.Emit(new byte[]{1,14});long occupied=host.Protocol.OptionalQueuedBytes;host.Despawn(actorId);
                probe.Emit(new byte[]{1,15});Check(occupied>0 && host.Protocol.OptionalQueuedBytes==occupied,"Despawn does not release queued optional budget");Record();
                Check(legacyCount==7,"legacy subscriber retains all emitted probe deltas");
                // A fresh host connection exercises explicit contract errors, including invalid enum.
                for(int mode=1;mode<=2;mode++)using(var bad=new EveActorHostConnection(session,mode+1,policy,()=>time,compatibility,optional)) {
                    bad.Track(actor);probe.Mode=mode;probe.Emit(new byte[]{1,16});
                    Check(bad.ClassifierErrors==1 && bad.LastReplicationError!=null && bad.Protocol.Phase==HostPhase.Closed,"classifier contract error visible close "+mode);probe.Mode=0;
                }
                using(var early=new EveActorHostConnection(session,4,policy,()=>time,compatibility,optional)) {
                    early.CaptureSnapshot();while(session.Snapshots.PendingJobs>0)session.Snapshots.AdvanceFrame();
                    while(early.Send(16,b=>{earlyWire.AddRange(b);return true;}))early.Protocol.ReleaseAccepted((int)early.Protocol.AcceptedBytes);
                    earlyBaseline=earlyWire.Count;early.Applied(new AppliedAck(4,early.Protocol.SnapshotId,early.Protocol.EndSequence));
                    early.Track(actor);probe.Emit(new byte[]{1,20});early.Despawn(actorId);
                    while(early.Send(16,b=>{earlyWire.AddRange(b);return true;}))early.Protocol.ReleaseAccepted((int)early.Protocol.AcceptedBytes);
                }
            }
            Finish();Begin(EveSessionRole.Client);session=EveRegistrySession.Context;session.Coop.AdmitHost(hostId,compatibility);
            session.ConfigureCleanup(new CleanupPolicy(24,24,16,2,32,1,2,1),()=>time,()=>10);
            using(var client=new EveActorClientConnection(session,hostId,1,policy,()=>time,_=>Factory(),l=>UnityEngine.Object.DestroyImmediate(l.gameObject),compatibility)) {
                int cursor=0;
                void Replay(int end){while(cursor<end)client.Receive(wire[cursor++]);for(int i=0;i<2000 && (client.Protocol.Phase==ClientPhase.Preparing || client.Protocol.NeedsPreparation().Count>0);i++){time+=.01;client.AdvanceFrame(new EveEntityPreparationBudget(4,32,2));}Check(client.Protocol.Phase!=ClientPhase.Closed && client.Protocol.Phase!=ClientPhase.Recovering,"actual client no gap/protocol failure");}
                Replay(ends[0]);var replicaActor=session.Entities.Snapshot().Single();var received=replicaActor.GetComponent<PNOptionalProbe>();
                Replay(ends[1]);Check(received.Applies==2 && received.Last==12,"reused buffer: optional first bytes and later required captured independently");
                var replica=replicaActor.GetComponent<PNBuffManager>().Replica;int cues=0;replica.CuePresented+=_=>{cues++;if(injectPresenterFault)throw new Exception("Injected presenter observer fault");};
                Replay(ends[2]);Check(replica.Snapshot().Count==1 && cues>0 && session.ConnectionFailureCode==null,injectPresenterFault?"real Buff cue loss preserves state and presenter failure does not close":"real Buff cue loss preserves state (fault injection verified separately in clone)");
                Replay(ends[3]);Check(client.Protocol.Phase==ClientPhase.Live,"cue-before-Despawn stream remains valid");
                long sequence=ModelCodec.Decode(wire[wire.Count-1],policy).Sequence;
                client.Receive(ModelCodec.Encode(new Envelope(hostId,1,sequence+1,MessageKind.Updated,new byte[]{0},new PresenceKey(actorId,1))));
                Check(client.Protocol.Phase==ClientPhase.Recovering,"invalid Updated-after-Despawn still recovers");
            }
            Finish();Begin(EveSessionRole.Client);session=EveRegistrySession.Context;session.Coop.AdmitHost(hostId,compatibility);
            session.ConfigureCleanup(new CleanupPolicy(24,24,16,2,32,1,2,1),()=>time,()=>10);
            using(var client=new EveActorClientConnection(session,hostId,4,policy,()=>time,_=>Factory(),l=>UnityEngine.Object.DestroyImmediate(l.gameObject),compatibility)) {
                for(int i=0;i<earlyBaseline;i++)client.Receive(earlyWire[i]);
                for(int i=0;i<100 && client.Protocol.Phase!=ClientPhase.Live;i++)client.AdvanceFrame(new EveEntityPreparationBudget(4,32,2));
                Check(client.Protocol.Phase==ClientPhase.Live,"empty baseline published before slow-load actor");
                for(int i=earlyBaseline;i<earlyWire.Count;i++)client.Receive(earlyWire[i]);
                client.AdvanceFrame(new EveEntityPreparationBudget(4,32,2));
                Check(client.Protocol.Phase==ClientPhase.Live && client.Protocol.BufferedMessages==0 && session.Entities.Snapshot().Count==0 && session.ConnectionFailureCode==null,"cue queue canceled by Despawn before Ready");
            }
            Finish();Check(SceneManager.GetActiveScene()==original && original.isDirty==dirty,"authored scene preserved");
            return "PASS "+lines.Count+" native optional admission checks\n"+string.Join("\n",lines);
        } finally {EveHotPathMetrics.CountOnly=originalCountOnly;Finish();foreach(var root in roots)if(root!=null)UnityEngine.Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(preview);}
    }
}
#endif
