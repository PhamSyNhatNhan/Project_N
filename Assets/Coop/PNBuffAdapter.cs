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
        private int buffPeerCursor;
        public long DroppedOptionalBuffCues {get;private set;}
        public int PendingBuffBytes=>pendingBuffBytes;
        public int PendingBuffEvents=>pendingBuffEvents;
        private void InitializeBuff()
        {
            RequiredAdapters.Add(BuffKind);
            RegisterStateAdapter(BuffKind,()=>bindings.Values.Where(b=>!b.Retiring).All(b=>b.Candidate?.Loader!=null&&b.Candidate.Loader.IsReady&&(!b.Shell.HasBuff||subscribedSession.HasAuthority||b.Buff?.HasBaseline==true)));
            if(NetworkServer.active)NetworkServer.RegisterHandler<PNBuffBaselineRequest>(ReceiveBuffRequest);
            if(NetworkClient.active)NetworkClient.RegisterHandler<PNBuffPacket>(ReceiveBuffPacket);
            if(Transport.active!=null&&NetworkMessages.MaxContentSize(Channels.Reliable)<PNBuffWire.ChunkBytes+160)throw new InvalidOperationException("Configured transport cannot carry Buff fragments.");
        }
        private PNBuffStream Stream(Binding b)=>new PNBuffStream{Session=b.Metadata.HostSession,NetId=b.Shell.netId,Entity=b.Metadata.Entity,Generation=b.Metadata.Generation,Nonce=(ulong)b.Token};
        private void HookBuff(Binding b)
        {
            b.Buff=b.Candidate.Loader.GetComponent<PNBuffManager>();if(b.Buff==null)return;b.Buff.UseTypedTransport();
            b.BuffChanged=change=>
            {
                if(stopping||fatalSerialization||b.Shell==null)return;
                var peers=buffPeers.Values.Where(x=>ReferenceEquals(x.Binding,b)&&!x.Requested).ToArray();if(peers.Length==0)return;
                byte[] body=PNBuffWire.Encode(change);foreach(var peer in peers)EnqueueBuff(peer,1,change.State.Sequence,change.PresentationTime,body);
            };
            b.BuffCue=(cue,time)=>
            {
                if(stopping||fatalSerialization)return;
                byte[] body=null;
                foreach(var peer in buffPeers.Values.Where(x=>ReferenceEquals(x.Binding,b)&&!x.Requested).ToArray())
                {
                    if(peer.OptionalFrame!=Time.frameCount){peer.OptionalFrame=Time.frameCount;peer.OptionalCount=peer.OptionalBytes=0;}
                    if(peer.OptionalCount>=32||peer.OptionalBytes>=16384)continue;body=body??PNBuffWire.Encode(cue);
                    if(peer.OptionalBytes+body.Length>16384||peer.Bytes+body.Length>8*1024*1024||hostBuffBytes+body.Length>32*1024*1024)continue;
                    peer.OptionalCount++;peer.OptionalBytes+=body.Length;EnqueueBuff(peer,2,b.Buff.AppliedRevision,time,body);
                }
            };
            b.BuffFault=error=>ProtocolFailure(new InvalidOperationException("Buff projection actor "+b.Metadata.Entity,error),true);
            b.Buff.StateChanged+=b.BuffChanged;b.Buff.OptionalCuePublished+=b.BuffCue;b.Buff.ProjectionFailed+=b.BuffFault;
        }
        private void UnhookBuff(Binding b)
        {
            DetachBuffPublication(b);
            b.BuffJob?.Dispose();b.BuffJob=null;b.BuffDecoder=null;
            b.BuffAssembly?.Dispose();b.BuffAssembly=null;while(b.BuffRecords.Count>0)b.BuffRecords.Dequeue().Dispose();foreach(var lease in b.BuffRetained)lease.Dispose();b.BuffRetained.Clear();
            foreach(var peer in buffPeers.Values.Where(x=>ReferenceEquals(x.Binding,b)).ToArray())RemoveBuffPeer(peer);
            b.Buff=null;
        }
        private void DetachBuffPublication(Binding b){if(b.Buff!=null){b.Buff.StateChanged-=b.BuffChanged;b.Buff.OptionalCuePublished-=b.BuffCue;b.Buff.ProjectionFailed-=b.BuffFault;}}
        private void RemoveBuffPeer(BuffPeer peer){hostBuffBytes-=peer.Bytes;peer.Bytes=0;peer.Packets.Clear();buffPeers.Remove((peer.Connection.connectionId,peer.Stream.NetId));}
        private void ReceiveBuffRequest(NetworkConnectionToClient conn,PNBuffBaselineRequest request)
        {
            if(stopping||fatalSerialization)return;
            try
            {
                var s=request.Stream;s.Validate();if(!(conn.authenticationData is PNPeer)||conn is LocalConnectionToClient||!bindings.TryGetValue(s.NetId,out var b)||b.Buff==null||!b.Shell.netIdentity.observers.ContainsKey(conn.connectionId)||s.Session!=b.Metadata.HostSession||s.Entity!=b.Metadata.Entity||s.Generation!=b.Metadata.Generation)throw new FormatException("Unauthorized Buff baseline request.");
                if(buffPeers.TryGetValue((conn.connectionId,s.NetId),out var old))
                {if(old.Stream.Equals(s))return;if(s.Nonce<=old.Stream.Nonce)throw new FormatException("Stale Buff observer nonce.");RemoveBuffPeer(old);}
                buffPeers.Add((conn.connectionId,s.NetId),new BuffPeer{Connection=conn,Binding=b,Stream=s});
            }
            catch(Exception error){LastFailure=error.Message;conn.Disconnect();}
        }
        private bool QueueBuffBaseline(BuffPeer peer,out long revision)
        {
            revision=0;var status=peer.Binding.Buff.TryCaptureState(out var frame);if(status==PNBuffCaptureStatus.Busy)return false;if(status==PNBuffCaptureStatus.Faulted)throw new InvalidOperationException("Buff baseline capture fault.");
            EnqueueBuff(peer,0,frame.Revision,frame.PresentationTime,PNBuffWire.Encode(frame));peer.Requested=false;revision=frame.Revision;return true;
        }
        private void EnqueueBuff(BuffPeer peer,byte kind,long revision,double time,byte[] bytes)
        {
            int count=(bytes.Length+PNBuffWire.ChunkBytes-1)/PNBuffWire.ChunkBytes;int charged=checked(bytes.Length+count*160);
            if(peer.Bytes+charged>8*1024*1024||hostBuffBytes+charged>32*1024*1024){peer.Connection.Disconnect();RemoveBuffPeer(peer);return;}
            ulong transfer=checked(++buffTransfer);
            for(int i=0;i<count;i++)
            {
                int size=Math.Min(PNBuffWire.ChunkBytes,bytes.Length-i*PNBuffWire.ChunkBytes);var body=new byte[size];Buffer.BlockCopy(bytes,i*PNBuffWire.ChunkBytes,body,0,size);
                peer.Packets.Enqueue(new PNBuffPacket{Stream=peer.Stream,Transfer=transfer,Kind=kind,Revision=revision,Time=time,HostTime=NetworkTime.time,Rate=Time.timeScale,Total=bytes.Length,Index=i,Count=count,Body=body});
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
            if(stopping||fatalSerialization||NetworkServer.active)return;
            try
            {
                if(!bindings.TryGetValue(p.Stream.NetId,out var b)||!p.Stream.Equals(Stream(b)))return;
                if(!b.Shell.HasBuff)throw new FormatException("Unexpected Buff stream.");
                if(p.Kind==2&&p.Transfer==b.DroppedOptionalTransfer)return;
                if(b.BuffAssembly==null)
                {
                    if(p.Transfer<=b.LastBuffTransfer)throw new FormatException("Reused Buff transfer.");
                    try{b.BuffAssembly=new PNBuffBaselineAssembly(p,Time.realtimeSinceStartupAsDouble,ReserveBuff,ReserveBuffStructure);}
                    catch(InvalidOperationException) when(p.Kind==2){b.DroppedOptionalTransfer=p.Transfer;b.LastBuffTransfer=p.Transfer;DroppedOptionalBuffCues++;return;}
                }
                else b.BuffAssembly.Add(p);
                if(!b.BuffAssembly.Complete)return;
                int retained=b.BuffRecords.Where(x=>x.Header.Kind==1).Sum(x=>x.Header.Total)+b.BuffRetained.OfType<PNBuffBaselineAssembly>().Where(x=>x.Header.Kind==1).Sum(x=>x.Header.Total);
                if(b.BuffRecords.Count+b.BuffRetained.Count>=256||retained+p.Total>256*1024&&p.Kind==1)
                {if(p.Kind==2){b.BuffAssembly.Dispose();b.BuffAssembly=null;b.LastBuffTransfer=p.Transfer;DroppedOptionalBuffCues++;return;}throw new FormatException("Pending Buff required event bound.");}
                b.LastBuffTransfer=p.Transfer;b.BuffRecords.Enqueue(b.BuffAssembly);b.BuffRecordBytes+=p.Total;b.BuffAssembly=null;
            }
            catch(Exception error){ProtocolFailure(error,false);}
        }
        private bool PrepareBuff(Binding b,EveEntityLoader loader)
        {
            if(b.Buff==null)b.Buff=loader.GetComponent<PNBuffManager>();
            if((b.Buff!=null)!=b.Shell.HasBuff)throw new FormatException("Buff component descriptor mismatch.");
            if(b.Buff==null)return true;
            if(b.BuffAssembly!=null&&Time.realtimeSinceStartupAsDouble-b.BuffAssembly.Started>Scheduler.TimeoutMilliseconds/1000d)throw new TimeoutException("Buff assembly deadline.");
            if(b.BuffRecords.Count==0)return b.BuffPrepared;
            var record=b.BuffRecords.Peek();var p=record.Header;
            if(p.Kind==0)
            {
                if(b.BuffDecoder==null)b.BuffDecoder=new PNBuffWire.FrameDecoder(record.Body(),p,record.ReserveNodes);
                if(!b.BuffDecoder.Advance())return false;
                var frame=b.BuffDecoder.Result;b.BuffDecoder=null;var stage=b.Buff.StageState(frame,p.Stream.Session,p.Stream.Generation);if(!b.Stages.Contains(stage))b.Stages.Add(stage);b.BuffStage=stage;b.BuffPrepared=true;
                if(loader.IsReady){stage.Commit();stage.Published();stage.Dispose();b.Stages.Remove(stage);b.BuffStage=null;}
            }
            else if(p.Kind==1)
            {
                if(!b.BuffPrepared)throw new FormatException("Buff delta before baseline.");
                b.Buff.ApplyChange(PNBuffWire.DecodeChange(record.Body(),p,record.ReserveNodes),p.Stream.Session,p.Stream.Generation);
            }
            else if(loader.IsReady&&b.Buff.HasBaseline&&b.Buff.AppliedRevision>=p.Revision)
            {try{b.Buff.ApplyOptionalCue(PNBuffWire.DecodeCue(record.Body(),p,record.ReserveNodes),p.Time);}catch(Exception error) when(error is FormatException||error is IOException||error is InvalidOperationException){DroppedOptionalBuffCues++;}}
            if(p.Kind!=2)
            {
                double anchor=p.Time,host=p.HostTime,rate=p.Rate;
                b.Buff.AuthoritativePresentationTime=()=>{double elapsed=clientInterrupted>=0?Math.Max(0,Time.realtimeSinceStartupAsDouble-clientInterrupted):0;return anchor+Math.Max(0,NetworkTime.time-elapsed-host)*rate;};
            }
            b.BuffRecords.Dequeue();b.BuffRecordBytes-=p.Total;
            if(loader.IsReady)record.Dispose();else b.BuffRetained.Add(record);
            return b.BuffPrepared&&b.BuffRecords.Count==0;
        }
        private void CommitBuff(Binding b){b.BuffStage?.Commit();}
        private void PublishBuff(Binding b){foreach(var value in b.BuffRetained)value.Dispose();b.BuffRetained.Clear();b.BuffStage=null;}
        private void PumpBuff()
        {
            if(!NetworkServer.active)
            {
                foreach(var b in bindings.Values)
                    if(b.BuffAssembly!=null&&Time.realtimeSinceStartupAsDouble-b.BuffAssembly.Started>Scheduler.TimeoutMilliseconds/1000d||b.BuffRecords.Count>0&&Time.realtimeSinceStartupAsDouble-b.BuffRecords.Peek().Started>Scheduler.TimeoutMilliseconds/1000d)throw new TimeoutException("Buff transfer/decode deadline.");
                foreach(var b in bindings.Values.Where(x=>x.Candidate?.Loader?.IsReady==true&&x.BuffRecords.Count>0&&x.BuffJob==null).ToArray())
                    b.BuffJob=Scheduler.SchedulePreparation(()=>{if(stopping||b.Retiring){b.BuffJob=null;return true;}bool done=PrepareBuff(b,b.Candidate.Loader);if(done)b.BuffJob=null;return done;},error=>{b.BuffJob=null;ProtocolFailure(error,false);});
                return;
            }
            int chunks=0,bytes=0;var peers=buffPeers.Values.ToArray();
            for(int i=0;i<peers.Length;i++)
            {
                var peer=peers[(i+buffPeerCursor)%peers.Length];
                if(!peer.Binding.Shell.netIdentity.observers.ContainsKey(peer.Connection.connectionId)||!NetworkServer.connections.ContainsKey(peer.Connection.connectionId)){RemoveBuffPeer(peer);continue;}
                if(peer.Binding.Retiring&&peer.Requested){RemoveBuffPeer(peer);continue;}
                if(peer.Requested&&!QueueBuffBaseline(peer,out _))continue;
                if(!peer.Binding.Retiring&&peer.ClockRate!=Time.timeScale){if(!float.IsNaN(peer.ClockRate)&&!QueueBuffBaseline(peer,out _))continue;peer.ClockRate=Time.timeScale;}
                int perChunks=0,perBytes=0;
                while(peer.Packets.Count>0&&chunks<32&&bytes<512*1024&&perChunks<8&&perBytes<128*1024)
                {
                    var packet=peer.Packets.Dequeue();peer.Connection.Send(packet,Channels.Reliable);int charge=packet.Body.Length+160;peer.Bytes-=charge;hostBuffBytes-=charge;
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
                var shell=b.Shell;UnhookStat(b);bindings.Remove(shell.netId);NetworkServer.Destroy(shell.gameObject);
            }
        }
        private void DisposeBuff(){foreach(var b in bindings.Values.Concat(awaitingSpawn).ToArray())UnhookBuff(b);foreach(var peer in buffPeers.Values.ToArray())RemoveBuffPeer(peer);}
    }
}
