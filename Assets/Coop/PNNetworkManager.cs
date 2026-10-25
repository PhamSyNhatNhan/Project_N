using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Mirror;
using UnityEngine;
namespace ProjectN.Coop
{
    public sealed partial class PNNetworkManager : NetworkManager
    {
        // The common binding owns identity/lifecycle; adapter state owns its resources.
        private sealed class Binding
        {
            public PNActorShell Shell;
            public EveEntityPreparationScheduler.Candidate Candidate;
            public PNActorMetadata Metadata;
            public long Token;
            public ulong WorldSubscriptionRevision;
            public IDisposable WorldLease;
            public bool Retiring;
            public double RetiringAt;
            public NetworkConnectionToClient OwnerConnection;
            public double AwaitingAt;
            public readonly StatBindingState Stats=new StatBindingState();
            public readonly BuffBindingState Buffs=new BuffBindingState();
            public readonly MoveBindingState Movement=new MoveBindingState();
        }
        private EveSessionContext subscribedSession;
        private double lastStatCapture;
        private readonly Dictionary<uint,(PNActorShell Shell,long Token)> initialStates=new Dictionary<uint,(PNActorShell,long)>();
        public void InitializeReplication()
        {
            if(subscribedSession!=null)return;
            subscribedSession=EveRegistrySession.Context;
            // Retain required adapter cleanup in the session even if the room closes
            // or a binding cleanup fails before normal entity retirement begins.
            subscribedSession.RegisterCleanup(DisposeReplication);
            subscribedSession.ConnectionFailed+=SessionFailed;
            InitializeStat();
            NetworkLoop.OnLateUpdate+=PumpReplication;
            InitializeBuff();
            InitializeMove();
            InitializeWorldReplication();
            if(Transfers==null)throw new InvalidOperationException("World transfer service is not attached.");
            Transfers.Transport=this;
        }
        private void DisposeReplication()
        {
            NetworkLoop.OnLateUpdate-=PumpReplication;
            if(subscribedSession!=null)subscribedSession.ConnectionFailed-=SessionFailed;
            var errors=new List<Exception>();
            foreach(var binding in bindings.Values.Concat(awaitingSpawn).Distinct().ToArray())
            {
                CleanupStep(errors,()=>CleanupBinding(binding));
                if(binding.Shell!=null&&binding.Shell.netId==0)Destroy(binding.Shell.gameObject);
            }
            // Keep adapter services reachable if binding cleanup needs a retry.
            if(errors.Count!=0)throw new AggregateException("Binding cleanup failed.",errors);
            DisposeWorldReplication();DisposeBuff();DisposeMove();
            foreach(var value in initialStates)if(inbox.Contains(value.Key,value.Value.Token))inbox.Release(value.Key,value.Value.Token);
            initialStates.Clear();peerFences.Clear();clientFence.Clear();stateAdapters.Clear();subscribedSession=null;
        }
        private void SessionFailed(string code,string message)=>ProtocolFailure(new InvalidOperationException(code+": "+message),false);
        private void PublishBindingStages(Binding binding)
        {
            PublishBuffStages(binding);
            PublishStatStages(binding);
        }
        private void CleanupBinding(Binding binding)
        {
            var errors=new List<Exception>();
            CleanupStep(errors,()=>UnhookMove(binding));
            CleanupStep(errors,()=>UnhookBuff(binding));
            CleanupStep(errors,()=>UnhookStat(binding));
            // Reverse publication staging: Stat stages followed the Buff baseline.
            CleanupStep(errors,()=>RollbackStatStages(binding));
            CleanupStep(errors,()=>RollbackBuffStages(binding));
            if(errors.Count!=0)throw new AggregateException("Adapter binding cleanup failed.",errors);
            binding.WorldLease?.Dispose();binding.WorldLease=null;
        }
        private static void CleanupStep(List<Exception> errors,Action cleanup)
        {try{cleanup();}catch(Exception error){errors.Add(error);}}
        private static void PublishOwnedStages(List<EveIReplicaStage> stages)
        {
            while(stages.Count>0){var stage=stages[0];stage.Published();stage.Dispose();stages.RemoveAt(0);}
        }
        private static void DisposeOwnedStages(List<EveIReplicaStage> stages)
        {
            var errors=new List<Exception>();
            for(int i=stages.Count-1;i>=0;i--)
            {try{stages[i].Dispose();stages.RemoveAt(i);}catch(Exception error){errors.Add(error);}}
            if(errors.Count!=0)throw new AggregateException("Replica stage cleanup failed.",errors);
        }
        private void PumpReplication()
        {
            if(stopping||!IsPlaying)return;
            if(fatalSerialization){StopSession();return;}
            try{PumpMove();PumpBuff();}catch(Exception error){ProtocolFailure(error,NetworkServer.active);return;}
            if(!NetworkServer.active)return;
            try
            {
                // Spawn deferred actors only at drained capture boundaries, within prepare timeout.
                foreach(var binding in awaitingSpawn.ToArray())
                {
                    if(binding.Candidate?.Loader==null||!binding.Candidate.Loader.IsReady||binding.Shell!=null||!WorldCanPublish(binding))continue;
                    if(Time.realtimeSinceStartupAsDouble-binding.AwaitingAt>Scheduler.TimeoutMilliseconds/1000d)throw new TimeoutException("Initial Stat capture timed out.");
                    var stat=binding.Candidate.Loader.GetComponent<PNStatReplication>();if(stat!=null&&!stat.CanCaptureReplication){var buffs=binding.Candidate.Loader.GetComponent<PNBuffManager>();if(buffs!=null&&buffs.StateCaptureFaulted)throw new InvalidOperationException("Initial Stat mutation source failed.");continue;}
                    var root=CreateShell<PNActorShell>("Actor "+binding.Metadata.Entity);binding.Shell=root.GetComponent<PNActorShell>();binding.Shell.Metadata=binding.Metadata;
                    HookStat(binding);HookBuff(binding);binding.Shell.HasBuff=binding.Buffs.Component!=null;binding.Shell.HasMove=binding.Movement.Component!=null;CaptureStat(binding);
                    NetworkServer.Spawn(root,PNActorShell.Asset,binding.OwnerConnection);bindings.Add(binding.Shell.netId,binding);awaitingSpawn.Remove(binding);
                }
                if(Time.realtimeSinceStartupAsDouble-lastStatCapture<.05)return;lastStatCapture=Time.realtimeSinceStartupAsDouble;
                foreach(var binding in bindings.Values.Where(x=>!x.Retiring&&WorldCanPublish(x)))
                    try{CaptureStat(binding);}catch(Exception error){WorldFailure(binding.Metadata.World,error);}
            }
            catch(Exception error){ProtocolFailure(error,true);StopSession();}
        }
        public EveRuntime Runtime;
        public IPNEntityFactory Factory;
        public EveEntityPreparationScheduler Scheduler;
        public PNNetworkProfile Profile=new PNNetworkProfile();
        public IPNSessionPolicy Policy;
        public string ModeId="roguelite",LastFailure;
        public double AdmittedAt;
        public int PublishedCount=>bindings.Values.Count(x=>x.Candidate.Loader!=null&&x.Candidate.Loader.IsReady);
        private readonly Dictionary<uint,Binding> bindings=new Dictionary<uint,Binding>();
        private readonly List<Binding> awaitingSpawn=new List<Binding>();
        private long nextOwner,nextToken;
        private EveBindingInbox inbox=new EveBindingInbox();
        public Action<EveEntityLoader,byte[]> ApplyLatestState,ApplyRequiredEvent;
        public long BindingToken(uint netId)=>bindings.TryGetValue(netId,out var value)?value.Token:0;
        public bool BufferRequired(uint netId,long token,byte[] payload,bool latest)
        {
            if(stopping||fatalSerialization||!EveRegistrySession.IsReady||EveRegistrySession.Context.Role!=EveSessionRole.Client)return false;
            if(!bindings.TryGetValue(netId,out var binding)||binding.Token!=token)return false;
            try
            {
                var apply=latest?ApplyLatestState:ApplyRequiredEvent;
                if(apply==null)throw new InvalidOperationException("Required replication adapter is not installed.");
                if(binding.Candidate.Loader!=null&&binding.Candidate.Loader.IsReady)apply(binding.Candidate.Loader,payload);
                else if(latest)inbox.Latest(netId,token,payload);else inbox.Required(netId,token,payload);
                return true;
            }
            catch(Exception error){ProtocolFailure(error,false);return false;}
        }
        private bool stopping,transportStopped,teardownRunning;
        private bool fatalSerialization;
        public void ProtocolFailure(Exception error,bool authoritative)
        {
            if(stopping||!IsPlaying||fatalSerialization)return;LastFailure=error.Message;fatalSerialization=true;
            foreach(var binding in bindings.Values)SetInput(binding,false);
            if(!authoritative&&EveRegistrySession.IsReady&&EveRegistrySession.Context.IsActive)EveRegistrySession.Context.FailConnection("REQUIRED_ADAPTER",error.Message);
        }
        public bool HasEnded=>stopping;
        public bool IsTeardownComplete=>transportStopped&&!playCleanupPending&&EveRegistrySession.State==EveSessionState.Stopped;
        public event Action TeardownCompleted;
        private bool notifiedTeardown;
        private void NotifyTeardown()
        {if(notifiedTeardown||!IsTeardownComplete)return;notifiedTeardown=true;TeardownCompleted?.Invoke();TeardownCompleted=null;}
        public Func<bool> RequiredStateSettled=()=>true;
        public long AllocateOwner()=>checked(++nextOwner);
        public void RouteIntent(NetworkConnectionToClient connection,Guid session,long entity,long generation,long sequence,string command,string payload,PNWorldBindingScope scope)
        {
            if(stopping||!IsPlaying||fatalSerialization||session!=PlaySession)return;
            try
            {
                if(command==null||command.Length>128||payload==null||System.Text.Encoding.UTF8.GetByteCount(payload)>Profile.CommandBytes)throw new InvalidOperationException("Command payload exceeds bounds.");
                if(IsRetiredCommand(connection,session,entity,generation,scope))return;
                if(!PeerInputReady(connection)||!(connection.authenticationData is PNPeer peer)||peer.Owner<=0)throw new InvalidOperationException("Command input is not admitted/ready.");
                var actor=bindings.Values.FirstOrDefault(b=>b.Metadata.Entity==entity&&b.Metadata.Generation==generation);
                if(actor!=null&&actor.Metadata.Owner==peer.Owner&&AuthorizeWorldRequest(connection,actor,scope)&&actor.Candidate?.Loader?.LoadContext?.Identity.IsGameplayHeld==true)return;
                if(actor==null||!AuthorizeWorldRequest(connection,actor,scope)||!WorldInputAllowed(actor))throw new InvalidOperationException("Command world binding is not applied/admitted.");
                var context=EveRegistrySession.Context;
                if(session!=context.SessionId)return;
                if(!context.Entities.TryGet(new EveEntityId(entity),generation,out _))throw new InvalidOperationException("Stale command binding.");
                context.Admission.HandleCommand(peer.Owner,new EveEntityCommand(session,entity,sequence,command,payload));
            }
            catch(Exception error){LastFailure=error.Message;connection.Disconnect();}
        }
        public override void OnStartServer()
        {
            RoomId=Guid.NewGuid();RoomIncarnation=1;
            var interest=GetComponent<PNWorldInterestManagement>()??gameObject.AddComponent<PNWorldInterestManagement>();interest.Manager=this;
            NetworkServer.RegisterHandler<PNWorldLoaded>(ReceiveWorldLoaded);
            NetworkServer.RegisterHandler<PNWorldApplied>(ReceiveWorldApplied);
            NetworkServer.RegisterHandler<PNPlayReady>(ReceivePlayReady);
            NetworkServer.RegisterHandler<PNHeartbeat>(ReceiveHeartbeat);
            NetworkServer.RegisterHandler<PNResume>(ReceiveResume);
            ResetLobby();RegisterLobbyServer();
        }
        public override void OnStartClient()
        {
            inbox=new EveBindingInbox {MaximumBindings=Profile.PendingBindings,MaximumStateBytes=Profile.LatestStateBytes,MaximumEvents=Profile.RequiredEvents,MaximumEventBytes=Profile.RequiredEventBytes,MaximumTotalEvents=Profile.PendingEvents,MaximumTotalBytes=Profile.PendingBytes};
            NetworkClient.RegisterHandler<PNPlayStatus>(ReceivePlayStatus);
            clientStartedAt=Time.realtimeSinceStartupAsDouble;
            NetworkClient.RegisterHandler<PNWorldOffer>(ReceiveWorldOffer);
            NetworkClient.RegisterHandler<PNWorldFence>(ReceiveWorldFence);
            NetworkClient.RegisterHandler<PNWorldApplied>(ReceiveWorldConfirmation);
            NetworkClient.RegisterHandler<PNWorldTransferNotice>(ReceiveTransferNotice);
            EnsureTransitionSource();
            NetworkClient.RegisterSpawnHandler(PNSessionShell.Asset,_=>CreateShell<PNSessionShell>("Session shell"),DestroyShell);
            NetworkClient.RegisterSpawnHandler(PNActorShell.Asset,_=>CreateShell<PNActorShell>("Actor shell"),DestroyShell);
            NetworkClient.RegisterHandler<PNResume>(ReceiveResumeClient);
            NetworkClient.RegisterHandler<PNStatNotice>(ReceiveStatMessage);
            NetworkClient.RegisterHandler<PNStateFence>(ReceiveFence);
            RegisterLobbyClient();
        }
        public static GameObject CreateShell<T>(string name) where T:NetworkBehaviour
        {var root=new GameObject(name);root.SetActive(false);root.AddComponent<NetworkIdentity>();root.AddComponent<PNRoomResource>();root.AddComponent<T>();root.SetActive(true);return root;}
        private static void DestroyShell(GameObject shell)=>Destroy(shell);
        public override void OnClientConnect()
        {
            if(NetworkServer.active){NetworkClient.Ready();roomShellRequested=true;NetworkClient.AddPlayer();}
        }
        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            if(!(conn.authenticationData is PNPeer peer)){conn.Disconnect();return;}
            peer.WantsRoomShell=true;
            // A lobby AddPlayer may already be in flight when the host offers a play.
            if(IsPlaying&&peer.PlayRevision!=PlayRevision)return;
            if(conn.identity!=null)return;
            var root=CreateShell<PNSessionShell>("Session owner "+peer.Owner);root.GetComponent<PNSessionShell>().Owner=peer.Owner;
            if(!NetworkServer.AddPlayerForConnection(conn,root,PNSessionShell.Asset)){Destroy(root);conn.Disconnect();return;}
            if(IsPlaying&&peer.Owner>0&&peer.SelectPlayer&&peer.EnrolledRevision!=PlayRevision)
            {peer.EnrolledRevision=PlayRevision;EnrollHost(DefaultPlayerRequest(peer.Owner),conn);}
        }
        private void EnrollHost(PNEntityRequest request,NetworkConnectionToClient owner)
        {
            if(request.World==null||request.WorldIncarnation!=request.World.Incarnation)throw new InvalidOperationException("Enrollment requires explicit current world membership.");
            var metadata=new PNActorMetadata {World=request.World.InstanceId,WorldIncarnation=request.World.Incarnation,MembershipRevision=1,ContentSlot=request.ContentSlot,SpawnEntry=request.SpawnEntry,Template=request.Template,Position=request.Position,HostSession=EveRegistrySession.Context.SessionId,Owner=request.Owner,Character=request.Character,SceneKey=request.SceneKey,EntityKey=request.EntityKey,Generation=1};
            metadata.Entity=EveRegistrySession.Context.Entities.AllocateHostId().Value;
            var binding=new Binding {Metadata=metadata,Token=checked(++nextToken),OwnerConnection=owner,AwaitingAt=Time.realtimeSinceStartupAsDouble,WorldLease=request.World.AcquireLease("replication binding",EveWorldLeaseKind.Resident)};
            awaitingSpawn.Add(binding);
            binding.Candidate=PNEntitySpawner.Enqueue(Scheduler,Factory,request,loader=>
            {
                ConfigureMove(binding,loader);loader.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(metadata.Entity),EveId.Parse(metadata.Character),metadata.Owner,metadata.Generation);
            },loader=>
            {
                if(stopping||!IsPlaying||fatalSerialization)return;
                Factory.Published(loader);
                PublishMove(binding);
            },error=>FailActor(binding,error),factoryPublished:false);
        }
        public void ReceiveActor(PNActorShell shell)
        {
            if(stopping||!IsPlaying||fatalSerialization)return;
            try
            {
                var session=EveRegistrySession.Context;var metadata=shell.Metadata;
                if(metadata.Character==null||metadata.Character.Length>128||string.IsNullOrWhiteSpace(metadata.SceneKey)||metadata.SceneKey.Length>128||metadata.EntityKey==null||metadata.EntityKey.Length>128)throw new InvalidOperationException("Invalid bounded actor metadata.");
                if(session.Role!=EveSessionRole.Client||session.Admission.AdmittedHostSession!=metadata.HostSession||metadata.Entity<=0||metadata.Generation<=0||metadata.Owner<0||shell.netId==0||bindings.ContainsKey(shell.netId))throw new InvalidOperationException("Invalid actor binding.");
                if(bindings.Values.Any(x=>x.Metadata.Entity==metadata.Entity))throw new InvalidOperationException("Duplicate actor entity identity.");
                if(!ClientScope(metadata,out var worldSubscription))throw new FormatException("Actor initial state has no loaded authorized world.");
                session.Registry.Characters.Get(EveId.Parse(metadata.Character));
                var request=new PNEntityRequest {Template=metadata.Template,Owner=metadata.Owner,Character=metadata.Character,SceneKey=metadata.SceneKey,EntityKey=metadata.EntityKey,World=worldSubscription.World,WorldIncarnation=metadata.WorldIncarnation,MembershipRevision=metadata.MembershipRevision,ContentSlot=metadata.ContentSlot,SpawnEntry=metadata.SpawnEntry,Position=metadata.Position};
                // P1 actor metadata positions are initial content/spawn positions; Move replication follows in P4.
                // World/slot/template and initial position are part of the immutable presence.
                if(!initialStates.TryGetValue(shell.netId,out var initial)||!ReferenceEquals(initial.Shell,shell))throw new FormatException("Actor lacks initial required state reservation.");
                var binding=new Binding {Shell=shell,Metadata=metadata,Token=initial.Token,WorldSubscriptionRevision=worldSubscription.Offer.Ticket.Subscription,WorldLease=request.World.AcquireLease("replication binding",EveWorldLeaseKind.Resident)};bindings.Add(shell.netId,binding);initialStates.Remove(shell.netId);
                binding.Candidate=PNEntitySpawner.Enqueue(Scheduler,Factory,request,loader=>
                {ConfigureMove(binding,loader);loader.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(metadata.Entity),EveId.Parse(metadata.Character),metadata.Owner,metadata.Generation);},loader=>
                {PublishBindingStages(binding);PublishBuff(binding);Factory.Published(loader);PublishMove(binding);inbox.Release(shell.netId,binding.Token);SetInput(binding,clientInputReady);},error=>FailActor(binding,error),loader=>{CommitBuff(binding);StageStat(binding,loader);inbox.Apply(shell.netId,binding.Token,bytes=>ApplyLatestState(loader,bytes),bytes=>ApplyRequiredEvent(loader,bytes));},loader=>PrepareBuff(binding,loader)&&PrepareMove(binding,loader),()=>!session.Entities.IsReserved(new EveEntityId(metadata.Entity)),factoryPublished:false);
                if(shell.HasMove)RequestMove(binding,true);
                if(shell.HasBuff)NetworkClient.Send(new PNBuffBaselineRequest{Stream=Stream(binding)});
            }
            catch(Exception error){ProtocolFailure(error,false);}
        }
        private void FailActor(Binding binding,Exception error)
        {
            if(stopping||!IsPlaying||fatalSerialization)return;
            Debug.LogException(error,this);
            LastFailure=error.Message;
            if(EveRegistrySession.IsReady&&EveRegistrySession.Context.Role==EveSessionRole.Client){ProtocolFailure(error,false);return;}
            if(binding.Candidate!=null)Scheduler.Retire(binding.Candidate);
            try
            {
                if(binding.Shell!=null){if(binding.Shell.netId!=0)NetworkServer.Destroy(binding.Shell.gameObject);else Destroy(binding.Shell.gameObject);}
                CleanupBinding(binding);
            }
            catch(Exception cleanup){WorldFailure(binding.Metadata.World,cleanup);return;}
            ForgetWorldActor(binding);awaitingSpawn.Remove(binding);
            if(binding.Metadata.Owner>0)
            {
                var owner=NetworkServer.connections.Values.FirstOrDefault(x=>x.authenticationData is PNPeer peer&&peer.Owner==binding.Metadata.Owner);
                owner?.Disconnect();return;
            }
            WorldFailure(binding.Metadata.World,error);
        }
        public void RemoveActor(PNActorShell shell)
        {
            if(initialStates.TryGetValue(shell.netId,out var initial)&&ReferenceEquals(initial.Shell,shell)){if(inbox.Contains(shell.netId,initial.Token))inbox.Release(shell.netId,initial.Token);initialStates.Remove(shell.netId);}
            if(bindings.TryGetValue(shell.netId,out var binding)&&ReferenceEquals(binding.Shell,shell))
            {try{CleanupBinding(binding);}catch(Exception error){WorldFailure(binding.Metadata.World,error);throw;}Scheduler.Retire(binding.Candidate);ForgetWorldActor(binding);if(inbox.Contains(shell.netId,binding.Token))inbox.Release(shell.netId,binding.Token);bindings.Remove(shell.netId);}
        }
        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            foreach(var entry in worldPeers.Values.Where(x=>x.Connection==conn).ToArray())UnsubscribeWorld(conn,entry.World.InstanceId);
            peerFences.Remove(conn.connectionId);
            if(conn.authenticationData is PNPeer peer && peer.Owner>0)
            {
                if(IsPlaying)Policy.OnOwnerLeft(()=>RetireOwner(peer.Owner));
                if(EveRegistrySession.IsReady&&EveRegistrySession.Context.IsActive)EveRegistrySession.Context.Admission.RemovePeer(peer.Owner);
            }
            PruneRetiredStreams(conn);
            base.OnServerDisconnect(conn);
        }
        private void RetireOwner(long owner)
        {
            foreach(var binding in bindings.Values.Concat(awaitingSpawn).Where(x=>x.Metadata.Owner==owner).ToArray())RetireBinding(binding);
        }
        private void RetireBinding(Binding binding)
        {
            if(binding.Shell!=null&&binding.Shell.netId!=0)
            {binding.Retiring=true;binding.RetiringAt=Time.realtimeSinceStartupAsDouble;DetachBuffPublication(binding);Scheduler.Retire(binding.Candidate);return;}
            CleanupBinding(binding);Scheduler.Retire(binding.Candidate);
            if(binding.Shell!=null){bindings.Remove(binding.Shell.netId);NetworkServer.Destroy(binding.Shell.gameObject);}
            awaitingSpawn.Remove(binding);
        }
        // Host: the owner picked another character in the lobby. The actor is retired and a new one enrolled at the same place, same world.
        private void RespawnAs(long owner,string character)
        {
            var old=bindings.Values.Concat(awaitingSpawn).FirstOrDefault(x=>!x.Retiring&&PNEntityRoles.IsPlayer(EveRegistrySession.Context.Registry,x.Metadata.Template)&&x.Metadata.Owner==owner);
            if(old==null||old.Metadata.Character==character||subscribedSession==null||!subscribedSession.Worlds.TryGet(old.Metadata.World,old.Metadata.WorldIncarnation,out var world))return;
            var loader=old.Candidate?.Loader;
            var request=new PNEntityRequest{Owner=owner,World=world,WorldIncarnation=world.Incarnation,ContentSlot=old.Metadata.ContentSlot,SpawnEntry=old.Metadata.SpawnEntry,Template=old.Metadata.Template,Character=character,SceneKey=old.Metadata.SceneKey,EntityKey="",Position=loader!=null&&loader.IsReady?loader.transform.position:old.Metadata.Position};
            var connection=old.OwnerConnection;
            RetireBinding(old);EnrollHost(request,connection);
        }
        public override void OnClientDisconnect()
        { LastFailure=LastFailure??"Host connection ended.";EndPlay(); }
        public override void OnStopServer()=>EndPlay();
        private void EndPlay()
        {
            stopping=true;
            try{CleanupPlay();}catch(Exception error){LastFailure=error.Message;Debug.LogException(error,this);}
        }
        public void StopSession()
        {
            if(teardownRunning)return;teardownRunning=true;stopping=true;
            try
            {
                if(!transportStopped)
                {
                    if(NetworkServer.active&&NetworkClient.active)StopHost();else if(NetworkServer.active)StopServer();else if(NetworkClient.active)StopClient();
                    NetworkClient.Shutdown();NetworkServer.Shutdown();
                    if(transport!=null&&transport.gameObject.activeSelf){transport.ServerStop();transport.ClientDisconnect();transport.Shutdown();}
                    transportStopped=true;
                }
                CleanupPlay();
                NotifyTeardown();
            }
            finally{teardownRunning=false;}
        }
        private void ReleaseInboxes(){foreach(var item in bindings)if(inbox.Contains(item.Key,item.Value.Token))inbox.Release(item.Key,item.Value.Token);}
        public bool PeerInputReady(NetworkConnectionToClient conn)=>IsPlaying&&conn.authenticationData is PNPeer peer&&peer.PlayRevision==PlayRevision&&peer.InputReady&&peer.InterruptedAt<0;
        private string BindingDigest(NetworkConnectionToClient observer=null,Guid world=default)
        {
            using(var stream=new System.IO.MemoryStream())
            {
                using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))
                {
                    writer.Write(string.Join("|",RequiredAdapters.OrderBy(x=>x,StringComparer.Ordinal)));
                    foreach(var item in bindings.Where(x=>!x.Value.Retiring&&(world==Guid.Empty?RoomCatchupBinding(x.Value,observer):x.Value.Metadata.World==world)&&(observer==null||x.Value.Shell.netIdentity.observers.ContainsKey(observer.connectionId))).OrderBy(x=>x.Value.Metadata.Entity))
                    {
                        var m=item.Value.Metadata;var scope=Scope(item.Value,observer);writer.Write(scope.World.ToByteArray());writer.Write(scope.Incarnation);writer.Write(scope.Membership);writer.Write(scope.Subscription);writer.Write(m.ContentSlot??"");writer.Write(m.SpawnEntry??"");writer.Write(m.Template??"");writer.Write(m.HostSession.ToByteArray());writer.Write(m.Entity);writer.Write(m.Generation);writer.Write(m.Owner);writer.Write(item.Key);writer.Write(m.Character);writer.Write(m.SceneKey);writer.Write(m.EntityKey);writer.Write(item.Value.Shell.HasBuff);writer.Write(item.Value.Shell.HasMove);
                        ulong nonce=0;if(item.Value.Shell.HasBuff){if(observer==null)nonce=(ulong)item.Value.Token;else if(buffPeers.TryGetValue((observer.connectionId,item.Key),out var peer))nonce=peer.Stream.Nonce;}writer.Write(nonce);
                        ulong moveNonce=0;if(item.Value.Shell.HasMove){if(observer==null)moveNonce=(ulong)item.Value.Token;else if(movePeers.TryGetValue((observer.connectionId,item.Key),out var mp))moveNonce=mp.Stream.Nonce;}writer.Write(moveNonce);
                    }
                }
                using(var hash=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-","");
            }
        }
        // Interruption protocol below runs on unscaled elapsed time and never advances gameplay to compensate.
        private double lastHeartbeat,lastHostTraffic,clientInterrupted=-1;
        private double clientStartedAt;
        private uint heartbeatSequence;
        private bool paused,clientInputReady=false;
        private double hostInterrupted=-1;
        private uint lastResponseSequence;
        private uint clientResumeFloor;
        private double GraceLimit=>IsPlaying&&Policy!=null?Policy.GraceSeconds:Profile.GraceSeconds;
        public bool ClientInputReady=>clientInputReady;
        private void OnApplicationPause(bool value)
            => NotifyApplicationPause(value);
        public void NotifyApplicationPause(bool value)
        {
            paused=value;
            double now=Time.realtimeSinceStartupAsDouble;
            if(NetworkClient.isConnected&&!NetworkServer.active&&IsPlaying)
            {
                if(value){if(clientInterrupted<0)clientInterrupted=now;clientInputReady=false;foreach(var binding in bindings.Values)SetInput(binding,false);}
                if(!value)clientResumeFloor=heartbeatSequence+1;
                SendHeartbeat();
            }
            if(NetworkServer.active&&IsPlaying)
            {
                if(EveRegistrySession.IsReady&&EveRegistrySession.Context.Cleanup.Scheduler.QuarantineCount>0&&(Worlds==null||Scheduler.HasUnscopedResidents)){LastFailure="Authoritative cleanup quarantined; session consistency cannot be established.";StopHost();return;}
                if(value)
                {
                    if(hostInterrupted<0)hostInterrupted=now;
                    foreach(var binding in bindings.Values)SetInput(binding,false);
                    foreach(var conn in NetworkServer.connections.Values)if(conn.authenticationData is PNPeer peer&&peer.Owner>0)Interrupt(peer);
                }
                else if(hostInterrupted>=0)
                {
                    if(now-hostInterrupted>=GraceLimit||!EveRegistrySession.IsReady||!EveRegistrySession.Context.IsActive||!AdaptersSettled()||bindings.Values.Any(x=>x.Candidate.Loader==null||!x.Candidate.Loader.IsReady))
                    {LastFailure="Host cannot safely continue after interruption.";StopHost();return;}
                    hostInterrupted=-1;foreach(var binding in bindings.Values.Where(x=>x.Metadata.Owner==0))SetInput(binding,true);
                }
            }
        }
        private void SendHeartbeat()
        {
            // Heartbeats require authentication on the server. In particular, do not race a reliable admission rejection with an unauthorized heartbeat.
            if(!NetworkClient.isConnected||NetworkClient.connection==null||!NetworkClient.connection.isAuthenticated)return;
            NetworkClient.Send(new PNHeartbeat {Session=IsPlaying?PlaySession:Guid.Empty,PlayRevision=PlayRevision,Sequence=++heartbeatSequence,ResumeFloor=clientResumeFloor,Paused=paused,ResumeRequired=!clientInputReady||clientInterrupted>=0});lastHeartbeat=Time.realtimeSinceStartupAsDouble;
        }
        private void Interrupt(PNPeer peer)
        {
            peer.Grace.Interrupt(Time.realtimeSinceStartupAsDouble);
            peer.InputReady=false;
            foreach(var binding in bindings.Values.Where(x=>x.Metadata.Owner==peer.Owner))SetInput(binding,false);
        }
        private void SetInput(Binding binding,bool allowed)
        {
            var loader=binding.Candidate?.Loader;if(loader==null)return;
            allowed=allowed&&WorldInputAllowed(binding)&&(binding.Shell?.HasMove!=true||binding.Movement.Attached&&(NetworkServer.active||binding.Movement.Component?.MovementBaselineApplied==true));
            if(!allowed&&!NetworkServer.active&&IsClientBindingCurrent(binding)&&binding.Movement.Component?.MovementHeld==false&&binding.Shell.HasMove)RequestMove(binding,true);
            binding.Movement.Component?.SetMovementHeld(!allowed);
            var gate=loader.GetComponent<PNNetworkInputGate>()??loader.gameObject.AddComponent<PNNetworkInputGate>();
            bool changed=gate.Blocked!=!allowed;gate.Blocked=!allowed;
            if(!allowed)loader.GetComponent<PNPlayerMove>()?.SetMoveDirection(Vector2.zero);
            if(changed)loader.GetComponent<PNPlayerSkill>()?.RefreshInput();
        }
        private void ReceiveHeartbeat(NetworkConnectionToClient conn,PNHeartbeat value)
        {
            if(!(conn.authenticationData is PNPeer peer)||value.Sequence<=peer.Sequence)return;
            if(peer.InterruptedAt>=0&&Time.realtimeSinceStartupAsDouble-peer.InterruptedAt>=GraceLimit){conn.Disconnect();return;}
            if(!IsPlaying)
            {
                if(value.Session!=Guid.Empty||value.PlayRevision!=PlayRevision)return;
                peer.Sequence=value.Sequence;peer.LastTraffic=Time.realtimeSinceStartupAsDouble;
                conn.Send(new PNResume{PlayRevision=PlayRevision,Sequence=value.Sequence,Acknowledged=true});return;
            }
            if(!MatchPlay(value.Session,value.PlayRevision))return;
            if(value.ResumeFloor>value.Sequence){conn.Disconnect();return;}
            if(value.ResumeFloor>peer.ResumeChallenge){peer.ResumeChallenge=0;peer.ResumeFloor=0;peerFences.Remove(conn.connectionId);}
            peer.Sequence=value.Sequence;peer.LastTraffic=Time.realtimeSinceStartupAsDouble;
            if(value.ResumeRequired)
            {Interrupt(peer);if(!value.Paused&&peer.ResumeFloor==0){peer.ResumeFloor=value.Sequence;peer.ResumeChallenge=value.Sequence;}}
            if(value.Paused){Interrupt(peer);peer.ResumeChallenge=peer.ResumeFloor=0;peerFences.Remove(conn.connectionId);return;}
            if(peer.InterruptedAt>=0)
            {if(peer.ResumeChallenge==0)peer.ResumeChallenge=value.Sequence;if(!SendFence(conn,peer))return;conn.Send(new PNResume {Session=PlaySession,PlayRevision=PlayRevision,Sequence=peer.ResumeChallenge,Acknowledged=false,BindingDigest=BindingDigest(conn)});}
            else conn.Send(new PNResume {Session=PlaySession,PlayRevision=PlayRevision,Sequence=value.Sequence,Acknowledged=true,BindingDigest=BindingDigest(conn)});
        }
        private void ReceiveResumeClient(PNResume value)
        {
            if(!IsPlaying){if(value.Session==Guid.Empty&&value.PlayRevision==PlayRevision)lastHostTraffic=Time.realtimeSinceStartupAsDouble;return;}
            if(!MatchPlay(value.Session,value.PlayRevision))return;
            if(clientInterrupted>=0&&value.Sequence<clientResumeFloor)return;
            if(value.Sequence<lastResponseSequence)return;
            lastResponseSequence=value.Sequence;
            lastHostTraffic=Time.realtimeSinceStartupAsDouble;
            if(paused)return;
            if(!value.Acknowledged)
            {
                if(clientInterrupted<0){clientInterrupted=Time.realtimeSinceStartupAsDouble;clientResumeFloor=value.Sequence;}
                clientInputReady=false;foreach(var binding in bindings.Values)SetInput(binding,false);
            }
            if(clientInterrupted>=0&&Time.realtimeSinceStartupAsDouble-clientInterrupted>=GraceLimit){LastFailure="Catch-up exceeded interruption grace.";StopClient();return;}
            // Required state must be settled before acknowledgement. P1 owns lifecycle/bindings; later adapters add state barriers.
            if(!AdaptersSettled() || bindings.Values.Where(x=>RoomCatchupBinding(x)).Any(x=>x.Candidate==null||x.Candidate.Loader==null||!x.Candidate.Loader.IsReady))return;
            if(value.BindingDigest!=BindingDigest())return;
            if((clientInterrupted>=0||!clientInputReady)&&!FenceSettled(value.Sequence,value.BindingDigest)){if(clientInterrupted<0)clientInterrupted=Time.realtimeSinceStartupAsDouble;return;}
            if(!value.Acknowledged){NetworkClient.Send(new PNResume {Session=PlaySession,PlayRevision=PlayRevision,Sequence=value.Sequence,Acknowledged=true,BindingDigest=value.BindingDigest});return;}
            clientInterrupted=-1;clientInputReady=true;foreach(var binding in bindings.Values)SetInput(binding,true);
        }
        private void ReceiveResume(NetworkConnectionToClient conn,PNResume value)
        {
            if(paused||!MatchPlay(value.Session,value.PlayRevision))return;
            if(!(conn.authenticationData is PNPeer peer)||!value.Acknowledged||value.Sequence!=peer.ResumeChallenge||peer.InterruptedAt<0)return;
            if(value.BindingDigest!=BindingDigest(conn))return;
            if(!peerFences.TryGetValue(conn.connectionId,out var fence)||fence[fence.Length-1].Attempt!=value.Sequence||fence[fence.Length-1].Digest!=value.BindingDigest)return;
            if(Time.realtimeSinceStartupAsDouble-peer.InterruptedAt>=GraceLimit){conn.Disconnect();return;}
            if(!peer.Grace.TryResume(Time.realtimeSinceStartupAsDouble,EveRegistrySession.IsReady&&EveRegistrySession.Context.IsActive,value.BindingDigest==BindingDigest(conn),AdaptersSettled()))return;
            peer.InputReady=true;
            peer.ResumeChallenge=0;
            peer.ResumeFloor=0;
            conn.Send(new PNResume {Session=PlaySession,PlayRevision=PlayRevision,Sequence=value.Sequence,Acknowledged=true,BindingDigest=value.BindingDigest});
            foreach(var binding in bindings.Values.Where(x=>x.Metadata.Owner==peer.Owner))SetInput(binding,true);
        }
        public override void LateUpdate()
        {
            base.LateUpdate();
            if(stopping){PumpRoom();NotifyTeardown();return;}
            PumpRoom();
            try{PumpLobby();}catch(Exception error){LastFailure=error.Message;}
            try{PumpWorldReplication();}catch(Exception error){ProtocolFailure(error,NetworkServer.active);}
            if(fatalSerialization){StopSession();return;}
            if(NetworkServer.active&&EveRegistrySession.IsReady&&EveRegistrySession.Context.Cleanup.Scheduler.QuarantineCount>0&&(Worlds==null||Scheduler.HasUnscopedResidents)){LastFailure="Authoritative cleanup quarantined; session consistency cannot be established.";StopHost();return;}
            double now=Time.realtimeSinceStartupAsDouble;
            if(!NetworkServer.active&&NetworkClient.active&&(NetworkClient.connection==null||!NetworkClient.connection.isAuthenticated)&&now-clientStartedAt>=Profile.AdmissionSeconds){LastFailure="Admission timeout.";StopClient();return;}
            if(NetworkServer.active)
            {
                if(hostInterrupted>=0&&now-hostInterrupted>=GraceLimit){LastFailure="Host interruption grace expired.";StopHost();return;}
                foreach(var conn in NetworkServer.connections.Values.ToArray())
                {
                    if(!(conn.authenticationData is PNPeer peer)||peer.Owner==0)continue;
                    if(IsPlaying&&now-peer.LastTraffic>1.5)Interrupt(peer);
                    if(!IsPlaying&&now-peer.LastTraffic>=GraceLimit){conn.Disconnect();continue;}
                    if(peer.InterruptedAt>=0&&now-peer.InterruptedAt>=GraceLimit)conn.Disconnect();
                }
            }
            else if(NetworkClient.isConnected)
            {
                if(lastHostTraffic==0)lastHostTraffic=now;
                if(!IsPlaying&&now-lastHostTraffic>=GraceLimit){LastFailure="Room heartbeat expired.";StopClient();return;}
                if(IsPlaying&&(paused||now-lastHostTraffic>1.5))
                {
                    if(clientInterrupted<0){clientInterrupted=now;clientResumeFloor=heartbeatSequence+1;}
                    clientInputReady=false;foreach(var binding in bindings.Values)SetInput(binding,false);
                }
                if(now-lastHeartbeat>.25)SendHeartbeat();
                if(clientInterrupted>=0&&now-clientInterrupted>=GraceLimit){LastFailure="Host interruption grace expired.";StopClient();}
            }
        }
    }
}
