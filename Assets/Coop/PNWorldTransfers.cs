using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Mirror;
using UnityEngine;

namespace ProjectN.Coop
{
    public struct PNWorldTransferNotice : NetworkMessage
    {
        public Guid Room,Play,Transfer;
        public ulong RoomIncarnation,PlayRevision,Revision;
        public long Entity,Generation;
        public byte Phase;
        public void Validate()
        {if(Room==Guid.Empty||Play==Guid.Empty||Transfer==Guid.Empty||RoomIncarnation==0||PlayRevision==0||Revision==0||Entity<=0||Generation<=0||Phase>3)throw new FormatException("Transfer notice identity/phase.");}
    }
    public static class PNWorldTransferWire
    {
        public static void WriteWorldTransferNotice(this NetworkWriter w,PNWorldTransferNotice v)
        {v.Validate();w.WriteGuid(v.Room);w.WriteGuid(v.Play);w.WriteGuid(v.Transfer);w.WriteULong(v.RoomIncarnation);w.WriteULong(v.PlayRevision);w.WriteULong(v.Revision);w.WriteLong(v.Entity);w.WriteLong(v.Generation);w.WriteByte(v.Phase);}
        public static PNWorldTransferNotice ReadWorldTransferNotice(this NetworkReader r)
        {
            if(r.Remaining>PNWorldWire.ControlBytes)throw new FormatException("Transfer control bound.");
            var v=new PNWorldTransferNotice{Room=r.ReadGuid(),Play=r.ReadGuid(),Transfer=r.ReadGuid(),RoomIncarnation=r.ReadULong(),PlayRevision=r.ReadULong(),Revision=r.ReadULong(),Entity=r.ReadLong(),Generation=r.ReadLong(),Phase=r.ReadByte()};v.Validate();return v;
        }
    }
    public sealed partial class PNNetworkManager : IPNWorldTransferTransport
    {
        public IPNWorldTransferTransportOwner Transfers;
        private sealed class TransferDispatch
        {
            public PNWorldTransferNotice Notice;
            public NetworkConnectionToClient Owner;
            public bool Holding,HoldSent,Ending;
            public PNWorldTransferNotice? Replacement;
            public Guid Destination;
        }
        private sealed class TransferEnvelope
        {public PNWorldTransferNotice Notice;public NetworkConnectionToClient Connection;public IDisposable Lease;}
        private readonly Dictionary<(Guid,long),TransferDispatch> transferDispatches=new Dictionary<(Guid,long),TransferDispatch>();
        private readonly Queue<TransferEnvelope> transferMessages=new Queue<TransferEnvelope>();
        private readonly Dictionary<(long,long),PNWorldTransferNotice> clientTransferHolds=new Dictionary<(long,long),PNWorldTransferNotice>();
        private readonly Dictionary<(long,long),ulong> clientTransferFloors=new Dictionary<(long,long),ulong>();
        private PNTransferTransitionSource transitionSource;
        private readonly Dictionary<(int,Guid),(int Users,bool Created)> transferSubscriptions=new Dictionary<(int,Guid),(int,bool)>();
        private ulong nextTransferRevision;
        int IPNWorldTransferTransport.MaximumParticipants=>(Policy?.MaximumClients??Profile.MaximumClients)+1;
        public int PendingTransferMessages=>transferMessages.Count;
        public void PublishRestoredActor(EveEntityPreparationScheduler.Candidate candidate,PNEntityRequest request)
        {
            if(!NetworkServer.active||!IsPlaying||candidate?.Loader?.IsReady!=true||candidate.Loader.LoadContext.Identity.World!=request.World)throw new InvalidOperationException("Restored host candidate required.");
            var identity=candidate.Loader.LoadContext.Identity;
            var metadata=new PNActorMetadata{HostSession=subscribedSession.SessionId,World=identity.World.InstanceId,WorldIncarnation=identity.World.Incarnation,MembershipRevision=identity.MembershipRevision,ContentSlot=request.ContentSlot,Template=request.Template,SpawnEntry=request.SpawnEntry,Position=request.Position,Owner=identity.OwnerId,Entity=identity.Id.Value,Generation=identity.Generation,Character=request.Character,SceneKey=request.SceneKey,EntityKey=request.EntityKey};
            var binding=new Binding{Candidate=candidate,Metadata=metadata,Token=checked(++nextToken),AwaitingAt=Time.realtimeSinceStartupAsDouble,WorldLease=identity.World.AcquireLease("replication binding",EveWorldLeaseKind.Resident)};
            try{ConfigureMove(binding,candidate.Loader);PublishMove(binding);awaitingSpawn.Add(binding);}catch{CleanupBinding(binding);throw;}
        }
        private Binding TransferBinding(PNWorldTransferContext context)
            =>bindings.Values.Concat(awaitingSpawn).Single(x=>!x.Retiring&&ReferenceEquals(x.Candidate?.Loader,context.Actor));
        private Binding PreviousTransferBinding(PNWorldTransferContext context)=>bindings.Values.Single(x=>!x.Retiring&&ReferenceEquals(x.Candidate?.Loader,context.PreviousActor));
        void IPNWorldTransferTransport.Validate(PNWorldTransferContext context)
        {
            if(!NetworkServer.active||!IsPlaying||context.Actor?.IsReady!=true)throw new InvalidOperationException("Transfer requires host publication.");
            var binding=TransferBinding(context);
            if(binding.Shell==null||binding.Metadata.World!=context.Source.InstanceId||binding.Metadata.MembershipRevision!=context.Actor.LoadContext.Identity.MembershipRevision)throw new InvalidOperationException("Stale transfer presence.");
            if(binding.Metadata.Owner>0&&(binding.OwnerConnection==null||!binding.OwnerConnection.isAuthenticated||!NetworkServer.connections.ContainsKey(binding.OwnerConnection.connectionId)))throw new InvalidOperationException("Transfer owner disconnected.");
        }
        void IPNWorldTransferTransport.Prepare(PNWorldTransferContext context)
        {
            var binding=TransferBinding(context);var owner=binding.OwnerConnection;
            if(binding.Metadata.Owner==0&&PNEntityRoles.IsPlayer(EveRegistrySession.Context.Registry,binding.Metadata.Template))owner=NetworkServer.localConnection;
            if(transferDispatches.Count>=128)throw new InvalidOperationException("Transfer dispatch bound.");
            if(owner!=null)
            {
                var key=(owner.connectionId,context.Destination.InstanceId);bool existed=worldPeers.ContainsKey(key);
                SubscribeWorld(owner,context.Destination);transferSubscriptions.TryGetValue(key,out var users);transferSubscriptions[key]=(users.Users+1,users.Created||!existed);
            }
            var notice=new PNWorldTransferNotice{Room=RoomId,RoomIncarnation=RoomIncarnation,Play=PlaySession,PlayRevision=PlayRevision,Transfer=context.TransferId,Revision=checked(++nextTransferRevision),Entity=binding.Metadata.Entity,Generation=binding.Metadata.Generation};
            transferDispatches.Add((context.TransferId,binding.Metadata.Entity),new TransferDispatch{Notice=notice,Owner=owner,Destination=context.Destination.InstanceId});
        }
        bool IPNWorldTransferTransport.IsLoaded(PNWorldTransferContext context)
        {
            var binding=TransferBinding(context);
            if(binding.Metadata.Owner==0)return true;
            return worldPeers.TryGetValue((binding.OwnerConnection.connectionId,context.Destination.InstanceId),out var entry)&&entry.Loaded&&!entry.Closing;
        }
        void IPNWorldTransferTransport.Hold(PNWorldTransferContext context)
        {
            var binding=TransferBinding(context);var dispatch=transferDispatches[(context.TransferId,binding.Metadata.Entity)];
            if(!dispatch.Holding){QueueTransfer(dispatch,0);dispatch.Holding=true;}SetInput(binding,false);
        }
        bool IPNWorldTransferTransport.IsHeld(PNWorldTransferContext context)
        {var dispatch=transferDispatches[(context.TransferId,context.Entity)];return dispatch.Owner==null||dispatch.Owner is LocalConnectionToClient||dispatch.HoldSent;}
        void IPNWorldTransferTransport.Rebind(PNWorldTransferContext context)
        {
            var previous=PreviousTransferBinding(context);var shell=previous.Shell;var metadata=previous.Metadata;
            CleanupBinding(previous);ForgetWorldActor(previous);bindings.Remove(shell.netId);awaitingSpawn.Remove(previous);
            NetworkServer.Destroy(shell.gameObject);
            var identity=context.Actor.LoadContext.Identity;
            if(context.Replacement)
            {
                var dispatch=transferDispatches[(context.TransferId,context.Entity)];var notice=dispatch.Notice;notice.Entity=identity.Id.Value;notice.Generation=identity.Generation;dispatch.Replacement=notice;QueueTransfer(dispatch,0,notice);
                metadata.Entity=identity.Id.Value;metadata.Generation=identity.Generation;metadata.Character=context.Target.Character;
            }
            metadata.World=identity.World.InstanceId;metadata.WorldIncarnation=identity.World.Incarnation;metadata.MembershipRevision=identity.MembershipRevision;
            metadata.ContentSlot=context.Target.ContentSlot;metadata.SpawnEntry=context.Target.SpawnEntry;metadata.Template=context.Target.Template;
            metadata.SceneKey=context.Target.SceneKey;metadata.EntityKey=context.Target.EntityKey;metadata.Position=context.Target.Position;
            var binding=new Binding{Candidate=context.Replacement?context.ReplacementCandidate:previous.Candidate,Metadata=metadata,Token=checked(++nextToken),OwnerConnection=previous.OwnerConnection,AwaitingAt=Time.realtimeSinceStartupAsDouble,WorldLease=identity.World.AcquireLease("replication binding",EveWorldLeaseKind.Resident)};
            ConfigureMove(binding,context.Actor);PublishMove(binding);awaitingSpawn.Add(binding);
            Factory.Published(context.Actor);
        }
        bool IPNWorldTransferTransport.IsApplied(PNWorldTransferContext context)
        {
            var binding=TransferBinding(context);
            if(binding.Shell==null||binding.Shell.netId==0)return false;
            if(binding.Metadata.Owner==0)return true;
            return binding.OwnerConnection!=null&&worldPeers.TryGetValue((binding.OwnerConnection.connectionId,context.Destination.InstanceId),out var entry)&&entry.Loaded&&entry.InitialReady&&entry.Applied.Contains(binding.Shell.netId);
        }
        void IPNWorldTransferTransport.End(PNWorldTransferContext context,bool committed,bool success)
        {
            var dispatch=transferDispatches.Values.FirstOrDefault(x=>x.Notice.Transfer==context.TransferId&&x.Notice.Entity==context.Entity);
            if(dispatch==null)return;
            if(!dispatch.Ending){byte phase=success?(byte)1:committed?(byte)3:(byte)2;QueueTransfer(dispatch,phase);if(dispatch.Replacement.HasValue)QueueTransfer(dispatch,phase,dispatch.Replacement);dispatch.Ending=true;}
            if(success&&dispatch.Owner!=null&&!bindings.Values.Any(x=>!x.Retiring&&PNEntityRoles.IsPlayer(EveRegistrySession.Context.Registry,x.Metadata.Template)&&x.Metadata.Owner==context.Owner&&x.Metadata.World==context.Source.InstanceId))UnsubscribeWorld(dispatch.Owner,context.Source.InstanceId);
            transferDispatches.Remove((dispatch.Notice.Transfer,dispatch.Notice.Entity));
            if(dispatch.Owner!=null)
            {
                var key=(dispatch.Owner.connectionId,dispatch.Destination);
                if(transferSubscriptions.TryGetValue(key,out var users))
                {
                    if(users.Users>1)transferSubscriptions[key]=(users.Users-1,users.Created);
                    else
                    {
                        transferSubscriptions.Remove(key);
                        if(!success&&!committed&&users.Created&&!bindings.Values.Concat(awaitingSpawn).Any(x=>!x.Retiring&&x.Metadata.Owner==context.Owner&&x.Metadata.World==dispatch.Destination))UnsubscribeWorld(dispatch.Owner,dispatch.Destination);
                    }
                }
            }
            if(context.Actor!=null){var binding=bindings.Values.FirstOrDefault(x=>ReferenceEquals(x.Candidate?.Loader,context.Actor));if(binding!=null)SetInput(binding,success||!committed);}
        }
        private void QueueTransfer(TransferDispatch dispatch,byte phase,PNWorldTransferNotice? replacement=null)
        {
            var connection=dispatch.Owner;if(connection==null||connection is LocalConnectionToClient||!NetworkServer.connections.ContainsKey(connection.connectionId))return;
            if(transferMessages.Count>=256)throw new InvalidOperationException("Transfer control queue bound.");
            var value=replacement??dispatch.Notice;value.Phase=phase;transferMessages.Enqueue(new TransferEnvelope{Connection=connection,Notice=value,Lease=ReserveWorld(192)});
        }
        private void PumpTransferMessages()
        {
            if(!NetworkServer.active)return;
            int count=transferMessages.Count;
            while(count-->0&&transferMessages.Count>0)
            {
                var item=transferMessages.Peek();
                if(NetworkServer.connections.TryGetValue(item.Connection.connectionId,out var current)&&ReferenceEquals(current,item.Connection))
                {
                    if(!TakeSendBudget(current.connectionId,128))return;current.Send(item.Notice);
                    if(item.Notice.Phase==0&&transferDispatches.TryGetValue((item.Notice.Transfer,item.Notice.Entity),out var dispatch))dispatch.HoldSent=true;
                }
                transferMessages.Dequeue();item.Lease.Dispose();
            }
        }
        private void ReceiveTransferNotice(PNWorldTransferNotice value)
        {
            if(NetworkServer.active||!IsPlaying||value.Room!=RoomId||value.RoomIncarnation!=RoomIncarnation||value.Play!=PlaySession||value.PlayRevision!=PlayRevision)return;
            try
            {
                value.Validate();var key=(value.Entity,value.Generation);
                EnsureTransitionSource();transitionSource.OnNotice(value,bindings.Values.Any(b=>b.Metadata.Entity==value.Entity&&b.Metadata.Generation==value.Generation&&b.Metadata.Owner==RoomOwner));
                if(clientTransferFloors.TryGetValue(key,out var floor)&&value.Revision<=floor)return;
                if(value.Phase==0)
                {if(clientTransferHolds.TryGetValue(key,out var current)&&value.Revision<current.Revision)return;if(clientTransferHolds.Count>=128&&!clientTransferHolds.ContainsKey(key))throw new FormatException("Transfer hold bound.");clientTransferHolds[key]=value;}
                else
                {if(clientTransferHolds.TryGetValue(key,out var current)&&current.Revision>value.Revision)return;if(clientTransferFloors.Count>=4096&&!clientTransferFloors.ContainsKey(key))throw new FormatException("Transfer history bound.");clientTransferHolds.Remove(key);clientTransferFloors[key]=value.Revision;}
                foreach(var binding in bindings.Values.Where(x=>x.Metadata.Entity==value.Entity&&x.Metadata.Generation==value.Generation))SetInput(binding,clientInputReady);
            }
            catch(Exception error){ProtocolFailure(error,false);}
        }
        // Registered when the client starts so the play session's transition controller can attach before the first notice arrives.
        // The source lives as long as the client connection (not per play), so a controller created later still finds the same instance.
        public override void OnStopClient(){base.OnStopClient();if(transitionSource!=null){PNTransitionSources.Unregister(transitionSource);transitionSource=null;}}
        private void EnsureTransitionSource(){if(transitionSource==null){transitionSource=new PNTransferTransitionSource();PNTransitionSources.Register(transitionSource);}}
        private void DisposeTransferReplication()
        {
            while(transferMessages.Count>0)transferMessages.Dequeue().Lease.Dispose();transferDispatches.Clear();transferSubscriptions.Clear();clientTransferHolds.Clear();clientTransferFloors.Clear();
            if(Transfers!=null&&ReferenceEquals(Transfers.Transport,this))Transfers.Transport=null;
        }
    }

    // Client-side early signal: the host told this client its actor starts a world transfer (hold phase). The controller ends the cycle from local
    // state (view bound, actor ready, input open); this source only opens it before the local view changes.
    public sealed class PNTransferTransitionSource : IPNTransitionSignalSource
    {
        private IPNTransitionSignalSink sink;
        // Last decision, for diagnostics: why a notice did or did not open a cycle.
        public string LastDecision { get; private set; } = "none";
        public int Opened { get; private set; }
        public void Attach(IPNTransitionSignalSink value) { sink = value; }
        public void Detach() { sink = null; }
        public string History { get; private set; } = "";
        // ownPresence: the notice names this client's own presence (matched through the actor bindings, not through local entity ids).
        public void OnNotice(PNWorldTransferNotice notice, bool ownPresence)
        {
            LastDecision = Decide(notice, ownPresence);
            History = (History + " " + LastDecision).Trim(); if (History.Length > 160) History = History.Substring(History.Length - 160);
        }
        private string Decide(PNWorldTransferNotice notice, bool ownPresence)
        {
            if (notice.Phase != 0) return "phase" + notice.Phase;
            if (!ownPresence) return "other-presence";
            if (sink == null) return "no-sink";
            var local = PNLocalWorldContext.Current;
            if (local == null) return "no-local-view";
            Opened++;
            sink.Begin(PNTransitionReason.Transfer, local.World?.DefinitionId.ToString() ?? "", "", false);
            return "begin";
        }
    }
}
