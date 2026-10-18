using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Mirror;
using UnityEngine;

namespace ProjectN.Coop
{
    public sealed partial class PNNetworkManager
    {
        private sealed class WorldSubscription
        {
            public PNWorldOffer Offer;
            public EveWorldContext World;
            public NetworkConnectionToClient Connection;
            public IDisposable DescriptorLease,WorldLease,FenceLease;
            public bool Loaded,Closing,Sent,AwaitingConfirmation,Confirmed,InitialCaptured,InitialReady;
            public double Started;
            public ulong FenceRevision,ConfirmedRevision;
            public string Digest;
            public PNStateFence[] Targets;
            public readonly Dictionary<(uint,byte),PNStateFence> Incoming=new Dictionary<(uint,byte),PNStateFence>();
            public int Count=-1,SendCursor;
            public readonly HashSet<uint> Applied=new HashSet<uint>();
            public readonly HashSet<uint> Granted=new HashSet<uint>();
            public readonly HashSet<long> InitialEntities=new HashSet<long>();
            public readonly HashSet<long> AppliedEntities=new HashSet<long>();
            public readonly List<IDisposable> TargetLeases=new List<IDisposable>();
        }
        public IPNWorldRuntime Worlds;
        public Func<PNWorldOffer,bool> WorldLoadAllowed = offer=>true;
        private readonly Dictionary<(int,Guid),WorldSubscription> worldPeers=new Dictionary<(int,Guid),WorldSubscription>();
        private readonly Dictionary<Guid,WorldSubscription> clientWorlds=new Dictionary<Guid,WorldSubscription>();
        private readonly Dictionary<Guid,ulong> worldFloors=new Dictionary<Guid,ulong>();
        private readonly Dictionary<Guid,IDisposable> waitingOfferLeases=new Dictionary<Guid,IDisposable>();
        private readonly Dictionary<Guid,PNWorldOffer> waitingWorldOffers=new Dictionary<Guid,PNWorldOffer>();
        private readonly List<WorldSubscription> closingWorlds=new List<WorldSubscription>();
        private ulong nextWorldSubscription;
        private int worldSendCursor;
        private int pendingWorldBytes;
        public int PendingWorldBytes=>pendingWorldBytes;
        public int WorldSubscriptionCount=>NetworkServer.active?worldPeers.Count:clientWorlds.Count;
        public bool ClientWorldReady(Guid world)=>clientWorlds.TryGetValue(world,out var s)&&s.Loaded&&s.Confirmed&&s.InitialReady&&!s.Closing;
        public PNWorldBindingScope CommandWorldScope(long entity,long generation)
        {var binding=bindings.Values.FirstOrDefault(x=>x.Metadata.Entity==entity&&x.Metadata.Generation==generation);return binding==null?default:Scope(binding,NetworkServer.active?NetworkServer.localConnection:null);}
        public PNWorldBindingScope BindingWorldScope(uint netId)=>bindings.TryGetValue(netId,out var binding)?Scope(binding):default;
        public ulong ClientWorldRevision(Guid world)=>clientWorlds.TryGetValue(world,out var s)?s.Offer.Ticket.Subscription:waitingWorldOffers.TryGetValue(world,out var pending)?pending.Ticket.Subscription:0;
        private IDisposable ReserveWorld(int bytes)
        {
            if(!NetworkServer.active&&(long)pendingWorldBytes+bytes>PNWorldWire.DescriptorBytes)throw new InvalidOperationException("Peer world descriptor/fence bound.");
            var lease=ReserveBuffStructure(bytes);pendingWorldBytes=checked(pendingWorldBytes+bytes);
            return new PNBuffRelease(()=>{pendingWorldBytes-=bytes;lease.Dispose();});
        }
        private PNWorldTicket WorldTicket(EveWorldContext world,ulong revision)=>new PNWorldTicket{Room=RoomId,RoomIncarnation=RoomIncarnation,Play=PlaySession,PlayRevision=PlayRevision,World=world.InstanceId,Incarnation=world.Incarnation,Subscription=revision};
        private bool MatchTicket(PNWorldTicket t)=>IsPlaying&&t.Room==RoomId&&t.RoomIncarnation==RoomIncarnation&&t.Play==PlaySession&&t.PlayRevision==PlayRevision;
        private void InitializeWorldReplication()
        {
            if(Worlds==null)throw new InvalidOperationException("A play world runtime is required.");
            Worlds.WorldReady+=WorldBecameReady;Worlds.WorldUnloading+=WorldWillUnload;

        }
        private PNEntityRequest DefaultPlayerRequest(long owner)
            =>Factory is IPNWorldEntityFactory factory&&Worlds.DefaultWorld!=null?factory.Player(owner,Worlds.DefaultWorld,null,null):Factory.Player(owner);
        private bool WorldCanPublish(Binding binding)
            =>subscribedSession!=null&&subscribedSession.Worlds.TryGet(binding.Metadata.World,binding.Metadata.WorldIncarnation,out var world)&&(world.State==EveWorldState.Ready||world.State==EveWorldState.Paused);
        private void WorldFailure(Guid id,Exception error)
        {
            if(NetworkServer.active&&subscribedSession!=null&&subscribedSession.Worlds.Snapshot().FirstOrDefault(x=>x.InstanceId==id) is EveWorldContext world)
            {LastFailure="World "+id+": "+error.Message;world.Fail(error);foreach(var binding in bindings.Values.Where(x=>x.Metadata.World==id))SetInput(binding,false);}
            else ProtocolFailure(error,NetworkServer.active);
        }
        private void WorldBecameReady(EveWorldContext world)
        {
            if(NetworkServer.active&&!(Worlds is IPNWorldPopulation population&&population.RestoredPopulation(world)))EnrollWorldEntities(world);
        }
        public void EnrollWorldEntities(EveWorldContext world)
        {
            if(!NetworkServer.active||!IsPlaying||!ReferenceEquals(world.Session,subscribedSession)||!(Factory is IPNWorldEntityFactory factory))throw new InvalidOperationException("Host world factory required.");
            foreach(var request in factory.SceneEntities(world))EnrollHost(request,null);
        }
        public ulong SubscribeWorld(NetworkConnectionToClient connection,EveWorldContext world)
        {
            if(!NetworkServer.active||!IsPlaying||!(connection?.authenticationData is PNPeer peer)||!connection.isAuthenticated||peer.PlayRevision!=PlayRevision||
                !ReferenceEquals(world?.Session,subscribedSession)||world.State==EveWorldState.Unloading||world.State==EveWorldState.Failed||world.State==EveWorldState.Disposed)
                throw new InvalidOperationException("Host-issued subscription requires an admitted play peer and a current world.");
            var key=(connection.connectionId,world.InstanceId);
            if(worldPeers.TryGetValue(key,out var current))return current.Offer.Ticket.Subscription;
            if(worldPeers.Values.Count(x=>x.Connection==connection)>=8)throw new InvalidOperationException("Peer world descriptor count bound.");
            var entry=new WorldSubscription{World=world,Connection=connection,Started=Time.realtimeSinceStartupAsDouble};
            entry.Offer=new PNWorldOffer{Ticket=WorldTicket(world,checked(++nextWorldSubscription)),Definition=world.DefinitionId.ToString(),ContentHash=world.ContentHash,Active=true,DefaultWorld=ReferenceEquals(world,Worlds.DefaultWorld),Paused=world.State==EveWorldState.Paused};
            try{entry.DescriptorLease=ReserveWorld(512);entry.WorldLease=world.AcquireLease("world subscription");worldPeers.Add(key,entry);}
            catch{entry.DescriptorLease?.Dispose();entry.WorldLease?.Dispose();throw;}
            if(connection is LocalConnectionToClient){entry.Loaded=true;entry.Confirmed=true;entry.InitialReady=true;}else connection.Send(entry.Offer);
            return entry.Offer.Ticket.Subscription;
        }
        public void UnsubscribeWorld(NetworkConnectionToClient connection,Guid world)
        {
            if(!NetworkServer.active)throw new InvalidOperationException("Only the host grants/revokes subscriptions.");
            if(!worldPeers.TryGetValue((connection.connectionId,world),out var entry))return;
            worldPeers.Remove((connection.connectionId,world));entry.Closing=true;
            var offer=entry.Offer;offer.Active=false;if(!(connection is LocalConnectionToClient))connection.Send(offer);
            foreach(var stream in movePeers.Values.Where(x=>x.Connection==connection&&x.Binding.Metadata.World==world).ToArray()){RememberRetiredMove(stream);RemoveMovePeer(stream);}
            foreach(var stream in buffPeers.Values.Where(x=>x.Connection==connection&&x.Binding.Metadata.World==world).ToArray()){RememberRetiredBuff(stream);RemoveBuffPeer(stream);}
            peerFences.Remove(connection.connectionId);
            foreach(var binding in bindings.Values.Where(x=>x.Metadata.World==world).ToArray())if(binding.Shell!=null)NetworkServer.RebuildObservers(binding.Shell.netIdentity,false);
            ReleaseWorldSubscription(entry);
        }
        private void ReleaseWorldSubscription(WorldSubscription entry)
        {entry.FenceLease?.Dispose();entry.FenceLease=null;entry.DescriptorLease?.Dispose();entry.DescriptorLease=null;entry.WorldLease?.Dispose();entry.WorldLease=null;foreach(var lease in entry.TargetLeases)lease.Dispose();entry.TargetLeases.Clear();entry.Incoming.Clear();entry.Targets=null;entry.Applied.Clear();entry.Granted.Clear();entry.InitialEntities.Clear();entry.AppliedEntities.Clear();}
        public bool CanObserveWorld(NetworkConnectionToClient connection,Guid world,ulong incarnation)
            =>IsPlaying&&connection.authenticationData is PNPeer peer&&peer.PlayRevision==PlayRevision&&worldPeers.TryGetValue((connection.connectionId,world),out var entry)&&entry.Loaded&&!entry.Closing&&entry.World.Incarnation==incarnation&&(entry.World.State==EveWorldState.Ready||entry.World.State==EveWorldState.Paused);
        public bool CanObserveActor(NetworkConnectionToClient connection,PNActorShell actor)
            =>CanObserveWorld(connection,actor.Metadata.World,actor.Metadata.WorldIncarnation)&&(connection is LocalConnectionToClient||worldPeers[(connection.connectionId,actor.Metadata.World)].Granted.Contains(actor.netId));
        private void GrantActorBatch(WorldSubscription entry)
        {
            if(!entry.InitialCaptured)
            {
                foreach(var binding in bindings.Values.Concat(awaitingSpawn).Where(x=>x.Metadata.World==entry.World.InstanceId&&!x.Retiring))entry.InitialEntities.Add(binding.Metadata.Entity);
                entry.InitialCaptured=true;
            }
            int pending=worldPeers.Values.Where(x=>x.Connection==entry.Connection).Sum(x=>x.Granted.Count(id=>!x.Applied.Contains(id)));
            int available=Profile.PendingBindings-pending;
            foreach(var binding in bindings.Values.Where(x=>!x.Retiring&&x.Metadata.World==entry.World.InstanceId&&x.Shell!=null&&!entry.Granted.Contains(x.Shell.netId)).OrderBy(x=>x.Metadata.Entity))
            {
                if(available--<=0)break;entry.Granted.Add(binding.Shell.netId);NetworkServer.RebuildObservers(binding.Shell.netIdentity,false);
            }
        }
        private void ForgetWorldActor(Binding binding)
        {
            uint netId=binding.Shell==null?0:binding.Shell.netId;
            foreach(var entry in worldPeers.Values.Concat(clientWorlds.Values).Where(x=>x.Offer.Ticket.World==binding.Metadata.World))
            {entry.Granted.Remove(netId);entry.Applied.Remove(netId);entry.InitialEntities.Remove(binding.Metadata.Entity);entry.AppliedEntities.Remove(binding.Metadata.Entity);}
        }
        private PNWorldBindingScope Scope(Binding binding,NetworkConnectionToClient peer=null)
        {
            var m=binding.Metadata;
            ulong revision=peer==null?binding.WorldSubscriptionRevision:worldPeers.TryGetValue((peer.connectionId,m.World),out var entry)?entry.Offer.Ticket.Subscription:0;
            return new PNWorldBindingScope{World=m.World,Incarnation=m.WorldIncarnation,Membership=m.MembershipRevision,Subscription=revision};
        }
        private bool AuthorizeWorldRequest(NetworkConnectionToClient connection,Binding binding,PNWorldBindingScope scope)
            =>CanObserveWorld(connection,binding.Metadata.World,binding.Metadata.WorldIncarnation)&&Scope(binding,connection).Equals(scope);
        private bool WorldInputAllowed(Binding binding)
        {
            if(binding.Candidate?.Loader?.LoadContext?.CanExecute!=true)return false;
            if(!NetworkServer.active&&(clientTransferHolds.ContainsKey((binding.Metadata.Entity,binding.Metadata.Generation))||binding.Metadata.Owner==subscribedSession.LocalOwnerId&&clientTransferHolds.Count>0))return false;
            if(NetworkServer.active)
            {
                // Host-local simulation does not need a serialized replica of itself.
                if(binding.Metadata.Owner==0)return true;
                var connection=binding.OwnerConnection;
                return connection!=null&&worldPeers.TryGetValue((connection.connectionId,binding.Metadata.World),out var entry)&&entry.Loaded&&entry.InitialReady&&entry.Applied.Contains(binding.Shell.netId);
            }
            return clientWorlds.TryGetValue(binding.Metadata.World,out var client)&&client.Loaded&&client.InitialReady&&!client.Closing&&client.Offer.Ticket.Subscription==binding.WorldSubscriptionRevision&&client.Applied.Contains(binding.Shell.netId);
        }
        private bool IsClientBindingCurrent(Binding binding)=>ClientScope(binding.Metadata,out var entry)&&entry.Offer.Ticket.Subscription==binding.WorldSubscriptionRevision;
        private bool RoomCatchupBinding(Binding binding,NetworkConnectionToClient peer=null)
        {
            if(peer!=null)return worldPeers.TryGetValue((peer.connectionId,binding.Metadata.World),out var host)&&host.InitialReady;
            return NetworkServer.active||clientWorlds.TryGetValue(binding.Metadata.World,out var client)&&client.InitialReady;
        }
        private bool ClientScope(PNActorMetadata metadata,out WorldSubscription entry)
            =>clientWorlds.TryGetValue(metadata.World,out entry)&&entry.Loaded&&!entry.Closing&&entry.World!=null&&entry.World.Incarnation==metadata.WorldIncarnation&&metadata.MembershipRevision>0;
        private void ReceiveWorldOffer(PNWorldOffer offer)
        {
            if(NetworkServer.active||!MatchTicket(offer.Ticket))return;
            try
            {
                offer.Ticket.Validate();
                var id=offer.Ticket.World;if(worldFloors.Count>=4096&&!worldFloors.ContainsKey(id))throw new FormatException("World history bound.");ulong floor=worldFloors.TryGetValue(id,out var value)?value:0;
                if(offer.Ticket.Subscription<floor)return;
                if(!offer.Active)
                {
                    worldFloors[id]=Math.Max(floor,offer.Ticket.Subscription);waitingWorldOffers.Remove(id);if(waitingOfferLeases.TryGetValue(id,out var reservation)){reservation.Dispose();waitingOfferLeases.Remove(id);}
                    if(clientWorlds.TryGetValue(id,out var old)&&old.Offer.Ticket.Subscription<=offer.Ticket.Subscription)CloseClientWorld(old);
                    return;
                }
                if(offer.Ticket.Subscription<=floor)return;
                var definition=subscribedSession.Registry.Get<PNWorldDefinition>().Get(EveId.Parse(offer.Definition));
                if(definition.Id.ToString()!=offer.Definition||definition.ContentHash!=offer.ContentHash)throw new FormatException("World selected definition/content mismatch.");
                if(clientWorlds.TryGetValue(id,out var current))
                {
                    if(current.Offer.Ticket.Equals(offer.Ticket))
                    {
                        if(current.Offer.Definition!=offer.Definition||current.Offer.ContentHash!=offer.ContentHash||current.Offer.DefaultWorld!=offer.DefaultWorld)throw new FormatException("Conflicting world descriptor.");
                        current.Offer=offer;
                        if(current.Loaded)
                        {
                            current.World.SetPaused(offer.Paused);
                            foreach(var binding in bindings.Values.Where(x=>x.Metadata.World==id))SetInput(binding,clientInputReady);
                        }
                        return;
                    }
                    CloseClientWorld(current);
                }
                if(clientWorlds.Count+waitingWorldOffers.Count+closingWorlds.Count>=8&&!waitingWorldOffers.ContainsKey(id))throw new FormatException("World directory bound.");
                if(!waitingOfferLeases.ContainsKey(id))waitingOfferLeases[id]=ReserveWorld(512);
                waitingWorldOffers[id]=offer;
            }
            catch(Exception error){ProtocolFailure(error,false);}
        }
        private void CloseClientWorld(WorldSubscription entry)
        {
            if(entry.Closing)return;entry.Closing=true;clientWorlds.Remove(entry.Offer.Ticket.World);
            worldFloors[entry.Offer.Ticket.World]=entry.Offer.Ticket.Subscription;closingWorlds.Add(entry);
            try{RetireClientWorldBindings(entry.Offer.Ticket.World);}catch(Exception error){LastFailure="World cleanup pending: "+error.Message;}
        }
        private void RetireClientWorldBindings(Guid world)
        {
            foreach(var binding in bindings.Values.Where(x=>x.Metadata.World==world).ToArray()){SetInput(binding,false);binding.Candidate?.Loader?.InvalidateAndHide();RemoveActor(binding.Shell);}
            foreach(var initial in initialStates.Values.Where(x=>x.Shell.Metadata.World==world).ToArray())
            {if(inbox.Contains(initial.Shell.netId,initial.Token))inbox.Release(initial.Shell.netId,initial.Token);initialStates.Remove(initial.Shell.netId);}
        }
        private void ReceiveWorldLoaded(NetworkConnectionToClient connection,PNWorldLoaded value)
        {
            if(!MatchTicket(value.Ticket)||!worldPeers.TryGetValue((connection.connectionId,value.Ticket.World),out var entry)||!entry.Offer.Ticket.Equals(value.Ticket))return;
            if(!value.Success){UnsubscribeWorld(connection,value.Ticket.World);return;}
            entry.Loaded=true;
            foreach(var binding in bindings.Values.Where(x=>x.Metadata.World==value.Ticket.World))NetworkServer.RebuildObservers(binding.Shell.netIdentity,false);
        }
        private void ReceiveWorldFence(PNWorldFence value)
        {
            if(NetworkServer.active||!MatchTicket(value.Ticket)||!clientWorlds.TryGetValue(value.Ticket.World,out var entry)||!entry.Loaded||!entry.Offer.Ticket.Equals(value.Ticket)||value.Revision<entry.FenceRevision)return;
            try
            {
                var target=value.Target;
                if(target.Session!=PlaySession||target.Digest==null||target.Digest.Length!=64||target.Adapter>2||target.NetId==0&&(target.Revision<0||target.Revision>Scheduler.MaximumResidents*3))throw new FormatException("World fence header/count.");
                if(value.Revision>entry.FenceRevision){foreach(var lease in entry.TargetLeases)lease.Dispose();entry.TargetLeases.Clear();entry.Incoming.Clear();entry.Count=-1;entry.FenceRevision=value.Revision;entry.Digest=target.Digest;entry.AwaitingConfirmation=false;}
                if(entry.Digest!=target.Digest)throw new FormatException("Conflicting world fence digest.");
                if(target.NetId==0)entry.Count=(int)target.Revision;
                else
                {
                    if(target.Entity<=0||target.Generation<=0||target.Revision<(target.Adapter==1?0:1)||target.Adapter>0&&target.Nonce==0||target.Scope.World!=value.Ticket.World||target.Scope.Incarnation!=value.Ticket.Incarnation||target.Scope.Subscription!=value.Ticket.Subscription)throw new FormatException("World fence target.");
                    var key=(target.NetId,target.Adapter);
                    if(!entry.Incoming.ContainsKey(key)&&entry.Incoming.Count>=Scheduler.MaximumResidents*3)throw new FormatException("World fence target bound.");
                    if(entry.Incoming.TryGetValue(key,out var old)&&(old.Revision!=target.Revision||old.Entity!=target.Entity||old.Generation!=target.Generation||old.Nonce!=target.Nonce||!old.Scope.Equals(target.Scope)))throw new FormatException("Conflicting world applied target.");
                    if(!entry.Incoming.ContainsKey(key))entry.TargetLeases.Add(ReserveWorld(192));
                    entry.Incoming[key]=target;
                }
            }
            catch(Exception error){ProtocolFailure(error,false);}
        }
        private bool TargetApplied(PNStateFence target)
        {
            if(!bindings.TryGetValue(target.NetId,out var b)||b.Retiring||b.Metadata.Entity!=target.Entity||b.Metadata.Generation!=target.Generation||!Scope(b).Equals(target.Scope)||b.Candidate?.Loader?.IsReady!=true)return false;
            if(target.Adapter==2)return (ulong)b.Token==target.Nonce&&b.Movement.Component?.MovementBaselineApplied==true&&b.Movement.Component.AppliedMovementControl>=(ulong)target.Revision&&!b.Movement.AwaitingBaseline;
            if(target.Adapter==1)return (ulong)b.Token==target.Nonce&&b.Buffs.Component?.HasBaseline==true&&b.Buffs.Component.AppliedRevision>=target.Revision&&b.Buffs.Records.Count==0&&b.Buffs.Assembly==null;
            return b.Candidate.Loader.GetComponent<PNStatReplication>()?.FullViewRevision>=target.Revision;
        }
        private void ReceiveWorldApplied(NetworkConnectionToClient connection,PNWorldApplied value)
        {
            if(value.Confirmed||!MatchTicket(value.Ticket)||!worldPeers.TryGetValue((connection.connectionId,value.Ticket.World),out var entry)||!entry.Offer.Ticket.Equals(value.Ticket)||!entry.Sent||entry.FenceRevision!=value.Revision||entry.Digest!=value.Digest||entry.Digest!=BindingDigest(connection,value.Ticket.World))return;
            entry.Applied.Clear();entry.AppliedEntities.Clear();foreach(var target in entry.Targets.Where(x=>x.NetId!=0)){entry.Applied.Add(target.NetId);entry.AppliedEntities.Add(target.Entity);}
            entry.InitialReady=entry.InitialCaptured&&entry.InitialEntities.IsSubsetOf(entry.AppliedEntities);
            entry.Confirmed=true;entry.ConfirmedRevision=value.Revision;value.Confirmed=true;value.InitialReady=entry.InitialReady;connection.Send(value);
        }
        private void ReceiveWorldConfirmation(PNWorldApplied value)
        {
            if(!value.Confirmed||NetworkServer.active||!MatchTicket(value.Ticket)||!clientWorlds.TryGetValue(value.Ticket.World,out var entry)||!entry.Offer.Ticket.Equals(value.Ticket)||entry.FenceRevision!=value.Revision||entry.Digest!=value.Digest||!entry.AwaitingConfirmation||entry.Digest!=BindingDigest(null,value.Ticket.World)||!entry.Incoming.Values.All(TargetApplied))return;
            entry.Applied.Clear();foreach(var target in entry.Incoming.Values)entry.Applied.Add(target.NetId);entry.Confirmed=true;entry.InitialReady=value.InitialReady;entry.ConfirmedRevision=value.Revision;entry.AwaitingConfirmation=false;
            foreach(var binding in bindings.Values.Where(x=>x.Metadata.World==value.Ticket.World))SetInput(binding,clientInputReady);
        }
        private void SendWorldFence(WorldSubscription entry)
        {
            var connection=entry.Connection;var world=entry.World.InstanceId;
            string digest=BindingDigest(connection,world);
            if(entry.Targets==null||entry.Digest!=digest)
            {
                var actors=bindings.Values.Where(x=>!x.Retiring&&x.Metadata.World==world&&x.Shell.netIdentity.observers.ContainsKey(connection.connectionId)).OrderBy(x=>x.Metadata.Entity).ToArray();
                foreach(var b in actors)
                {if(!CaptureStat(b)||b.Buffs.Component!=null&&(!buffPeers.TryGetValue((connection.connectionId,b.Shell.netId),out var bp)||bp.Requested)||b.Movement.Component!=null&&(!movePeers.TryGetValue((connection.connectionId,b.Shell.netId),out var mp)||mp.NeedsBaseline))return;}
                var targets=new List<PNStateFence>();
                foreach(var b in actors)
                {
                    var header=new PNStateFence{Session=PlaySession,NetId=b.Shell.netId,Entity=b.Metadata.Entity,Generation=b.Metadata.Generation,Digest=digest,Scope=Scope(b,connection)};
                    if(b.Stats.Component!=null){var t=header;t.Revision=b.Shell.State.Revision;targets.Add(t);}
                    if(b.Buffs.Component!=null){if(!QueueBuffBaseline(buffPeers[(connection.connectionId,b.Shell.netId)],out long revision))return;var t=header;t.Adapter=1;t.Revision=revision;t.Nonce=buffPeers[(connection.connectionId,b.Shell.netId)].Stream.Nonce;targets.Add(t);}
                    if(b.Movement.Component!=null){if(!QueueMoveBaseline(movePeers[(connection.connectionId,b.Shell.netId)],out long revision))return;var t=header;t.Adapter=2;t.Revision=revision;t.Nonce=movePeers[(connection.connectionId,b.Shell.netId)].Stream.Nonce;targets.Add(t);}
                }
                targets.Add(new PNStateFence{Session=PlaySession,Revision=targets.Count,Digest=digest});
                int bytes=checked(targets.Count*192);
                if(bytes+worldPeers.Values.Where(x=>x.Connection==connection&&x!=entry).Sum(x=>(x.Targets?.Length??0)*192+512)>PNWorldWire.DescriptorBytes)throw new InvalidOperationException("World descriptor/fence peer bound.");
                entry.FenceLease?.Dispose();entry.FenceLease=ReserveWorld(bytes);entry.Targets=targets.ToArray();entry.Digest=digest;entry.FenceRevision=checked(entry.FenceRevision+1);entry.Sent=false;entry.SendCursor=0;entry.Confirmed=false;entry.Started=Time.realtimeSinceStartupAsDouble;
            }
            if(entry.Sent)return;
            foreach(var target in entry.Targets)
            {if(target.Adapter==1&&(!buffPeers.TryGetValue((connection.connectionId,target.NetId),out var bp)||bp.SentRevision<target.Revision||bp.Packets.Any(x=>x.Kind==0)))return;if(target.Adapter==2&&(!movePeers.TryGetValue((connection.connectionId,target.NetId),out var mp)||mp.SentRevision<target.Revision))return;}
            while(entry.SendCursor<entry.Targets.Length)
            {
                var packet=new PNWorldFence{Ticket=entry.Offer.Ticket,Revision=entry.FenceRevision,Target=entry.Targets[entry.SendCursor]};
                using(var writer=NetworkWriterPool.Get())
                {writer.WriteWorldFence(packet);if(writer.Position>PNWorldWire.ControlBytes)throw new FormatException("World control size.");if(!TakeSendBudget(connection.connectionId,writer.Position+16))return;}
                connection.Send(packet);entry.SendCursor++;
            }
            entry.Sent=true;
        }
        private void PumpWorldReplication()
        {
            if(!IsPlaying||stopping)return;
            PumpTransferMessages();
            if(NetworkServer.active)
            {
                foreach(var world in subscribedSession.Worlds.Snapshot().Where(x=>x.State==EveWorldState.Failed||x.State==EveWorldState.Unloading).ToArray())
                    try{Worlds.Unload(world);}catch(Exception error){LastFailure="World cleanup pending: "+error.Message;}
                var peers=worldPeers.Values.ToArray();
                for(int i=0;i<peers.Length;i++)
                {
                    var entry=peers[(i+worldSendCursor)%peers.Length];
                    if(!NetworkServer.connections.ContainsKey(entry.Connection.connectionId)||entry.World.State==EveWorldState.Failed||entry.World.State==EveWorldState.Unloading||entry.World.State==EveWorldState.Disposed){UnsubscribeWorld(entry.Connection,entry.World.InstanceId);continue;}
                    bool pausedWorld=entry.World.State==EveWorldState.Paused;
                    if(pausedWorld!=entry.Offer.Paused){var offer=entry.Offer;offer.Paused=pausedWorld;entry.Offer=offer;if(!(entry.Connection is LocalConnectionToClient))entry.Connection.Send(offer);}
                    if(entry.Connection is LocalConnectionToClient)continue;
                    if((!entry.Loaded||!entry.Confirmed)&&Time.realtimeSinceStartupAsDouble-entry.Started>60){UnsubscribeWorld(entry.Connection,entry.World.InstanceId);continue;}
                    if(entry.Loaded&&(entry.World.State==EveWorldState.Ready||entry.World.State==EveWorldState.Paused))
                        try{GrantActorBatch(entry);SendWorldFence(entry);}catch(Exception error){LastFailure="World subscription failed: "+error.Message;UnsubscribeWorld(entry.Connection,entry.World.InstanceId);}
                }
                worldSendCursor=peers.Length==0?0:(worldSendCursor+1)%peers.Length;return;
            }
            foreach(var entry in closingWorlds.ToArray())
            {
                try{RetireClientWorldBindings(entry.Offer.Ticket.World);if(entry.World!=null&&!Worlds.Unload(entry.World))continue;ReleaseWorldSubscription(entry);closingWorlds.Remove(entry);}catch(Exception error){LastFailure="World cleanup pending: "+error.Message;}
            }
            foreach(var offer in waitingWorldOffers.Values.ToArray())
            {
                if(closingWorlds.Any(x=>x.Offer.Ticket.World==offer.Ticket.World)||!WorldLoadAllowed(offer))continue;
                var entry=new WorldSubscription{Offer=offer,Started=Time.realtimeSinceStartupAsDouble};
                entry.DescriptorLease=waitingOfferLeases[offer.Ticket.World];waitingOfferLeases.Remove(offer.Ticket.World);clientWorlds.Add(offer.Ticket.World,entry);waitingWorldOffers.Remove(offer.Ticket.World);
                try{entry.World=Worlds.CreateReplica(EveId.Parse(offer.Definition),offer.Ticket.World,offer.Ticket.Incarnation,offer.Ticket.Subscription,offer.DefaultWorld);}
                catch(Exception error){if(subscribedSession.Worlds.TryGet(offer.Ticket.World,offer.Ticket.Incarnation,out var partial))entry.World=partial;LastFailure="World load failed: "+error.Message;NetworkClient.Send(new PNWorldLoaded{Ticket=offer.Ticket,Success=false});CloseClientWorld(entry);}
            }
            foreach(var entry in clientWorlds.Values.ToArray())
            {
                if(entry.World==null)continue;
                if(entry.World.State==EveWorldState.Failed||!entry.Loaded&&Time.realtimeSinceStartupAsDouble-entry.Started>60){NetworkClient.Send(new PNWorldLoaded{Ticket=entry.Offer.Ticket,Success=false});CloseClientWorld(entry);continue;}
                if(!entry.Loaded&&entry.World.State==EveWorldState.Ready){entry.World.SetPaused(entry.Offer.Paused);entry.Loaded=true;NetworkClient.Send(new PNWorldLoaded{Ticket=entry.Offer.Ticket,Success=true});}
                if(!entry.Loaded||entry.AwaitingConfirmation||entry.ConfirmedRevision==entry.FenceRevision||entry.Count<0||entry.Count!=entry.Incoming.Count||entry.Digest!=BindingDigest(null,entry.World.InstanceId)||!entry.Incoming.Values.All(TargetApplied))continue;
                entry.AwaitingConfirmation=true;NetworkClient.Send(new PNWorldApplied{Ticket=entry.Offer.Ticket,Revision=entry.FenceRevision,Digest=entry.Digest});
            }
        }
        private void WorldWillUnload(EveWorldContext world)
        {
            if(NetworkServer.active)
            {
                foreach(var entry in worldPeers.Values.Where(x=>ReferenceEquals(x.World,world)).ToArray())UnsubscribeWorld(entry.Connection,world.InstanceId);
                foreach(var b in bindings.Values.Concat(awaitingSpawn).Where(x=>x.Metadata.World==world.InstanceId).ToArray())
                {if(b.Shell!=null&&b.Shell.netId!=0)NetworkServer.Destroy(b.Shell.gameObject);else{CleanupBinding(b);Scheduler.Retire(b.Candidate);awaitingSpawn.Remove(b);}}
            }
            else RetireClientWorldBindings(world.InstanceId);
        }
        private void DisposeWorldReplication()
        {
            DisposeTransferReplication();ClearRetiredStreams();
            if(Worlds!=null){Worlds.WorldReady-=WorldBecameReady;Worlds.WorldUnloading-=WorldWillUnload;}
            foreach(var entry in worldPeers.Values.Concat(clientWorlds.Values).Concat(closingWorlds).ToArray())ReleaseWorldSubscription(entry);
            worldPeers.Clear();clientWorlds.Clear();closingWorlds.Clear();worldFloors.Clear();waitingWorldOffers.Clear();foreach(var lease in waitingOfferLeases.Values)lease.Dispose();waitingOfferLeases.Clear();
        }
    }
}
