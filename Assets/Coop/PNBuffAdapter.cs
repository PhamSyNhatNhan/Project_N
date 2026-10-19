using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using EveEngine;
using Mirror;
using UnityEngine;

namespace ProjectN.Coop
{
    public sealed partial class PNNetworkManager
    {
        private sealed class BuffBindingState
        {
            public PNBuffManager Component;
            public Action<PNBuffChange> Changed;
            public Action<PNBuffOptionalCueMessage,double> Cue;
            public Action<Exception> Fault;
            public PNBuffBaselineAssembly Assembly;
            public readonly Queue<PNBuffBaselineAssembly> Records=new Queue<PNBuffBaselineAssembly>();
            public readonly List<IDisposable> Retained=new List<IDisposable>();
            public int RecordBytes;
            public ulong LastTransfer;
            public bool Prepared;
            public EveIReplicaStage Stage;
            public PNBuffWire.FrameDecoder Decoder;
            public IDisposable Job;
            public ulong DroppedOptionalTransfer;
            public readonly List<EveIReplicaStage> Stages=new List<EveIReplicaStage>();
        }
        private const string BuffKind="project_n:buff";
        private sealed class BuffPeer
        {
            public NetworkConnectionToClient Connection;public Binding Binding;public PNBuffStream Stream;
            public readonly Queue<PNBuffPacket> Packets=new Queue<PNBuffPacket>();public int Bytes;public bool Requested=true;
            public int OptionalCount,OptionalBytes;public int OptionalFrame=-1;public long SentRevision=-1;
            public float ClockRate=float.NaN;
        }
        private readonly Dictionary<(int,uint),BuffPeer> buffPeers=new Dictionary<(int,uint),BuffPeer>();
        private ulong buffTransfer;
        private int pendingBuffBytes,pendingBuffEvents,hostBuffBytes;
        public int StaleBuffRequests{get;private set;}
        private int buffPeerCursor;
        public long DroppedOptionalBuffCues {get;private set;}
        public int PendingBuffBytes=>pendingBuffBytes-pendingMoveBytes-pendingWorldBytes;
        public int PendingBuffEvents=>pendingBuffEvents-pendingMoveEvents;
        private void InitializeBuff()
        {
            RequiredAdapters.Add(BuffKind);
            RegisterStateAdapter(BuffKind,()=>bindings.Values.Where(b=>!b.Retiring&&RoomCatchupBinding(b)).All(b=>b.Candidate?.Loader!=null&&b.Candidate.Loader.IsReady&&(!b.Shell.HasBuff||subscribedSession.HasAuthority||b.Buffs.Component?.HasBaseline==true)));
            if(NetworkServer.active)NetworkServer.ReplaceHandler<PNBuffBaselineRequest>(ReceiveBuffRequest);
            if(NetworkClient.active)NetworkClient.ReplaceHandler<PNBuffPacket>(ReceiveBuffPacket);
            if(Transport.active!=null&&NetworkMessages.MaxContentSize(Channels.Reliable)<PNBuffWire.ChunkBytes+160)throw new InvalidOperationException("Configured transport cannot carry Buff fragments.");
        }
        private PNBuffStream Stream(Binding b)=>new PNBuffStream{Session=b.Metadata.HostSession,NetId=b.Shell.netId,Entity=b.Metadata.Entity,Generation=b.Metadata.Generation,Nonce=(ulong)b.Token,Scope=Scope(b)};
        private void HookBuff(Binding b)
        {
            b.Buffs.Component=b.Candidate.Loader.GetComponent<PNBuffManager>();if(b.Buffs.Component==null)return;b.Buffs.Component.UseTypedTransport();
            b.Buffs.Changed=change=>
            {
                if(stopping||!IsPlaying||fatalSerialization||b.Shell==null)return;
                var peers=buffPeers.Values.Where(x=>ReferenceEquals(x.Binding,b)&&!x.Requested).ToArray();if(peers.Length==0)return;
                byte[] body=PNBuffWire.Encode(change);foreach(var peer in peers)EnqueueBuff(peer,1,change.State.Sequence,change.PresentationTime,body);
            };
            b.Buffs.Cue=(cue,time)=>
            {
                if(stopping||!IsPlaying||fatalSerialization)return;
                byte[] body=null;
                foreach(var peer in buffPeers.Values.Where(x=>ReferenceEquals(x.Binding,b)&&!x.Requested).ToArray())
                {
                    if(peer.OptionalFrame!=Time.frameCount){peer.OptionalFrame=Time.frameCount;peer.OptionalCount=peer.OptionalBytes=0;}
                    if(peer.OptionalCount>=32||peer.OptionalBytes>=16384)continue;body=body??PNBuffWire.Encode(cue);
                    if(peer.OptionalBytes+body.Length>16384||peer.Bytes+body.Length+movePeers.Values.Where(x=>x.Connection==peer.Connection).Sum(x=>(long)x.Bytes)>8*1024*1024||(long)hostBuffBytes+hostMoveBytes+body.Length>32*1024*1024)continue;
                    peer.OptionalCount++;peer.OptionalBytes+=body.Length;EnqueueBuff(peer,2,b.Buffs.Component.AppliedRevision,time,body);
                }
            };
            b.Buffs.Fault=error=>WorldFailure(b.Metadata.World,new InvalidOperationException("Buff projection actor "+b.Metadata.Entity,error));
            b.Buffs.Component.StateChanged+=b.Buffs.Changed;b.Buffs.Component.OptionalCuePublished+=b.Buffs.Cue;b.Buffs.Component.ProjectionFailed+=b.Buffs.Fault;
        }
        private void UnhookBuff(Binding b)
        {
            DetachBuffPublication(b);
            b.Buffs.Job?.Dispose();b.Buffs.Job=null;b.Buffs.Decoder=null;
            b.Buffs.Assembly?.Dispose();b.Buffs.Assembly=null;while(b.Buffs.Records.Count>0)b.Buffs.Records.Dequeue().Dispose();foreach(var lease in b.Buffs.Retained)lease.Dispose();b.Buffs.Retained.Clear();
            foreach(var peer in buffPeers.Values.Where(x=>ReferenceEquals(x.Binding,b)).ToArray()){RememberRetiredBuff(peer);RemoveBuffPeer(peer);}
            b.Buffs.Component=null;
        }
        private void DetachBuffPublication(Binding b){if(b.Buffs.Component!=null){b.Buffs.Component.StateChanged-=b.Buffs.Changed;b.Buffs.Component.OptionalCuePublished-=b.Buffs.Cue;b.Buffs.Component.ProjectionFailed-=b.Buffs.Fault;}}
        private void RemoveBuffPeer(BuffPeer peer){hostBuffBytes-=peer.Bytes;peer.Bytes=0;peer.Packets.Clear();buffPeers.Remove((peer.Connection.connectionId,peer.Stream.NetId));}
        private void ReceiveBuffRequest(NetworkConnectionToClient conn,PNBuffBaselineRequest request)
        {
            if(stopping||!IsPlaying||fatalSerialization)return;
            try
            {
                var s=request.Stream;s.Validate();if(IsRetiredBuff(conn,s))return;
                // A request can arrive after its entity's binding was removed (owner replacement/transfer, latency). That is stale, not hostile: drop it without disconnecting the peer.
                if(conn.authenticationData is PNPeer&&!(conn is LocalConnectionToClient)&&!bindings.ContainsKey(s.NetId)){StaleBuffRequests++;Debug.LogWarning("Ignored stale Buff baseline request for unbound netId "+s.NetId+" from connection "+conn.connectionId);return;}
                if(!(conn.authenticationData is PNPeer)||conn is LocalConnectionToClient||!bindings.TryGetValue(s.NetId,out var b)||b.Buffs.Component==null||!b.Shell.netIdentity.observers.ContainsKey(conn.connectionId)||s.Session!=b.Metadata.HostSession||s.Entity!=b.Metadata.Entity||s.Generation!=b.Metadata.Generation||!AuthorizeWorldRequest(conn,b,s.Scope))throw new FormatException("Unauthorized Buff baseline request.");
                if(buffPeers.TryGetValue((conn.connectionId,s.NetId),out var old))
                {if(old.Stream.Equals(s))return;if(s.Nonce<=old.Stream.Nonce)throw new FormatException("Stale Buff observer nonce.");RemoveBuffPeer(old);}
                buffPeers.Add((conn.connectionId,s.NetId),new BuffPeer{Connection=conn,Binding=b,Stream=s});
            }
            catch(Exception error){LastFailure=error.Message;conn.Disconnect();}
        }
        private bool QueueBuffBaseline(BuffPeer peer,out long revision)
        {
            revision=0;var status=peer.Binding.Buffs.Component.TryCaptureState(out var frame);if(status==PNBuffCaptureStatus.Busy)return false;if(status==PNBuffCaptureStatus.Faulted)throw new InvalidOperationException("Buff baseline capture fault.");
            EnqueueBuff(peer,0,frame.Revision,frame.PresentationTime,PNBuffWire.Encode(frame));peer.Requested=false;revision=frame.Revision;return true;
        }
        private void EnqueueBuff(BuffPeer peer,byte kind,long revision,double time,byte[] bytes)
        {
            int count=(bytes.Length+PNBuffWire.ChunkBytes-1)/PNBuffWire.ChunkBytes;int charged=checked(bytes.Length+count*160);
            if(peer.Bytes+charged+movePeers.Values.Where(x=>x.Connection==peer.Connection).Sum(x=>(long)x.Bytes)>8*1024*1024||(long)hostBuffBytes+hostMoveBytes+charged>32*1024*1024){peer.Connection.Disconnect();RemoveBuffPeer(peer);return;}
            ulong transfer=checked(++buffTransfer);
            for(int i=0;i<count;i++)
            {
                int size=Math.Min(PNBuffWire.ChunkBytes,bytes.Length-i*PNBuffWire.ChunkBytes);var body=new byte[size];Buffer.BlockCopy(bytes,i*PNBuffWire.ChunkBytes,body,0,size);
                peer.Packets.Enqueue(new PNBuffPacket{Stream=peer.Stream,Transfer=transfer,Kind=kind,Revision=revision,Time=time,HostTime=NetworkTime.time,Rate=BuffClockRate(peer.Binding),Total=bytes.Length,Index=i,Count=count,Body=body});
            }
            peer.Bytes+=charged;hostBuffBytes+=charged;
        }
        private IDisposable ReserveBuff(int size)
            => ReserveBuffCore(size,true);
        private IDisposable ReserveBuffStructure(int size)
            => ReserveBuffCore(size,false);
        private IDisposable ReserveBuffCore(int size,bool countEvent)
        {
            if(size<0||(long)pendingBuffBytes+inbox.TotalBytes+size>Profile.PendingBytes||countEvent&&(long)pendingBuffEvents+inbox.TotalEvents>=Profile.PendingEvents)throw new InvalidOperationException("Global pending Stat/Buff byte/event bound.");
            pendingBuffBytes+=size;if(countEvent)pendingBuffEvents++;UpdateInboxBudget();
            return new PNBuffRelease(()=>{pendingBuffBytes-=size;if(countEvent)pendingBuffEvents--;UpdateInboxBudget();});
        }
        private void UpdateInboxBudget(){inbox.MaximumTotalBytes=Profile.PendingBytes-pendingBuffBytes;inbox.MaximumTotalEvents=Profile.PendingEvents-pendingBuffEvents;}
        private void ReceiveBuffPacket(PNBuffPacket p)
        {
            if(stopping||!IsPlaying||fatalSerialization||NetworkServer.active)return;
            try
            {
                if(!bindings.TryGetValue(p.Stream.NetId,out var b)||!IsClientBindingCurrent(b)||!p.Stream.Equals(Stream(b)))return;
                if(!b.Shell.HasBuff)throw new FormatException("Unexpected Buff stream.");
                if(p.Kind==2&&p.Transfer==b.Buffs.DroppedOptionalTransfer)return;
                if(b.Buffs.Assembly==null)
                {
                    if(p.Transfer<=b.Buffs.LastTransfer)throw new FormatException("Reused Buff transfer.");
                    try{b.Buffs.Assembly=new PNBuffBaselineAssembly(p,Time.realtimeSinceStartupAsDouble,ReserveBuff,ReserveBuffStructure);}
                    catch(InvalidOperationException) when(p.Kind==2){b.Buffs.DroppedOptionalTransfer=p.Transfer;b.Buffs.LastTransfer=p.Transfer;DroppedOptionalBuffCues++;return;}
                }
                else b.Buffs.Assembly.Add(p);
                if(!b.Buffs.Assembly.Complete)return;
                int retained=b.Buffs.Records.Where(x=>x.Header.Kind==1).Sum(x=>x.Header.Total)+b.Buffs.Retained.OfType<PNBuffBaselineAssembly>().Where(x=>x.Header.Kind==1).Sum(x=>x.Header.Total);
                if(b.Buffs.Records.Count+b.Buffs.Retained.Count>=256||retained+p.Total>256*1024&&p.Kind==1)
                {if(p.Kind==2){b.Buffs.Assembly.Dispose();b.Buffs.Assembly=null;b.Buffs.LastTransfer=p.Transfer;DroppedOptionalBuffCues++;return;}throw new FormatException("Pending Buff required event bound.");}
                b.Buffs.LastTransfer=p.Transfer;b.Buffs.Records.Enqueue(b.Buffs.Assembly);b.Buffs.RecordBytes+=p.Total;b.Buffs.Assembly=null;
            }
            catch(Exception error){ProtocolFailure(error,false);}
        }
        private bool PrepareBuff(Binding b,EveEntityLoader loader)
        {
            if(b.Buffs.Component==null)b.Buffs.Component=loader.GetComponent<PNBuffManager>();
            if((b.Buffs.Component!=null)!=b.Shell.HasBuff)throw new FormatException("Buff component descriptor mismatch.");
            if(b.Buffs.Component==null)return true;
            if(b.Buffs.Assembly!=null&&Time.realtimeSinceStartupAsDouble-b.Buffs.Assembly.Started>Scheduler.TimeoutMilliseconds/1000d)throw new TimeoutException("Buff assembly deadline.");
            if(b.Buffs.Records.Count==0)return b.Buffs.Prepared;
            var record=b.Buffs.Records.Peek();var p=record.Header;
            if(p.Kind==0)
            {
                if(b.Buffs.Decoder==null)b.Buffs.Decoder=new PNBuffWire.FrameDecoder(record.Body(),p,record.ReserveNodes);
                if(!b.Buffs.Decoder.Advance())return false;
                var frame=b.Buffs.Decoder.Result;b.Buffs.Decoder=null;var stage=b.Buffs.Component.StageState(frame,p.Stream.Session,p.Stream.Generation);if(!b.Buffs.Stages.Contains(stage))b.Buffs.Stages.Add(stage);b.Buffs.Stage=stage;b.Buffs.Prepared=true;
                if(loader.IsReady){stage.Commit();stage.Published();stage.Dispose();b.Buffs.Stages.Remove(stage);b.Buffs.Stage=null;}
            }
            else if(p.Kind==1)
            {
                if(!b.Buffs.Prepared)throw new FormatException("Buff delta before baseline.");
                b.Buffs.Component.ApplyChange(PNBuffWire.DecodeChange(record.Body(),p,record.ReserveNodes),p.Stream.Session,p.Stream.Generation);
            }
            else if(loader.IsReady&&b.Buffs.Component.HasBaseline&&b.Buffs.Component.AppliedRevision>=p.Revision)
            {try{b.Buffs.Component.ApplyOptionalCue(PNBuffWire.DecodeCue(record.Body(),p,record.ReserveNodes),p.Time);}catch(Exception error) when(error is FormatException||error is IOException||error is InvalidOperationException){DroppedOptionalBuffCues++;}}
            if(p.Kind!=2)
            {
                double anchor=p.Time,host=p.HostTime,rate=p.Rate;
                b.Buffs.Component.AuthoritativePresentationTime=()=>{double elapsed=clientInterrupted>=0?Math.Max(0,Time.realtimeSinceStartupAsDouble-clientInterrupted):0;return anchor+Math.Max(0,NetworkTime.time-elapsed-host)*rate;};
            }
            b.Buffs.Records.Dequeue();b.Buffs.RecordBytes-=p.Total;
            if(loader.IsReady)record.Dispose();else b.Buffs.Retained.Add(record);
            return b.Buffs.Prepared&&b.Buffs.Records.Count==0;
        }
        private void CommitBuff(Binding b){b.Buffs.Stage?.Commit();}
        private void PublishBuffStages(Binding b)=>PublishOwnedStages(b.Buffs.Stages);
        private void RollbackBuffStages(Binding b)
        {DisposeOwnedStages(b.Buffs.Stages);b.Buffs.Stage=null;}
        private void PublishBuff(Binding b){foreach(var value in b.Buffs.Retained)value.Dispose();b.Buffs.Retained.Clear();b.Buffs.Stage=null;}
        private void PumpBuff()
        {
            if(!NetworkServer.active)
            {
                foreach(var b in bindings.Values)
                    if(b.Buffs.Assembly!=null&&Time.realtimeSinceStartupAsDouble-b.Buffs.Assembly.Started>Scheduler.TimeoutMilliseconds/1000d||b.Buffs.Records.Count>0&&Time.realtimeSinceStartupAsDouble-b.Buffs.Records.Peek().Started>Scheduler.TimeoutMilliseconds/1000d)throw new TimeoutException("Buff transfer/decode deadline.");
                foreach(var b in bindings.Values.Where(x=>x.Candidate?.Loader?.IsReady==true&&x.Buffs.Records.Count>0&&x.Buffs.Job==null).ToArray())
                    b.Buffs.Job=Scheduler.SchedulePreparation(()=>{if(stopping||b.Retiring){b.Buffs.Job=null;return true;}bool done=PrepareBuff(b,b.Candidate.Loader);if(done)b.Buffs.Job=null;return done;},error=>{b.Buffs.Job=null;ProtocolFailure(error,false);});
                return;
            }
            int chunks=0,bytes=0;var peers=buffPeers.Values.ToArray();
            for(int i=0;i<peers.Length;i++)
            {
                var peer=peers[(i+buffPeerCursor)%peers.Length];
                if(!peer.Binding.Shell.netIdentity.observers.ContainsKey(peer.Connection.connectionId)||!NetworkServer.connections.ContainsKey(peer.Connection.connectionId)){RemoveBuffPeer(peer);continue;}
                if(peer.Binding.Retiring&&peer.Requested){RemoveBuffPeer(peer);continue;}
                if(peer.Requested&&!QueueBuffBaseline(peer,out _))continue;
                if(!peer.Binding.Retiring&&peer.ClockRate!=BuffClockRate(peer.Binding)){if(!float.IsNaN(peer.ClockRate)&&!QueueBuffBaseline(peer,out _))continue;peer.ClockRate=BuffClockRate(peer.Binding);}
                int perChunks=0,perBytes=0;
                while(peer.Packets.Count>0&&chunks<32&&bytes<512*1024&&perChunks<8&&perBytes<128*1024)
                {
                    var packet=peer.Packets.Peek();if(!TakeSendBudget(peer.Connection.connectionId,packet.Body.Length+160))break;peer.Packets.Dequeue();peer.Connection.Send(packet,Channels.Reliable);int charge=packet.Body.Length+160;peer.Bytes-=charge;hostBuffBytes-=charge;
                    chunks++;perChunks++;bytes+=packet.Body.Length;perBytes+=packet.Body.Length;
                    if(packet.Index==packet.Count-1&&packet.Kind!=2)peer.SentRevision=Math.Max(peer.SentRevision,packet.Revision);
                }
            }
            buffPeerCursor++;
            foreach(var b in bindings.Values.Where(x=>x.Retiring).ToArray())
            {
                var waiting=buffPeers.Values.Where(x=>ReferenceEquals(x.Binding,b)&&x.Packets.Count>0).ToArray();
                if(waiting.Length>0&&Time.realtimeSinceStartupAsDouble-b.RetiringAt<Profile.GraceSeconds)continue;
                foreach(var peer in waiting){peer.Connection.Disconnect();RemoveBuffPeer(peer);}
                var shell=b.Shell;CleanupBinding(b);bindings.Remove(shell.netId);NetworkServer.Destroy(shell.gameObject);
            }
        }
        private static float BuffClockRate(Binding binding)=>binding.Candidate?.Loader?.LoadContext?.CanExecute==true?Time.timeScale:0;
        private void DisposeBuff(){foreach(var peer in buffPeers.Values.ToArray())RemoveBuffPeer(peer);}
    }
}
