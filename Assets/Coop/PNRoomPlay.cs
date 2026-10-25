using System;
using System.Linq;
using EveEngine;
using Mirror;
using UnityEngine;
namespace ProjectN.Coop
{
    public sealed partial class PNNetworkManager
    {
        public Guid RoomId {get;private set;}
        public ulong RoomIncarnation {get;private set;}
        public ulong PlayRevision {get;private set;}
        public Guid PlaySession {get;private set;}
        public bool IsPlaying {get;private set;}
        public long RoomOwner {get;private set;}
        public Action<EveSessionRole> StartPlayRuntime;
        private EveContentCompatibility hostContent;
        private bool playCleanupPending,roomShellRequested;
        private double playEndedAt;
        public bool CanBeginPlay=>NetworkServer.active&&!stopping&&!IsPlaying&&!playCleanupPending&&
            EveRegistrySession.State==EveSessionState.Stopped&&NetworkServer.connections.Values.All(c=>
                !(c.authenticationData is PNPeer p)||p.Owner==0||p.EndedRevision==PlayRevision);
        public void AdmitRoom(Guid room,ulong incarnation,long owner,EveContentCompatibility content)
        {
            if(room==Guid.Empty||incarnation==0||owner<0)throw new InvalidOperationException("Invalid room admission.");
            Runtime.Content.Compatibility.Validate(content);
            if(NetworkServer.active&&(room!=RoomId||incarnation!=RoomIncarnation||owner!=0))throw new InvalidOperationException("Invalid host-local room admission.");
            RoomId=room;RoomIncarnation=incarnation;RoomOwner=owner;hostContent=content;
        }
        private PNPlayStatus PlayStatus()=>new PNPlayStatus{Room=RoomId,Incarnation=RoomIncarnation,Session=PlaySession,Revision=PlayRevision,Active=IsPlaying,Mode=ModeId};
        public override void OnServerConnect(NetworkConnectionToClient conn)
        {if(IsPlaying&&conn is LocalConnectionToClient&&conn.authenticationData is PNPeer local){ResetPeerPlay(local);if(Worlds?.DefaultWorld!=null)SubscribeWorld(conn,Worlds.DefaultWorld);}conn.Send(PlayStatus());}
        public void BeginPlay(string mode)
        {
            if(!CanBeginPlay)throw new InvalidOperationException("Room is not ready: finish previous play and peer cleanup first.");
            var policy=PNModeDefinition.Select(Runtime.Content.Registry,mode,Profile);
            if(NetworkServer.connections.Values.Count(c=>c.authenticationData is PNPeer p&&p.Owner>0)>policy.MaximumClients)
                throw new InvalidOperationException("Selected mode capacity exceeded.");
            ResetLobbyForPlay();
            ModeId=mode;Policy=policy;PlayRevision=checked(PlayRevision+1);
            IsPlaying=true;PlaySession=Guid.Empty;
            try
            {
                StartPlayRuntime(EveSessionRole.Host);PlaySession=EveRegistrySession.Context.SessionId;IsPlaying=true;
                ResetPlayState();InitializeReplication();
                // Runtime entities of the host (summons) are enrolled and retired through this manager from now on (it replaces the offline host of the same session).
                PNEntityHosts.Set(EveRegistrySession.Context,new CoopEntityHost(this));
                foreach(var conn in NetworkServer.connections.Values.ToArray())
                {
                    if(!(conn.authenticationData is PNPeer peer))continue;
                    if(peer.Owner==0)ResetPeerPlay(peer);else {peer.PlayRevision=0;peer.InputReady=false;}
                    if(!(conn is LocalConnectionToClient))NetworkServer.SetClientNotReady(conn);
                }
                if(NetworkServer.localConnection?.authenticationData is PNPeer localPeer&&NetworkServer.localConnection.isAuthenticated&&localPeer.PlayRevision==PlayRevision&&Worlds.DefaultWorld!=null)
                    SubscribeWorld(NetworkServer.localConnection,Worlds.DefaultWorld);
                foreach(var request in Factory.SceneEntities())EnrollHost(request,null);
                if(Profile.SelectPlayer)EnrollHost(DefaultPlayerRequest(0),null);
                foreach(var conn in NetworkServer.connections.Values.ToArray())if(conn.authenticationData is PNPeer)conn.Send(PlayStatus());
                // The host's own client sends Ready/AddPlayer only once its connection is authenticated; OnClientConnect does it
                // right after authentication when the room was opened in the same frame (an earlier send is refused by Mirror).
                if(NetworkClient.isConnected&&NetworkClient.connection!=null&&NetworkClient.connection.isAuthenticated){if(!NetworkClient.ready)NetworkClient.Ready();if(!roomShellRequested){roomShellRequested=true;NetworkClient.AddPlayer();}}
            }
            catch{EndRoomPlay();throw;}
        }
        private void ResetPeerPlay(PNPeer peer)
        {peer.PlayRevision=PlayRevision;peer.InputReady=true;peer.ResumeChallenge=peer.ResumeFloor=0;peer.Grace=new PNGraceWindow(GraceLimit);}
        private void ResetPlayState()
        {
            fatalSerialization=false;clientInputReady=false;clientInterrupted=hostInterrupted=-1;clientResumeFloor=0;
            lastResponseSequence=0;lastHostTraffic=Time.realtimeSinceStartupAsDouble;lastStatCapture=lastMoveTick=0;
            fenceAttempt=0;fenceCount=-1;fenceDigest=null;clientFence.Clear();peerFences.Clear();LastFailure=null;
        }
        private void ReceivePlayStatus(PNPlayStatus value)
        {
            if(stopping||NetworkServer.active)return;
            try
            {
                if(value.Room!=RoomId||value.Incarnation!=RoomIncarnation||value.Revision<PlayRevision)return;
                if(value.Active)
                {
                    if(value.Revision==PlayRevision){if(IsPlaying&&value.Session==PlaySession)return;throw new InvalidOperationException("Conflicting play offer.");}
                    if(IsPlaying||playCleanupPending||EveRegistrySession.State!=EveSessionState.Stopped||value.Session==Guid.Empty)
                        throw new InvalidOperationException("Play offer arrived before cleanup.");
                    Policy=PNModeDefinition.Select(Runtime.Content.Registry,value.Mode,Profile);
                    ModeId=value.Mode;PlayRevision=value.Revision;PlaySession=value.Session;
                    StartPlayRuntime(EveSessionRole.Client);EveRegistrySession.Context.Admission.AdmitHost(PlaySession,hostContent,RoomOwner);
                    IsPlaying=true;ResetPlayState();InitializeReplication();
                    NetworkClient.Send(new PNPlayReady{Room=RoomId,Incarnation=RoomIncarnation,Revision=PlayRevision,Session=PlaySession});
                    NetworkClient.Ready();if(!roomShellRequested){roomShellRequested=true;NetworkClient.AddPlayer();}
                }
                else
                {
                    if(value.Revision>PlayRevision&&IsPlaying)throw new InvalidOperationException("Unexpected play end revision.");
                    if(value.Revision==PlayRevision&&PlaySession!=Guid.Empty&&value.Session!=PlaySession)throw new InvalidOperationException("Conflicting play end.");
                    PlayRevision=value.Revision;PlaySession=value.Session;
                    try{CleanupPlay();}
                    catch(Exception error){LastFailure="Play cleanup pending: "+error.Message;return;}
                    if(!roomShellRequested){NetworkClient.Ready();roomShellRequested=true;NetworkClient.AddPlayer();}
                }
            }
            catch(Exception error){LastFailure=error.Message;StopClient();}
        }
        private void ReceivePlayReady(NetworkConnectionToClient conn,PNPlayReady value)
        {
            if(!(conn.authenticationData is PNPeer peer)||value.Room!=RoomId||value.Incarnation!=RoomIncarnation||value.Revision!=PlayRevision||value.Session!=PlaySession)return;
            if(value.Ended){if(!IsPlaying)peer.EndedRevision=value.Revision;return;}
            if(!IsPlaying)return;
            if(peer.PlayRevision==PlayRevision)return;
            EveRegistrySession.Context.Admission.AdmitPeer(peer.Owner,peer.Content);ResetPeerPlay(peer);
            if(Worlds.DefaultWorld!=null)SubscribeWorld(conn,Worlds.DefaultWorld);
        }
        public override void OnServerReady(NetworkConnectionToClient conn)
        {
            if(!(conn.authenticationData is PNPeer peer)){conn.Disconnect();return;}
            // Ready has no play header in Mirror. Ignore an in-flight lobby Ready;
            // the matching play ACK precedes the client's subsequent Ready.
            if(IsPlaying&&peer.PlayRevision!=PlayRevision)return;
            base.OnServerReady(conn);
            if(conn.identity==null&&peer.WantsRoomShell)OnServerAddPlayer(conn);
            if(IsPlaying&&peer.Owner>0&&peer.SelectPlayer&&conn.identity!=null&&peer.EnrolledRevision!=PlayRevision)
            {peer.EnrolledRevision=PlayRevision;EnrollHost(DefaultPlayerRequest(peer.Owner),conn);}
        }
        public void EndRoomPlay()
        {
            if(!NetworkServer.active)throw new InvalidOperationException("Only the host ends a room's play.");
            if(IsPlaying)
            {
                IsPlaying=false;playEndedAt=Time.realtimeSinceStartupAsDouble;
                // End precedes hides/state teardown on the reliable channel.
                foreach(var conn in NetworkServer.connections.Values.ToArray())if(conn.authenticationData is PNPeer peer)
                {
                    peer.Grace=new PNGraceWindow(Profile.GraceSeconds);peer.InputReady=false;peer.ResumeChallenge=peer.ResumeFloor=0;conn.Send(PlayStatus());
                    if(!(conn is LocalConnectionToClient))
                    {
                        // SetClientNotReady removes observer bookkeeping without sending hides.
                        // Retire only our actor presences; the room control shell keeps its netId.
                        foreach(var identity in NetworkServer.spawned.Values.Where(i=>i!=null&&i.GetComponent<PNActorShell>()!=null&&i.observers.ContainsKey(conn.connectionId)).ToArray())
                            conn.Send(new ObjectHideMessage{netId=identity.netId});
                        NetworkServer.SetClientNotReady(conn);
                    }
                }
            }
            CleanupPlay();
        }
        private void CleanupPlay()
        {
            IsPlaying=false;playCleanupPending=true;clientInputReady=false;clientInterrupted=hostInterrupted=-1;
            foreach(var b in bindings.Values.Concat(awaitingSpawn).ToArray())SetInput(b,false);
            // Adapter failures keep bindings reachable for retry.
            try{DisposeReplication();}
            catch
            {
                // Invalidate gameplay even when adapter resources need a retry.
                // Its registered cleanup retains the failed binding/stages.
                try{Runtime.EndSession();}catch(Exception error){LastFailure="Play cleanup pending: "+error.Message;}
                throw;
            }
            ReleaseInboxes();
            foreach(var b in bindings.Values.Concat(awaitingSpawn).ToArray())Scheduler?.Retire(b.Candidate);
            foreach(var shell in NetworkServer.active?NetworkServer.spawned.Values.Where(i=>i!=null&&i.GetComponent<PNActorShell>()!=null).ToArray():Array.Empty<NetworkIdentity>())NetworkServer.Destroy(shell.gameObject);
            bindings.Clear();awaitingSpawn.Clear();
            Runtime.EndSession();
            FinishPlayCleanup();
        }
        private void FinishPlayCleanup()
        {
            if(!playCleanupPending)return;
            if(EveRegistrySession.State==EveSessionState.Stopping)Runtime.EndSession();
            if(EveRegistrySession.State!=EveSessionState.Stopped)return;
            playCleanupPending=false;Factory=null;Scheduler=null;
            if(!NetworkServer.active&&NetworkClient.isConnected)
                NetworkClient.Send(new PNPlayReady{Room=RoomId,Incarnation=RoomIncarnation,Revision=PlayRevision,Session=PlaySession,Ended=true});
        }
        private bool MatchPlay(Guid session,ulong revision)=>IsPlaying&&session==PlaySession&&revision==PlayRevision;
        private void PumpRoom()
        {
            if(playCleanupPending){try{CleanupPlay();}catch(Exception error){LastFailure="Play cleanup pending: "+error.Message;}}
            if(NetworkServer.active&&!IsPlaying&&!playCleanupPending&&PlayRevision>0)
                foreach(var conn in NetworkServer.connections.Values.ToArray())
                    if(conn.authenticationData is PNPeer p&&p.Owner>0&&p.EndedRevision!=PlayRevision&&Time.realtimeSinceStartupAsDouble-playEndedAt>Profile.AdmissionSeconds)conn.Disconnect();
        }
    }
}
