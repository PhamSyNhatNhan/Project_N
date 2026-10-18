using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Mirror;
namespace ProjectN.Coop {
    public sealed partial class PNNetworkManager {
        private readonly Dictionary<int,PNStateFence[]> peerFences=new Dictionary<int,PNStateFence[]>();
        private readonly Dictionary<(uint,byte),PNStateFence> clientFence=new Dictionary<(uint,byte),PNStateFence>();
        private uint fenceAttempt;
        private int fenceCount=-1;
        private string fenceDigest;
        private readonly Dictionary<string,Func<bool>> stateAdapters=new Dictionary<string,Func<bool>>();
        public readonly HashSet<string> RequiredAdapters=new HashSet<string>{StatKind};
        public void RegisterStateAdapter(string kind,Func<bool> settled)
        {if(string.IsNullOrEmpty(kind)||settled==null||stateAdapters.ContainsKey(kind))throw new InvalidOperationException("Required adapter registration.");stateAdapters.Add(kind,settled);}
        private bool AdaptersSettled()=>RequiredAdapters.All(x=>stateAdapters.TryGetValue(x,out var check)&&check())&&RequiredStateSettled();
        private bool SendFence(NetworkConnectionToClient conn,PNPeer peer)
        {
            string digest=BindingDigest(conn);
            if(!peerFences.TryGetValue(conn.connectionId,out var values)||values.Length==0||values[values.Length-1].Attempt!=peer.ResumeChallenge||values[values.Length-1].Digest!=digest)
            {
                var list=new List<PNStateFence>();
                foreach(var item in bindings.OrderBy(x=>x.Key))
                {
                    if(item.Value.Retiring||!RoomCatchupBinding(item.Value,conn))continue;
                    if(!item.Value.Shell.netIdentity.observers.ContainsKey(conn.connectionId))continue;
                    if(!CaptureStat(item.Value))return false;
                    var b=item.Value;
                    // Full state is reliably pushed before the fixed target; no per-frame ACK protocol.
                    if(b.Stats.Component!=null)
                    {
                        conn.Send(new PNStatNotice {NetId=item.Key,Session=b.Metadata.HostSession,Entity=b.Metadata.Entity,Generation=b.Metadata.Generation,State=b.Shell.State,Scope=Scope(b,conn)});
                        list.Add(new PNStateFence {Attempt=peer.ResumeChallenge,NetId=item.Key,Entity=b.Metadata.Entity,Generation=b.Metadata.Generation,Revision=b.Shell.State.Revision,Digest=digest,Scope=Scope(b,conn)});
                    }
                    if(b.Buffs.Component!=null)
                    {
                        if(!buffPeers.TryGetValue((conn.connectionId,item.Key),out var stream)||stream.Requested)return false;
                        if(!QueueBuffBaseline(stream,out long revision))return false;
                        list.Add(new PNStateFence{Attempt=peer.ResumeChallenge,NetId=item.Key,Adapter=1,Nonce=stream.Stream.Nonce,Entity=b.Metadata.Entity,Generation=b.Metadata.Generation,Revision=revision,Digest=digest,Scope=Scope(b,conn)});
                    }
                    if(b.Movement.Component!=null)
                    {
                        if(!movePeers.TryGetValue((conn.connectionId,item.Key),out var stream)||stream.NeedsBaseline)return false;
                        if(!QueueMoveBaseline(stream,out long revision))return false;
                        list.Add(new PNStateFence{Attempt=peer.ResumeChallenge,NetId=item.Key,Adapter=2,Nonce=stream.Stream.Nonce,Entity=b.Metadata.Entity,Generation=b.Metadata.Generation,Revision=revision,Digest=digest,Scope=Scope(b,conn)});
                    }
                }
                list.Add(new PNStateFence {Attempt=peer.ResumeChallenge,NetId=0,Revision=list.Count,Digest=digest});
                values=list.ToArray();peerFences[conn.connectionId]=values;
            }
            foreach(var value in values.Where(x=>x.NetId!=0&&x.Adapter==1))if(!buffPeers.TryGetValue((conn.connectionId,value.NetId),out var stream)||stream.SentRevision<value.Revision||stream.Packets.Any(x=>x.Kind==0))return false;
            foreach(var value in values.Where(x=>x.Adapter==2))if(!movePeers.TryGetValue((conn.connectionId,value.NetId),out var stream)||stream.SentRevision<value.Revision)return false;
            foreach(var value in values){var target=value;target.Session=EveRegistrySession.Context.SessionId;conn.Send(target);}
            return true;
        }
        private void ReceiveFence(PNStateFence value)
        {
            if(stopping||!IsPlaying||fatalSerialization)return;
            try
            {
                if(value.NetId!=0&&bindings.TryGetValue(value.NetId,out var scoped)&&!Scope(scoped).Equals(value.Scope))return;
                if(value.Attempt<clientResumeFloor||value.Attempt<fenceAttempt)return;
                if(value.Session!=EveRegistrySession.Context.Coop.AdmittedHostSession)return;
                if(value.Digest==null||value.Digest.Length!=64||value.Attempt==0)throw new FormatException("State fence header.");
                if(value.Attempt!=fenceAttempt||value.Digest!=fenceDigest){clientFence.Clear();fenceCount=-1;fenceAttempt=value.Attempt;fenceDigest=value.Digest;}
                if(value.NetId==0){if(value.Revision<0||value.Revision>Scheduler.MaximumResidents*3)throw new FormatException("State fence count.");fenceCount=(int)value.Revision;}
                else {var key=(value.NetId,value.Adapter);if(value.Adapter>2||value.Revision<(value.Adapter==1?0:1)||value.Entity<1||value.Generation<1||value.Adapter>0&&value.Nonce==0||!clientFence.ContainsKey(key)&&clientFence.Count>=Scheduler.MaximumResidents*3)throw new FormatException("State fence bounds.");if(clientFence.TryGetValue(key,out var existing)&&(existing.Revision!=value.Revision||existing.Nonce!=value.Nonce))throw new FormatException("Conflicting state target.");clientFence[key]=value;}
            }
            catch(Exception error){ProtocolFailure(error,false);}
        }
        private bool FenceSettled(uint attempt,string digest)
        {
            if(fenceAttempt!=attempt||fenceDigest!=digest||fenceCount!=clientFence.Count||fenceCount!=bindings.Values.Where(x=>!x.Retiring&&RoomCatchupBinding(x)).Sum(x=>(x.Candidate?.Loader?.GetComponent<PNStat>()!=null?1:0)+(x.Shell.HasBuff?1:0)+(x.Shell.HasMove?1:0)))return false;
            foreach(var item in clientFence)
            {
                if(!bindings.TryGetValue(item.Key.Item1,out var b)||b.Metadata.Entity!=item.Value.Entity||b.Metadata.Generation!=item.Value.Generation||!Scope(b).Equals(item.Value.Scope))return false;
                if(item.Key.Item2==2){if((ulong)b.Token!=item.Value.Nonce||b.Movement.Component?.MovementBaselineApplied!=true||b.Movement.Component.AppliedMovementControl<(ulong)item.Value.Revision||b.Movement.AwaitingBaseline)return false;continue;}
                if(item.Key.Item2==1){if((ulong)b.Token!=item.Value.Nonce||b.Buffs.Component?.HasBaseline!=true||b.Buffs.Component.AppliedRevision<item.Value.Revision||b.Buffs.Records.Count>0||b.Buffs.Assembly!=null)return false;continue;}
                var view=b.Candidate?.Loader?.GetComponent<PNStatReplication>()?.View;
                if(view==null||b.Candidate.Loader.GetComponent<PNStatReplication>().FullViewRevision<item.Value.Revision)return false;
            }
            return true;
        }
    }
}
