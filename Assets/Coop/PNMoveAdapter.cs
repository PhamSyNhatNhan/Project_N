using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using EveEngine.Movement;
using Mirror;
using UnityEngine;

namespace ProjectN.Coop
{
    public sealed partial class PNNetworkManager
    {
        private sealed class MoveBindingState
        {
            public PNMove Component;
            public bool Attached,AwaitingBaseline;
            public uint Request;
            public double LastRequest;
            public double BaselineStarted;
            public long Received;
            public string ControlDigest;
            public PNMovePacket? Baseline,Applying,Pose;
            public IDisposable ApplyingLease;
            public readonly Queue<(PNMovePacket Packet,IDisposable Lease)> Controls=new Queue<(PNMovePacket,IDisposable)>();
            public EveEngine.Movement.MovePoseReport LatestReport;
            public Action<EveEngine.Movement.MovePoseReport> Report;
            public Action<Exception> Failure;
        }
        private const string MoveKind="project_n:move";
        private sealed class MovePeer
        {
            public Binding Binding;public NetworkConnectionToClient Connection;public PNMoveStream Stream;
            public uint Request;public long Revision,SentRevision;
            public uint MirrorRevision,CorrectionId;
            public readonly Queue<(PNMovePacket Packet,int Bytes)> Queue=new Queue<(PNMovePacket,int)>();
            public int Bytes;public bool NeedsBaseline=true;
        }
        private PNMoveHostWorld moveWorld;
        public PNMoveHostWorld MovementAuthority=>moveWorld;
        private readonly Dictionary<(int,uint),MovePeer> movePeers=new Dictionary<(int,uint),MovePeer>();
        private double lastMoveTick;
        private int moveCursor,hostMoveBytes,pendingMoveBytes,pendingMoveEvents;
        public int StaleMoveRequests{get;private set;}
        public int PendingMoveBytes=>pendingMoveBytes;
        public int PendingMoveEvents=>pendingMoveEvents;
        private void InitializeMove()
        {
            RequiredAdapters.Add(MoveKind);
            RegisterStateAdapter(MoveKind,()=>bindings.Values.Where(b=>!b.Retiring&&RoomCatchupBinding(b)).All(b=>!b.Shell.HasMove||subscribedSession.HasAuthority||b.Movement.Component?.MovementBaselineApplied==true));
            if(NetworkServer.active){moveWorld=new PNMoveHostWorld(subscribedSession,Scheduler.MaximumResidents);NetworkServer.ReplaceHandler<PNMoveRequest>(ReceiveMoveRequest);NetworkServer.ReplaceHandler<PNMovePacket>(ReceiveMoveReport);}
            if(NetworkClient.active)NetworkClient.ReplaceHandler<PNMovePacket>(ReceiveMovePacket);
            if(Transport.active!=null&&(NetworkMessages.MaxContentSize(Channels.Unreliable)<PNMoveWire.PoseBytes||NetworkMessages.MaxContentSize(Channels.Reliable)<PNMoveWire.ControlBytes))throw new InvalidOperationException("Move transport limits.");
        }
        private PNMoveStream MovementStream(Binding b)=>new PNMoveStream{Session=b.Metadata.HostSession,NetId=b.Shell.netId,Entity=b.Metadata.Entity,Generation=b.Metadata.Generation,Nonce=(ulong)b.Token,Scope=Scope(b)};
        private void ConfigureMove(Binding b,EveEntityLoader loader)
        {
            b.Movement.Component=loader.GetComponent<PNMove>();if(b.Movement.Component==null)return;
            b.Movement.Component.BeginMovementBinding();
            var gate=loader.GetComponent<PNNetworkInputGate>()??loader.gameObject.AddComponent<PNNetworkInputGate>();gate.Blocked=true;
            b.Movement.Failure=e=>{if(subscribedSession.HasAuthority)WorldFailure(b.Metadata.World,e);else ProtocolFailure(e,false);};b.Movement.Component.MovementFailed+=b.Movement.Failure;
        }
        private void PublishMove(Binding b)
        {
            if(b.Movement.Component==null){if(b.Shell?.HasMove==true)throw new FormatException("Required Move component missing.");return;}
            if(subscribedSession.HasAuthority)
            {
                // Stat life has to be capturable before Track; deferred host pump completes attachment.
                return;
            }
            if(!b.Shell.HasMove)throw new FormatException("Move component descriptor mismatch.");
            AttachClientMove(b);
        }
        private void AttachClientMove(Binding b)
        {
            if(b.Movement.Component==null||!b.Movement.Component.NetworkUsable)return;
            bool owner=b.Metadata.Owner==subscribedSession.LocalOwnerId;
            if(owner)
            {
                b.Movement.Component.BindOwnerMovement(b.Metadata.HostSession);b.Movement.Component.OwnerMovement.ExpectBaseline(b.Movement.Request);
                b.Movement.Report=q=>b.Movement.LatestReport=q;b.Movement.Component.MovementReportPublished+=b.Movement.Report;
            }
            else b.Movement.Component.BindObserverMovement(b.Metadata.HostSession);
            b.Movement.Attached=true;
        }
        private void RequestMove(Binding b,bool fresh)
        {
            if(!b.Shell.HasMove)return;
            if(fresh){b.Movement.Request=MoveOrder.Next(b.Movement.Request);b.Movement.BaselineStarted=Time.realtimeSinceStartupAsDouble;b.Movement.AwaitingBaseline=true;b.Movement.Received=0;b.Movement.Baseline=null;b.Movement.Pose=null;b.Movement.LatestReport=null;while(b.Movement.Controls.Count>0)b.Movement.Controls.Dequeue().Lease.Dispose();b.Movement.ApplyingLease?.Dispose();b.Movement.ApplyingLease=null;b.Movement.Applying=null;b.Movement.Component?.SetMovementHeld(true);if(b.Movement.Component?.OwnerMovement!=null)b.Movement.Component.OwnerMovement.ExpectBaseline(b.Movement.Request);}
            NetworkClient.Send(new PNMoveRequest{Stream=MovementStream(b),Request=b.Movement.Request});b.Movement.LastRequest=Time.realtimeSinceStartupAsDouble;
        }
        private void ReceiveMoveRequest(NetworkConnectionToClient conn,PNMoveRequest request)
        {
            if(stopping||!IsPlaying||fatalSerialization)return;
            try
            {
                var s=request.Stream;s.Validate();if(request.Request!=0&&IsRetiredMove(conn,s))return;
                // Stale, not hostile: the entity's binding was removed or is retiring (owner replacement/transfer, latency) before this request arrived.
                if(request.Request!=0&&conn.authenticationData is PNPeer&&!(conn is LocalConnectionToClient)&&(!bindings.TryGetValue(s.NetId,out var gone)||gone.Retiring)){StaleMoveRequests++;Debug.LogWarning("Ignored stale Move request for netId "+s.NetId+" from connection "+conn.connectionId);return;}
                if(request.Request==0||!(conn.authenticationData is PNPeer)||conn is LocalConnectionToClient||!bindings.TryGetValue(s.NetId,out var b)||b.Movement.Component==null||b.Retiring||!b.Shell.netIdentity.observers.ContainsKey(conn.connectionId)||s.Session!=b.Metadata.HostSession||s.Entity!=b.Metadata.Entity||s.Generation!=b.Metadata.Generation||!AuthorizeWorldRequest(conn,b,s.Scope))throw new FormatException("Unauthorized Move request.");
                if(movePeers.TryGetValue((conn.connectionId,s.NetId),out var old))
                {
                    if(s.Nonce<old.Stream.Nonce)return;
                    if(s.Equals(old.Stream))
                    {
                        if(request.Request==old.Request)return; // Reliable delivery owns retries after first acceptance.
                        if(!MoveOrder.Newer(request.Request,old.Request))return;
                        old.Request=request.Request;old.NeedsBaseline=true;return;
                    }
                    RemoveMovePeer(old);
                }
                movePeers.Add((conn.connectionId,s.NetId),new MovePeer{Connection=conn,Binding=b,Stream=s,Request=request.Request});
            }
            catch(Exception error){LastFailure=error.Message;conn.Disconnect();}
        }
        private void ReceiveMoveReport(NetworkConnectionToClient conn,PNMovePacket p)
        {
            if(stopping||!IsPlaying||fatalSerialization)return;
            try
            {
                if(p.Kind!=4||!(conn.authenticationData is PNPeer peer))throw new FormatException("Move upstream kind/owner.");
                if(!movePeers.TryGetValue((conn.connectionId,p.Stream.NetId),out var stream)||!stream.Stream.Equals(p.Stream)||p.Request!=stream.Request||stream.Binding.Retiring)return;
                if(stream.Binding.Metadata.Owner!=peer.Owner)throw new FormatException("Wrong Move owner.");
                if(!PeerInputReady(conn)||!WorldInputAllowed(stream.Binding)||!AuthorizeWorldRequest(conn,stream.Binding,p.Stream.Scope)||!stream.Binding.Movement.Component.NetworkUsable)return;
                var result=moveWorld.ReceiveTyped(p.Report,peer.Owner,NetworkTime.time);
                if(result==MoveReportResult.WrongOwner||result==MoveReportResult.Malformed)throw new FormatException("Move report rejected.");
            }
            catch(Exception error){LastFailure=error.Message;conn.Disconnect();}
        }
        private int MovePacketBytes(PNMovePacket p)
        {using(var writer=NetworkWriterPool.Get()){writer.WriteMovePacket(p);return writer.Position+128;}}
        private void QueueMove(MovePeer peer,PNMovePacket p)
        {
            int size=MovePacketBytes(p);
            long peerTotal=movePeers.Values.Where(x=>x.Connection==peer.Connection).Sum(x=>(long)x.Bytes)+buffPeers.Values.Where(x=>x.Connection==peer.Connection).Sum(x=>(long)x.Bytes);
            if(peer.Queue.Count>=64||peer.Bytes+size>262144||peerTotal+size>8*1024*1024||(long)hostMoveBytes+hostBuffBytes+size>32*1024*1024)throw new InvalidOperationException("Move required send queue bound.");
            peer.Queue.Enqueue((p,size));peer.Bytes+=size;hostMoveBytes+=size;
        }
        private bool QueueMoveBaseline(MovePeer peer,out long revision)
        {
            revision=0;var b=peer.Binding;var model=moveWorld.TypedModel(peer.Stream.Identity);if(model==null||!b.Movement.Component.NetworkUsable||!model.Ready)return false;
            bool owner=b.Metadata.Owner==((PNPeer)peer.Connection.authenticationData).Owner;
            if(owner)
            {
                // Request IDs are per owner stream; retries never allocate a new epoch.
                var result=moveWorld.ReceiveTyped(new MoveBaselineRequest(peer.Stream.Identity,peer.Request),b.Metadata.Owner,NetworkTime.time);
                if(result!=MoveReportResult.Accepted&&result!=MoveReportResult.Stale)return false;
            }
            var p=ControlPacket(peer,model,owner?(byte)0:(byte)2);QueueMove(peer,p);peer.NeedsBaseline=false;revision=p.Revision;return true;
        }
        private PNMovePacket ControlPacket(MovePeer peer,MoveHostModel model,byte kind)
        {
            peer.MirrorRevision=model.Mirror.Revision;peer.CorrectionId=model.LastCorrection.Id;
            return new PNMovePacket{Kind=kind,Stream=peer.Stream,Request=peer.Request,Revision=checked(++peer.Revision),Life=model.Mirror.LifeRevision,Mirror=model.Mirror,Correction=model.LastCorrection,Pose=kind==2?model.Sample(NetworkTime.time):null};
        }
        private IDisposable ReserveMove(int size)
        {
            var lease=ReserveBuff(size);pendingMoveBytes+=size;pendingMoveEvents++;
            return new PNBuffRelease(()=>{pendingMoveBytes-=size;pendingMoveEvents--;lease.Dispose();});
        }
        private void ReceiveMovePacket(PNMovePacket p)
        {
            if(stopping||!IsPlaying||fatalSerialization||NetworkServer.active)return;
            try
            {
                if(p.Kind==4)throw new FormatException("Move downstream kind.");
                if(!bindings.TryGetValue(p.Stream.NetId,out var b)||!IsClientBindingCurrent(b)||!MovementStream(b).Equals(p.Stream)||p.Request!=b.Movement.Request||b.Retiring)return;
                if(!b.Shell.HasMove)throw new FormatException("Unexpected Move adapter.");
                if(p.Kind==3)
                {if(b.Movement.AwaitingBaseline||p.Revision<b.Movement.Received)return;b.Movement.Pose=p;return;}
                bool owner=b.Metadata.Owner==subscribedSession.LocalOwnerId;
                if(owner!=(p.Kind<=1))throw new FormatException("Move downstream role.");
                string controlDigest;using(var w=NetworkWriterPool.Get()){w.WriteMovePacket(p);using(var hash=System.Security.Cryptography.SHA256.Create())controlDigest=Convert.ToBase64String(hash.ComputeHash(w.ToArray()));}
                if(p.Revision<b.Movement.Received)return;
                if(p.Revision==b.Movement.Received){if(controlDigest!=b.Movement.ControlDigest)throw new FormatException("Conflicting Move control revision.");return;}
                if(b.Movement.Received!=0&&p.Revision!=b.Movement.Received+1)throw new FormatException("Missing required Move control.");
                if(b.Movement.Controls.Count>=64||b.Movement.Controls.Sum(x=>(long)MovePacketBytes(x.Packet))+MovePacketBytes(p)>262144)throw new FormatException("Move pending controls bound.");
                if(b.Movement.AwaitingBaseline && owner && (p.Kind!=0||p.Correction.BaselineRequestId!=b.Movement.Request))return;
                var lease=ReserveMove(MovePacketBytes(p));b.Movement.Controls.Enqueue((p,lease));b.Movement.Received=p.Revision;b.Movement.ControlDigest=controlDigest;
                if(b.Movement.AwaitingBaseline){b.Movement.Baseline=p;b.Movement.AwaitingBaseline=false;}
            }
            catch(Exception error){ProtocolFailure(error,false);}
        }
        private bool PrepareMove(Binding b,EveEntityLoader loader)
        {
            bool present=loader.GetComponent<PNMove>()!=null;if(present!=b.Shell.HasMove)throw new FormatException("Move required descriptor mismatch.");
            return !present||b.Movement.Baseline.HasValue;
        }
        private bool MoveLifeCompatible(Binding b,PNMovePacket p)
        {
            var stat=b.Candidate.Loader.GetComponent<PNStatReplication>()?.View;
            return stat!=null&&stat.LifeRevision==p.Life;
        }
        private void PumpClientMove()
        {
            foreach(var b in bindings.Values.Where(x=>!x.Retiring&&x.Shell.HasMove).ToArray())
            {
                if(Time.realtimeSinceStartupAsDouble-b.Movement.BaselineStarted>Profile.AdmissionSeconds&&b.Movement.AwaitingBaseline)throw new TimeoutException("Movement baseline deadline.");
                if(b.Movement.AwaitingBaseline&&Time.realtimeSinceStartupAsDouble-b.Movement.LastRequest>.5)RequestMove(b,false);
                if(b.Movement.Component==null||b.Candidate.Loader?.IsReady!=true||!b.Movement.Component.NetworkUsable)continue;
                if(b.Movement.Attached&&b.Movement.Component.NetworkRole==PNMoveNetworkRole.Local)
                {b.Movement.Attached=false;b.Movement.Component.MovementReportPublished-=b.Movement.Report;RequestMove(b,true);}
                if(!b.Movement.Attached)AttachClientMove(b);
                var life=b.Candidate.Loader.GetComponent<PNStatReplication>()?.View;
                if(life!=null&&b.Movement.Controls.Count>0&&b.Movement.Controls.Peek().Packet.Life<life.LifeRevision){RequestMove(b,true);continue;}
                if(life!=null&&b.Movement.Component.OwnerMovement?.Mirror!=null&&b.Movement.Component.OwnerMovement.Mirror.LifeRevision<life.LifeRevision)b.Movement.Component.SetMovementHeld(true);
                if(b.Movement.Applying.HasValue&&b.Movement.Component.AppliedMovementControl>=(ulong)b.Movement.Applying.Value.Revision){b.Movement.ApplyingLease?.Dispose();b.Movement.ApplyingLease=null;b.Movement.Applying=null;}
                if(!b.Movement.Applying.HasValue&&b.Movement.Controls.Count>0&&MoveLifeCompatible(b,b.Movement.Controls.Peek().Packet))
                {
                    var next=b.Movement.Controls.Dequeue();b.Movement.Applying=next.Packet;b.Movement.ApplyingLease=next.Lease;
                    if(next.Packet.Kind<=1)b.Movement.Component.StageMovementControl(next.Packet.Mirror,next.Packet.Correction,(ulong)next.Packet.Revision);
                    else b.Movement.Component.StageObserverMovement(next.Packet.Pose,(ulong)next.Packet.Revision);
                }
                bool coherent=b.Movement.Component.MovementBaselineApplied&&!b.Movement.AwaitingBaseline&&(b.Movement.Component.OwnerMovement?.Mirror==null||life!=null&&b.Movement.Component.OwnerMovement.Mirror.LifeRevision==life.LifeRevision);
                if(!b.Movement.Component.MovementHeld&&(clientInterrupted>=0||paused))RequestMove(b,true);
                b.Movement.Component.SetMovementHeld(!coherent||!WorldInputAllowed(b)||!clientInputReady||clientInterrupted>=0||paused);
                if(b.Movement.Component.NetworkRole==PNMoveNetworkRole.Observer&&coherent&&!paused&&clientInterrupted<0)
                {
                    if(b.Movement.Pose.HasValue&&b.Movement.Pose.Value.Revision<=(long)b.Movement.Component.AppliedMovementControl)
                    {b.Movement.Component.ObserverMovement.Receive(b.Movement.Pose.Value.Pose,true);b.Movement.Pose=null;}
                    if(!b.Movement.Component.MovementHeld)b.Movement.Component.ApplyObserverPose(Math.Max(0,NetworkTime.time-Profile.MoveInterpolationSeconds));
                }
            }
            if(NetworkTime.time-lastMoveTick<Profile.MoveSendSeconds)return;lastMoveTick=NetworkTime.time;
            foreach(var b in bindings.Values.Where(x=>!x.Retiring&&x.Movement.LatestReport!=null&&x.Movement.Component?.MovementHeld==false))
            {var p=new PNMovePacket{Kind=4,Stream=MovementStream(b),Request=b.Movement.Request,Revision=Math.Max(1,b.Movement.Received),Life=b.Movement.Component.OwnerMovement.Mirror.LifeRevision,Report=b.Movement.LatestReport};if(!TryMoveSend(null,p,Channels.Unreliable))break;b.Movement.LatestReport=null;}
        }
        private void PumpMove()
        {
            if(!NetworkServer.active){PumpClientMove();return;}
            foreach(var b in bindings.Values.Where(x=>!x.Retiring&&x.Movement.Component!=null).Concat(awaitingSpawn.Where(x=>x.Movement.Component!=null)).Distinct().ToArray())
            {
                if(!b.Movement.Component.NetworkUsable)continue;
                if(b.Movement.Attached){if(b.Metadata.Owner==0)SetInput(b,hostInterrupted<0);else b.Movement.Component.SetMovementHeld(false);continue;}
                if(b.Candidate.Loader.GetComponent<PNStatReplication>()?.CanCaptureReplication!=true)continue;
                moveWorld.TrackTyped(b.Movement.Component,b.Metadata.Owner>0);b.Movement.Attached=true;b.Movement.Component.SetMovementHeld(false);
                if(b.Metadata.Owner==0)SetInput(b,hostInterrupted<0);
            }
            moveWorld.Advance(NetworkTime.time);
            foreach(var b in bindings.Values.Where(x=>!x.Retiring&&x.Movement.Attached))if(moveWorld.SamplingFailure(b.Movement.Component.NetworkIdentity(b.Metadata.HostSession)) is Exception error)throw new InvalidOperationException("Move capture failed.",error);
            var peers=movePeers.Values.ToArray();if(peers.Length==0)return;
            bool tick=NetworkTime.time-lastMoveTick>=Profile.MoveSendSeconds;if(tick)lastMoveTick=NetworkTime.time;
            for(int i=0;i<peers.Length;i++)
            {
                var peer=peers[(i+moveCursor)%peers.Length];var b=peer.Binding;
                if(b.Retiring||!NetworkServer.connections.ContainsKey(peer.Connection.connectionId)||!b.Shell.netIdentity.observers.ContainsKey(peer.Connection.connectionId)){RemoveMovePeer(peer);continue;}
                var model=moveWorld.TypedModel(peer.Stream.Identity);if(model==null||!b.Movement.Component.NetworkUsable)continue;
                if(peer.NeedsBaseline){if(!QueueMoveBaseline(peer,out _))continue;}
                else if(peer.MirrorRevision!=model.Mirror.Revision||peer.CorrectionId!=model.LastCorrection.Id)QueueMove(peer,ControlPacket(peer,model,b.Metadata.Owner==((PNPeer)peer.Connection.authenticationData).Owner?(byte)1:(byte)2));
                while(peer.Queue.Count>0){var item=peer.Queue.Peek();if(!TryMoveSend(peer.Connection,item.Packet,Channels.Reliable))break;peer.Queue.Dequeue();peer.Bytes-=item.Bytes;hostMoveBytes-=item.Bytes;peer.SentRevision=item.Packet.Revision;}
                if(tick&&peer.Queue.Count==0&&b.Metadata.Owner!=((PNPeer)peer.Connection.authenticationData).Owner)
                    TryMoveSend(peer.Connection,new PNMovePacket{Kind=3,Stream=peer.Stream,Request=peer.Request,Revision=peer.SentRevision,Life=model.Mirror.LifeRevision,Pose=model.Sample(NetworkTime.time)},Channels.Unreliable);
            }
            moveCursor++;
        }
        private void RemoveMovePeer(MovePeer peer){hostMoveBytes-=peer.Bytes;peer.Bytes=0;peer.Queue.Clear();movePeers.Remove((peer.Connection.connectionId,peer.Stream.NetId));}
        private void UnhookMove(Binding b)
        {
            foreach(var p in movePeers.Values.Where(x=>x.Binding==b).ToArray()){RememberRetiredMove(p);RemoveMovePeer(p);}
            while(b.Movement.Controls.Count>0)b.Movement.Controls.Dequeue().Lease.Dispose();b.Movement.ApplyingLease?.Dispose();b.Movement.ApplyingLease=null;
            if(b.Movement.Component!=null){b.Movement.Component.MovementReportPublished-=b.Movement.Report;b.Movement.Component.MovementFailed-=b.Movement.Failure;if(subscribedSession?.HasAuthority==true)moveWorld?.Untrack(new MoveIdentity(b.Metadata.HostSession,b.Metadata.Entity,b.Metadata.Generation));b.Movement.Component.ReleaseNetworkMovement();}
            b.Movement.Component=null;
        }
        private void DisposeMove(){foreach(var p in movePeers.Values.ToArray())RemoveMovePeer(p);moveWorld?.Dispose();moveWorld=null;}
        private int sendBudgetFrame=-1,sendBudgetBytes,sendBudgetMessages;
        private readonly Dictionary<int,(int Bytes,int Messages)> peerSendBudget=new Dictionary<int,(int,int)>();
        private bool TakeSendBudget(int connection,int bytes)
        {
            if(sendBudgetFrame!=Time.frameCount){sendBudgetFrame=Time.frameCount;sendBudgetBytes=sendBudgetMessages=0;peerSendBudget.Clear();}
            peerSendBudget.TryGetValue(connection,out var used);
            if(sendBudgetMessages>=512||sendBudgetBytes+bytes>256*1024||used.Messages>=128||used.Bytes+bytes>64*1024)return false;
            sendBudgetMessages++;sendBudgetBytes+=bytes;peerSendBudget[connection]=(used.Bytes+bytes,used.Messages+1);return true;
        }
        private bool TryMoveSend(NetworkConnectionToClient connection,PNMovePacket packet,int channel)
        {
            using(var w=NetworkWriterPool.Get())
            {
                w.WriteMovePacket(packet);if(w.Position>NetworkMessages.MaxContentSize(channel))throw new FormatException("Movement exceeds configured channel.");
                if(!TakeSendBudget(connection?.connectionId??-1,w.Position+16))return false;
                if(connection==null)NetworkClient.Send(packet,channel);else connection.Send(packet,channel);return true;
            }
        }
    }
}
