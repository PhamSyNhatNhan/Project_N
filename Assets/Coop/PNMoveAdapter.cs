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
        public int PendingMoveBytes=>pendingMoveBytes;
        public int PendingMoveEvents=>pendingMoveEvents;
        private void InitializeMove()
        {
            RequiredAdapters.Add(MoveKind);
            RegisterStateAdapter(MoveKind,()=>bindings.Values.Where(b=>!b.Retiring).All(b=>!b.Shell.HasMove||subscribedSession.HasAuthority||b.Move?.MovementBaselineApplied==true));
            if(NetworkServer.active){moveWorld=new PNMoveHostWorld(subscribedSession,Scheduler.MaximumResidents);NetworkServer.RegisterHandler<PNMoveRequest>(ReceiveMoveRequest);NetworkServer.RegisterHandler<PNMovePacket>(ReceiveMoveReport);}
            if(NetworkClient.active)NetworkClient.RegisterHandler<PNMovePacket>(ReceiveMovePacket);
            if(Transport.active!=null&&(NetworkMessages.MaxContentSize(Channels.Unreliable)<PNMoveWire.PoseBytes||NetworkMessages.MaxContentSize(Channels.Reliable)<PNMoveWire.ControlBytes))throw new InvalidOperationException("Move transport limits.");
        }
        private PNMoveStream MovementStream(Binding b)=>new PNMoveStream{Session=b.Metadata.HostSession,NetId=b.Shell.netId,Entity=b.Metadata.Entity,Generation=b.Metadata.Generation,Nonce=(ulong)b.Token};
        private void ConfigureMove(Binding b,EveEntityLoader loader)
        {
            b.Move=loader.GetComponent<PNMove>();if(b.Move==null)return;
            b.Move.ConfigureNetworkMovement();
            var gate=loader.GetComponent<PNNetworkInputGate>()??loader.gameObject.AddComponent<PNNetworkInputGate>();gate.Blocked=true;
            b.MoveFailure=e=>ProtocolFailure(e,subscribedSession.HasAuthority);b.Move.MovementFailed+=b.MoveFailure;
        }
        private void PublishMove(Binding b)
        {
            if(b.Move==null){if(b.Shell?.HasMove==true)throw new FormatException("Required Move component missing.");return;}
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
            if(b.Move==null||!b.Move.NetworkUsable)return;
            bool owner=b.Metadata.Owner==subscribedSession.LocalOwnerId;
            if(owner)
            {
                b.Move.BindOwnerMovement(b.Metadata.HostSession);b.Move.OwnerMovement.ExpectBaseline(b.MoveRequest);
                b.MoveReport=q=>b.LatestMoveReport=q;b.Move.MovementReportPublished+=b.MoveReport;
            }
            else b.Move.BindObserverMovement(b.Metadata.HostSession);
            b.MoveAttached=true;
        }
        private void RequestMove(Binding b,bool fresh)
        {
            if(!b.Shell.HasMove)return;
            if(fresh){b.MoveRequest=MoveOrder.Next(b.MoveRequest);b.MoveBaselineStarted=Time.realtimeSinceStartupAsDouble;b.MoveAwaitingBaseline=true;b.MoveReceived=0;b.MoveBaseline=null;b.MovePose=null;b.LatestMoveReport=null;while(b.MoveControls.Count>0)b.MoveControls.Dequeue().Lease.Dispose();b.MoveApplyingLease?.Dispose();b.MoveApplyingLease=null;b.MoveApplying=null;b.Move?.SetMovementHeld(true);if(b.Move?.OwnerMovement!=null)b.Move.OwnerMovement.ExpectBaseline(b.MoveRequest);}
            NetworkClient.Send(new PNMoveRequest{Stream=MovementStream(b),Request=b.MoveRequest});b.LastMoveRequest=Time.realtimeSinceStartupAsDouble;
        }
        private void ReceiveMoveRequest(NetworkConnectionToClient conn,PNMoveRequest request)
        {
            if(stopping||fatalSerialization)return;
            try
            {
                var s=request.Stream;s.Validate();if(request.Request==0||!(conn.authenticationData is PNPeer)||conn is LocalConnectionToClient||!bindings.TryGetValue(s.NetId,out var b)||b.Move==null||b.Retiring||!b.Shell.netIdentity.observers.ContainsKey(conn.connectionId)||s.Session!=b.Metadata.HostSession||s.Entity!=b.Metadata.Entity||s.Generation!=b.Metadata.Generation)throw new FormatException("Unauthorized Move request.");
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
            if(stopping||fatalSerialization)return;
            try
            {
                if(p.Kind!=4||!(conn.authenticationData is PNPeer peer))throw new FormatException("Move upstream kind/owner.");
                if(!movePeers.TryGetValue((conn.connectionId,p.Stream.NetId),out var stream)||!stream.Stream.Equals(p.Stream)||p.Request!=stream.Request||stream.Binding.Retiring)return;
                if(stream.Binding.Metadata.Owner!=peer.Owner)throw new FormatException("Wrong Move owner.");
                if(!PeerInputReady(conn)||!stream.Binding.Move.NetworkUsable)return;
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
            revision=0;var b=peer.Binding;var model=moveWorld.TypedModel(peer.Stream.Identity);if(model==null||!b.Move.NetworkUsable||!model.Ready)return false;
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
            if(stopping||fatalSerialization||NetworkServer.active)return;
            try
            {
                if(p.Kind==4)throw new FormatException("Move downstream kind.");
                if(!bindings.TryGetValue(p.Stream.NetId,out var b)||!MovementStream(b).Equals(p.Stream)||p.Request!=b.MoveRequest||b.Retiring)return;
                if(!b.Shell.HasMove)throw new FormatException("Unexpected Move adapter.");
                if(p.Kind==3)
                {if(b.MoveAwaitingBaseline||p.Revision<b.MoveReceived)return;b.MovePose=p;return;}
                bool owner=b.Metadata.Owner==subscribedSession.LocalOwnerId;
                if(owner!=(p.Kind<=1))throw new FormatException("Move downstream role.");
                string controlDigest;using(var w=NetworkWriterPool.Get()){w.WriteMovePacket(p);using(var hash=System.Security.Cryptography.SHA256.Create())controlDigest=Convert.ToBase64String(hash.ComputeHash(w.ToArray()));}
                if(p.Revision<b.MoveReceived)return;
                if(p.Revision==b.MoveReceived){if(controlDigest!=b.MoveControlDigest)throw new FormatException("Conflicting Move control revision.");return;}
                if(b.MoveReceived!=0&&p.Revision!=b.MoveReceived+1)throw new FormatException("Missing required Move control.");
                if(b.MoveControls.Count>=64||b.MoveControls.Sum(x=>(long)MovePacketBytes(x.Packet))+MovePacketBytes(p)>262144)throw new FormatException("Move pending controls bound.");
                if(b.MoveAwaitingBaseline && owner && (p.Kind!=0||p.Correction.BaselineRequestId!=b.MoveRequest))return;
                var lease=ReserveMove(MovePacketBytes(p));b.MoveControls.Enqueue((p,lease));b.MoveReceived=p.Revision;b.MoveControlDigest=controlDigest;
                if(b.MoveAwaitingBaseline){b.MoveBaseline=p;b.MoveAwaitingBaseline=false;}
            }
            catch(Exception error){ProtocolFailure(error,false);}
        }
        private bool PrepareMove(Binding b,EveEntityLoader loader)
        {
            bool present=loader.GetComponent<PNMove>()!=null;if(present!=b.Shell.HasMove)throw new FormatException("Move required descriptor mismatch.");
            return !present||b.MoveBaseline.HasValue;
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
                if(Time.realtimeSinceStartupAsDouble-b.MoveBaselineStarted>Profile.AdmissionSeconds&&b.MoveAwaitingBaseline)throw new TimeoutException("Movement baseline deadline.");
                if(b.MoveAwaitingBaseline&&Time.realtimeSinceStartupAsDouble-b.LastMoveRequest>.5)RequestMove(b,false);
                if(b.Move==null||b.Candidate.Loader?.IsReady!=true||!b.Move.NetworkUsable)continue;
                if(b.MoveAttached&&b.Move.NetworkRole==PNMoveNetworkRole.Local)
                {b.MoveAttached=false;b.Move.MovementReportPublished-=b.MoveReport;RequestMove(b,true);}
                if(!b.MoveAttached)AttachClientMove(b);
                var life=b.Candidate.Loader.GetComponent<PNStatReplication>()?.View;
                if(life!=null&&b.MoveControls.Count>0&&b.MoveControls.Peek().Packet.Life<life.LifeRevision){RequestMove(b,true);continue;}
                if(life!=null&&b.Move.OwnerMovement?.Mirror!=null&&b.Move.OwnerMovement.Mirror.LifeRevision<life.LifeRevision)b.Move.SetMovementHeld(true);
                if(b.MoveApplying.HasValue&&b.Move.AppliedMovementControl>=(ulong)b.MoveApplying.Value.Revision){b.MoveApplyingLease?.Dispose();b.MoveApplyingLease=null;b.MoveApplying=null;}
                if(!b.MoveApplying.HasValue&&b.MoveControls.Count>0&&MoveLifeCompatible(b,b.MoveControls.Peek().Packet))
                {
                    var next=b.MoveControls.Dequeue();b.MoveApplying=next.Packet;b.MoveApplyingLease=next.Lease;
                    if(next.Packet.Kind<=1)b.Move.StageMovementControl(next.Packet.Mirror,next.Packet.Correction,(ulong)next.Packet.Revision);
                    else b.Move.StageObserverMovement(next.Packet.Pose,(ulong)next.Packet.Revision);
                }
                bool coherent=b.Move.MovementBaselineApplied&&!b.MoveAwaitingBaseline&&(b.Move.OwnerMovement?.Mirror==null||life!=null&&b.Move.OwnerMovement.Mirror.LifeRevision==life.LifeRevision);
                if(!b.Move.MovementHeld&&(clientInterrupted>=0||paused))RequestMove(b,true);
                b.Move.SetMovementHeld(!coherent||!clientInputReady||clientInterrupted>=0||paused);
                if(b.Move.NetworkRole==PNMoveNetworkRole.Observer&&coherent&&!paused&&clientInterrupted<0)
                {
                    if(b.MovePose.HasValue&&b.MovePose.Value.Revision<=(long)b.Move.AppliedMovementControl)
                    {b.Move.ObserverMovement.Receive(b.MovePose.Value.Pose,true);b.MovePose=null;}
                    if(!b.Move.MovementHeld)b.Move.ApplyObserverPose(Math.Max(0,NetworkTime.time-Profile.MoveInterpolationSeconds));
                }
            }
            if(NetworkTime.time-lastMoveTick<Profile.MoveSendSeconds)return;lastMoveTick=NetworkTime.time;
            foreach(var b in bindings.Values.Where(x=>!x.Retiring&&x.LatestMoveReport!=null&&x.Move?.MovementHeld==false))
            {var p=new PNMovePacket{Kind=4,Stream=MovementStream(b),Request=b.MoveRequest,Revision=Math.Max(1,b.MoveReceived),Life=b.Move.OwnerMovement.Mirror.LifeRevision,Report=b.LatestMoveReport};if(!TryMoveSend(null,p,Channels.Unreliable))break;b.LatestMoveReport=null;}
        }
        private void PumpMove()
        {
            if(!NetworkServer.active){PumpClientMove();return;}
            foreach(var b in bindings.Values.Where(x=>!x.Retiring&&x.Move!=null).Concat(awaitingSpawn.Where(x=>x.Move!=null)).Distinct().ToArray())
            {
                if(!b.Move.NetworkUsable)continue;
                if(b.MoveAttached){if(b.Metadata.Owner==0)SetInput(b,hostInterrupted<0);else b.Move.SetMovementHeld(false);continue;}
                if(b.Candidate.Loader.GetComponent<PNStatReplication>()?.CanCaptureReplication!=true)continue;
                moveWorld.TrackTyped(b.Move,b.Metadata.Owner>0);b.MoveAttached=true;b.Move.SetMovementHeld(false);
                if(b.Metadata.Owner==0)SetInput(b,hostInterrupted<0);
            }
            moveWorld.Advance(NetworkTime.time);
            foreach(var b in bindings.Values.Where(x=>!x.Retiring&&x.MoveAttached))if(moveWorld.SamplingFailure(b.Move.NetworkIdentity(b.Metadata.HostSession)) is Exception error)throw new InvalidOperationException("Move capture failed.",error);
            var peers=movePeers.Values.ToArray();if(peers.Length==0)return;
            bool tick=NetworkTime.time-lastMoveTick>=Profile.MoveSendSeconds;if(tick)lastMoveTick=NetworkTime.time;
            for(int i=0;i<peers.Length;i++)
            {
                var peer=peers[(i+moveCursor)%peers.Length];var b=peer.Binding;
                if(b.Retiring||!NetworkServer.connections.ContainsKey(peer.Connection.connectionId)||!b.Shell.netIdentity.observers.ContainsKey(peer.Connection.connectionId)){RemoveMovePeer(peer);continue;}
                var model=moveWorld.TypedModel(peer.Stream.Identity);if(model==null||!b.Move.NetworkUsable)continue;
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
            foreach(var p in movePeers.Values.Where(x=>x.Binding==b).ToArray())RemoveMovePeer(p);
            while(b.MoveControls.Count>0)b.MoveControls.Dequeue().Lease.Dispose();b.MoveApplyingLease?.Dispose();b.MoveApplyingLease=null;
            if(b.Move!=null){b.Move.MovementReportPublished-=b.MoveReport;b.Move.MovementFailed-=b.MoveFailure;if(subscribedSession?.HasAuthority==true&&b.Move.NetworkUsable)moveWorld?.Untrack(b.Move.NetworkIdentity(b.Metadata.HostSession));b.Move.ReleaseNetworkMovement();}
            b.Move=null;
        }
        private void DisposeMove(){foreach(var b in bindings.Values.Concat(awaitingSpawn).ToArray())UnhookMove(b);foreach(var p in movePeers.Values.ToArray())RemoveMovePeer(p);moveWorld?.Dispose();moveWorld=null;}
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
