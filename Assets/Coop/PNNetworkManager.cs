using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Mirror;
using UnityEngine;
namespace ProjectN.Coop
{
    public sealed class PNNetworkManager : NetworkManager
    {
        private sealed class Binding
        {
            public PNActorShell Shell;
            public EveEntityPreparationScheduler.Candidate Candidate;
            public PNActorMetadata Metadata;
            public long Token;
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
            if(stopping||!EveRegistrySession.IsReady||EveRegistrySession.Context.Role!=EveSessionRole.Client)return false;
            if(!bindings.TryGetValue(netId,out var binding)||binding.Token!=token)return false;
            try
            {
                var apply=latest?ApplyLatestState:ApplyRequiredEvent;
                if(apply==null)throw new InvalidOperationException("Required replication adapter is not installed.");
                if(binding.Candidate.Loader!=null&&binding.Candidate.Loader.IsReady)apply(binding.Candidate.Loader,payload);
                else if(latest)inbox.Latest(netId,token,payload);else inbox.Required(netId,token,payload);
                return true;
            }
            catch(Exception error){LastFailure=error.Message;NetworkClient.Disconnect();return false;}
        }
        private bool stopping;
        private bool fatalSerialization;
        public void ProtocolFailure(Exception error,bool authoritative)
        {if(stopping)return;LastFailure=error.Message;if(authoritative)fatalSerialization=true;else NetworkClient.Disconnect();}
        public bool HasEnded=>stopping;
        public Func<bool> RequiredStateSettled=()=>true;
        public long AllocateOwner()=>checked(++nextOwner);
        public void RouteIntent(NetworkConnectionToClient connection,Guid session,long entity,long generation,long sequence,string command,string payload)
        {
            if(stopping)return;
            try
            {
                if(!PeerInputReady(connection)||!(connection.authenticationData is PNPeer peer)||peer.Owner<=0)throw new InvalidOperationException("Command input is not admitted/ready.");
                if(command==null||command.Length>128||payload==null||System.Text.Encoding.UTF8.GetByteCount(payload)>Profile.CommandBytes)throw new InvalidOperationException("Command payload exceeds bounds.");
                var context=EveRegistrySession.Context;
                if(session!=context.SessionId||!context.Entities.TryGet(new EveEntityId(entity),generation,out _))throw new InvalidOperationException("Stale command binding.");
                context.Coop.HandleCommand(peer.Owner,new EveEntityCommand(session,entity,sequence,command,payload));
            }
            catch(Exception error){LastFailure=error.Message;connection.Disconnect();}
        }
        public override void OnStartServer()
        {
            NetworkServer.RegisterHandler<PNHeartbeat>(ReceiveHeartbeat);
            NetworkServer.RegisterHandler<PNResume>(ReceiveResume);
            foreach(var request in Factory.SceneEntities())EnrollHost(request,null);
            if(Profile.SelectPlayer)EnrollHost(Factory.Player(0),null);
        }
        public override void OnStartClient()
        {
            inbox=new EveBindingInbox {MaximumBindings=Profile.PendingBindings,MaximumStateBytes=Profile.LatestStateBytes,MaximumEvents=Profile.RequiredEvents,MaximumEventBytes=Profile.RequiredEventBytes,MaximumTotalEvents=Profile.PendingEvents,MaximumTotalBytes=Profile.PendingBytes};
            clientStartedAt=Time.realtimeSinceStartupAsDouble;
            NetworkClient.RegisterSpawnHandler(PNSessionShell.Asset,_=>CreateShell<PNSessionShell>("Session shell"),DestroyShell);
            NetworkClient.RegisterSpawnHandler(PNActorShell.Asset,_=>CreateShell<PNActorShell>("Actor shell"),DestroyShell);
            NetworkClient.RegisterHandler<PNResume>(ReceiveResumeClient);
        }
        public static GameObject CreateShell<T>(string name) where T:NetworkBehaviour
        {var root=new GameObject(name);root.SetActive(false);root.AddComponent<NetworkIdentity>();root.AddComponent<T>();root.SetActive(true);return root;}
        private static void DestroyShell(GameObject shell)=>Destroy(shell);
        public override void OnClientConnect()
        {
            NetworkClient.Ready();NetworkClient.AddPlayer();
        }
        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            if(!(conn.authenticationData is PNPeer peer)){conn.Disconnect();return;}
            var root=CreateShell<PNSessionShell>("Session owner "+peer.Owner);root.GetComponent<PNSessionShell>().Owner=peer.Owner;
            if(!NetworkServer.AddPlayerForConnection(conn,root,PNSessionShell.Asset)){Destroy(root);conn.Disconnect();return;}
            if(peer.Owner>0&&peer.SelectPlayer)EnrollHost(Factory.Player(peer.Owner),conn);
        }
        private void EnrollHost(PNEntityRequest request,NetworkConnectionToClient owner)
        {
            var metadata=new PNActorMetadata {HostSession=EveRegistrySession.Context.SessionId,Owner=request.Owner,Character=request.Character,SceneKey=request.SceneKey,EntityKey=request.EntityKey,Generation=1};
            metadata.Entity=EveRegistrySession.Context.Entities.AllocateHostId().Value;
            var binding=new Binding {Metadata=metadata,Token=checked(++nextToken)};
            awaitingSpawn.Add(binding);
            binding.Candidate=Scheduler.Enqueue(()=>Factory.Get(request),loader=>
            {
                Factory.Configure(loader,request);loader.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(metadata.Entity),EveId.Parse(metadata.Character),metadata.Owner,metadata.Generation);
            },Factory.Return,loader=>
            {
                if(stopping)return;
                Factory.Published(loader);
                var root=CreateShell<PNActorShell>("Actor "+metadata.Entity);binding.Shell=root.GetComponent<PNActorShell>();binding.Shell.Metadata=metadata;
                NetworkServer.Spawn(root,PNActorShell.Asset,owner);bindings.Add(binding.Shell.netId,binding);awaitingSpawn.Remove(binding);
            },error=>FailActor(binding,error));
        }
        public void ReceiveActor(PNActorShell shell)
        {
            try
            {
                var session=EveRegistrySession.Context;var metadata=shell.Metadata;
                if(metadata.Character==null||metadata.Character.Length>128||string.IsNullOrWhiteSpace(metadata.SceneKey)||metadata.SceneKey.Length>128||metadata.EntityKey==null||metadata.EntityKey.Length>128)throw new InvalidOperationException("Invalid bounded actor metadata.");
                if(session.Role!=EveSessionRole.Client||session.Coop.AdmittedHostSession!=metadata.HostSession||metadata.Entity<=0||metadata.Generation<=0||metadata.Owner<0||shell.netId==0||bindings.ContainsKey(shell.netId))throw new InvalidOperationException("Invalid actor binding.");
                if(bindings.Values.Any(x=>x.Metadata.Entity==metadata.Entity))throw new InvalidOperationException("Duplicate actor entity identity.");
                session.Registry.Characters.Get(EveId.Parse(metadata.Character));
                var request=new PNEntityRequest {Player=string.IsNullOrEmpty(metadata.EntityKey),Owner=metadata.Owner,Character=metadata.Character,SceneKey=metadata.SceneKey,EntityKey=metadata.EntityKey};
                // P1 actor metadata positions are initial content/spawn positions; Move replication follows in P4.
                if(request.Player)request.Position=Factory.Player(metadata.Owner).Position;
                var binding=new Binding {Shell=shell,Metadata=metadata,Token=checked(++nextToken)};bindings.Add(shell.netId,binding);
                inbox.Reserve(shell.netId,binding.Token,64+System.Text.Encoding.UTF8.GetByteCount(metadata.Character+metadata.SceneKey+metadata.EntityKey));
                binding.Candidate=Scheduler.Enqueue(()=>Factory.Get(request),loader=>
                {Factory.Configure(loader,request);loader.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(metadata.Entity),EveId.Parse(metadata.Character),metadata.Owner,metadata.Generation);},Factory.Return,loader=>
                {Factory.Published(loader);inbox.Release(shell.netId,binding.Token);SetInput(binding,clientInputReady);},error=>FailActor(binding,error),loader=>inbox.Apply(shell.netId,binding.Token,bytes=>ApplyLatestState(loader,bytes),bytes=>ApplyRequiredEvent(loader,bytes)));
            }
            catch(Exception error){LastFailure=error.Message;NetworkClient.Disconnect();}
        }
        private void FailActor(Binding binding,Exception error)
        {
            if(stopping)return;
            Debug.LogException(error,this);
            LastFailure=error.Message;
            if(EveRegistrySession.IsReady&&EveRegistrySession.Context.Role==EveSessionRole.Client){NetworkClient.Disconnect();return;}
            if(binding.Candidate!=null)Scheduler.Retire(binding.Candidate);
            if(binding.Shell!=null)
            {if(binding.Shell.netId!=0)NetworkServer.Destroy(binding.Shell.gameObject);else Destroy(binding.Shell.gameObject);}
            awaitingSpawn.Remove(binding);
            if(binding.Metadata.Owner>0)
            {
                var owner=NetworkServer.connections.Values.FirstOrDefault(x=>x.authenticationData is PNPeer peer&&peer.Owner==binding.Metadata.Owner);
                owner?.Disconnect();return;
            }
            // Required scene/host-local content failure cannot leave a partially represented shared world.
            StopHost();
        }
        public void RemoveActor(PNActorShell shell)
        {
            if(bindings.TryGetValue(shell.netId,out var binding)&&ReferenceEquals(binding.Shell,shell))
            {Scheduler.Retire(binding.Candidate);if(inbox.Contains(shell.netId,binding.Token))inbox.Release(shell.netId,binding.Token);bindings.Remove(shell.netId);}
        }
        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            if(conn.authenticationData is PNPeer peer && peer.Owner>0)
            {
                Policy.OnOwnerLeft(()=>RetireOwner(peer.Owner));
                if(EveRegistrySession.IsReady&&EveRegistrySession.Context.IsActive)EveRegistrySession.Context.Coop.RemovePeer(peer.Owner);
            }
            base.OnServerDisconnect(conn);
        }
        private void RetireOwner(long owner)
        {
            foreach(var binding in bindings.Values.Concat(awaitingSpawn).Where(x=>x.Metadata.Owner==owner).ToArray())
            {
                Scheduler.Retire(binding.Candidate);
                if(binding.Shell!=null){bindings.Remove(binding.Shell.netId);NetworkServer.Destroy(binding.Shell.gameObject);}
                awaitingSpawn.Remove(binding);
            }
        }
        public override void OnClientDisconnect()
        { LastFailure=LastFailure??"Host connection ended.";EndPlay(); }
        public override void OnStopServer()=>EndPlay();
        private void EndPlay()
        {
            if(stopping)return;stopping=true;
            foreach(var binding in bindings.Values.Concat(awaitingSpawn).ToArray())Scheduler.Retire(binding.Candidate);
            ReleaseInboxes();bindings.Clear();awaitingSpawn.Clear();Runtime.EndSession();
        }
        public void StopSession()
        {
            stopping=true;
            if(NetworkServer.active&&NetworkClient.active)StopHost();else if(NetworkServer.active)StopServer();else if(NetworkClient.active)StopClient();
            foreach(var binding in bindings.Values.Concat(awaitingSpawn).ToArray())Scheduler.Retire(binding.Candidate);
            ReleaseInboxes();bindings.Clear();awaitingSpawn.Clear();Runtime.EndSession();
        }
        private void ReleaseInboxes(){foreach(var item in bindings)if(inbox.Contains(item.Key,item.Value.Token))inbox.Release(item.Key,item.Value.Token);}
        public bool PeerInputReady(NetworkConnectionToClient conn)=>conn.authenticationData is PNPeer peer&&peer.InputReady&&peer.InterruptedAt<0;
        private string BindingDigest()
        {
            using(var stream=new System.IO.MemoryStream())
            {
                using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))foreach(var item in bindings.OrderBy(x=>x.Value.Metadata.Entity))
                {var m=item.Value.Metadata;writer.Write(m.HostSession.ToByteArray());writer.Write(m.Entity);writer.Write(m.Generation);writer.Write(m.Owner);writer.Write(item.Key);writer.Write(m.Character);writer.Write(m.SceneKey);writer.Write(m.EntityKey);}
                using(var hash=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-","");
            }
        }
        // Interruption protocol below runs on unscaled elapsed time and never advances gameplay to compensate.
        private double lastHeartbeat,lastHostTraffic,clientInterrupted=-1;
        private double clientStartedAt;
        private uint heartbeatSequence;
        private bool paused,clientInputReady=true;
        private double hostInterrupted=-1;
        private uint lastResponseSequence;
        private uint clientResumeFloor;
        public bool ClientInputReady=>clientInputReady;
        private void OnApplicationPause(bool value)
            => NotifyApplicationPause(value);
        public void NotifyApplicationPause(bool value)
        {
            paused=value;
            double now=Time.realtimeSinceStartupAsDouble;
            if(NetworkClient.isConnected&&!NetworkServer.active)
            {
                if(value){if(clientInterrupted<0)clientInterrupted=now;clientInputReady=false;foreach(var binding in bindings.Values)SetInput(binding,false);}
                SendHeartbeat();
                if(!value)clientResumeFloor=heartbeatSequence;
            }
            if(NetworkServer.active)
            {
                if(EveRegistrySession.IsReady&&EveRegistrySession.Context.Cleanup.Scheduler.QuarantineCount>0){LastFailure="Authoritative cleanup quarantined; session consistency cannot be established.";StopHost();return;}
                if(value)
                {
                    if(hostInterrupted<0)hostInterrupted=now;
                    foreach(var binding in bindings.Values)SetInput(binding,false);
                    foreach(var conn in NetworkServer.connections.Values)if(conn.authenticationData is PNPeer peer&&peer.Owner>0)Interrupt(peer);
                }
                else if(hostInterrupted>=0)
                {
                    if(now-hostInterrupted>=Profile.GraceSeconds||!EveRegistrySession.IsReady||!EveRegistrySession.Context.IsActive||!RequiredStateSettled()||bindings.Values.Any(x=>x.Candidate.Loader==null||!x.Candidate.Loader.IsReady))
                    {LastFailure="Host cannot safely continue after interruption.";StopHost();return;}
                    hostInterrupted=-1;foreach(var binding in bindings.Values.Where(x=>x.Metadata.Owner==0))SetInput(binding,true);
                }
            }
        }
        private void SendHeartbeat()
        {NetworkClient.Send(new PNHeartbeat {Sequence=++heartbeatSequence,Paused=paused,ResumeRequired=!clientInputReady||clientInterrupted>=0});lastHeartbeat=Time.realtimeSinceStartupAsDouble;}
        private void Interrupt(PNPeer peer)
        {
            peer.Grace.Interrupt(Time.realtimeSinceStartupAsDouble);
            peer.InputReady=false;
            foreach(var binding in bindings.Values.Where(x=>x.Metadata.Owner==peer.Owner))SetInput(binding,false);
        }
        private static void SetInput(Binding binding,bool allowed)
        {
            var loader=binding.Candidate?.Loader;if(loader==null)return;
            var gate=loader.GetComponent<PNNetworkInputGate>()??loader.gameObject.AddComponent<PNNetworkInputGate>();
            bool changed=gate.Blocked!=!allowed;gate.Blocked=!allowed;
            if(!allowed)loader.GetComponent<PNPlayerMove>()?.SetMoveDirection(Vector2.zero);
            if(changed)loader.GetComponent<PNPlayerSkill>()?.RefreshInput();
        }
        private void ReceiveHeartbeat(NetworkConnectionToClient conn,PNHeartbeat value)
        {
            if(!(conn.authenticationData is PNPeer peer)||value.Sequence<=peer.Sequence)return;
            if(peer.InterruptedAt>=0&&Time.realtimeSinceStartupAsDouble-peer.InterruptedAt>=Profile.GraceSeconds){conn.Disconnect();return;}
            peer.Sequence=value.Sequence;peer.LastTraffic=Time.realtimeSinceStartupAsDouble;
            if(value.ResumeRequired)
            {Interrupt(peer);if(!value.Paused&&peer.ResumeFloor==0){peer.ResumeFloor=value.Sequence;peer.ResumeChallenge=value.Sequence;}}
            if(value.Paused){Interrupt(peer);return;}
            if(peer.InterruptedAt>=0)
            {if(peer.ResumeChallenge==0)peer.ResumeChallenge=value.Sequence;conn.Send(new PNResume {Sequence=peer.ResumeChallenge,Acknowledged=false,BindingDigest=BindingDigest()});}
            else conn.Send(new PNResume {Sequence=value.Sequence,Acknowledged=true,BindingDigest=BindingDigest()});
        }
        private void ReceiveResumeClient(PNResume value)
        {
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
            if(clientInterrupted>=0&&Time.realtimeSinceStartupAsDouble-clientInterrupted>=Profile.GraceSeconds){LastFailure="Catch-up exceeded interruption grace.";StopClient();return;}
            // Required state must be settled before acknowledgement. P1 owns lifecycle/bindings; later adapters add state barriers.
            if(!RequiredStateSettled() || bindings.Values.Any(x=>x.Candidate==null||x.Candidate.Loader==null||!x.Candidate.Loader.IsReady))return;
            if(value.BindingDigest!=BindingDigest())return;
            if(!value.Acknowledged){NetworkClient.Send(new PNResume {Sequence=value.Sequence,Acknowledged=true,BindingDigest=value.BindingDigest});return;}
            clientInterrupted=-1;clientInputReady=true;foreach(var binding in bindings.Values)SetInput(binding,true);
        }
        private void ReceiveResume(NetworkConnectionToClient conn,PNResume value)
        {
            if(paused)return;
            if(!(conn.authenticationData is PNPeer peer)||!value.Acknowledged||value.Sequence!=peer.ResumeChallenge||peer.InterruptedAt<0)return;
            if(value.BindingDigest!=BindingDigest())return;
            if(Time.realtimeSinceStartupAsDouble-peer.InterruptedAt>=Profile.GraceSeconds){conn.Disconnect();return;}
            if(!peer.Grace.TryResume(Time.realtimeSinceStartupAsDouble,EveRegistrySession.IsReady&&EveRegistrySession.Context.IsActive,value.BindingDigest==BindingDigest(),RequiredStateSettled()))return;
            peer.InputReady=true;
            peer.ResumeChallenge=0;
            peer.ResumeFloor=0;
            conn.Send(new PNResume {Sequence=value.Sequence,Acknowledged=true,BindingDigest=value.BindingDigest});
            foreach(var binding in bindings.Values.Where(x=>x.Metadata.Owner==peer.Owner))SetInput(binding,true);
        }
        public override void LateUpdate()
        {
            base.LateUpdate();
            if(stopping)return;
            if(fatalSerialization){StopHost();return;}
            if(NetworkServer.active&&EveRegistrySession.IsReady&&EveRegistrySession.Context.Cleanup.Scheduler.QuarantineCount>0){LastFailure="Authoritative cleanup quarantined; session consistency cannot be established.";StopHost();return;}
            double now=Time.realtimeSinceStartupAsDouble;
            if(!NetworkServer.active&&NetworkClient.active&&!NetworkClient.ready&&now-clientStartedAt>=Profile.AdmissionSeconds){LastFailure="Admission timeout.";StopClient();return;}
            if(NetworkServer.active)
            {
                if(hostInterrupted>=0&&now-hostInterrupted>=Profile.GraceSeconds){LastFailure="Host interruption grace expired.";StopHost();return;}
                foreach(var conn in NetworkServer.connections.Values.ToArray())
                {
                    if(!(conn.authenticationData is PNPeer peer)||peer.Owner==0)continue;
                    if(now-peer.LastTraffic>1.5)Interrupt(peer);
                    if(peer.InterruptedAt>=0&&now-peer.InterruptedAt>=Profile.GraceSeconds)conn.Disconnect();
                }
            }
            else if(NetworkClient.isConnected&&NetworkClient.ready)
            {
                if(lastHostTraffic==0)lastHostTraffic=now;
                if(paused||now-lastHostTraffic>1.5)
                {
                    if(clientInterrupted<0){clientInterrupted=now;clientResumeFloor=heartbeatSequence+1;}
                    clientInputReady=false;foreach(var binding in bindings.Values)SetInput(binding,false);
                }
                if(now-lastHeartbeat>.25)SendHeartbeat();
                if(clientInterrupted>=0&&now-clientInterrupted>=Profile.GraceSeconds){LastFailure="Host interruption grace expired.";StopClient();}
            }
        }
    }
}
