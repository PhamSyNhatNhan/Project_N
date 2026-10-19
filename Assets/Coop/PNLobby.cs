using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
namespace ProjectN.Coop
{
    public struct PNLobbyPeer { public long Owner; public bool Host, Ready; }
    // Client -> host: the sender's own ready flag. Anything else in the lobby is host-authored.
    public struct PNLobbyIntent : NetworkMessage { public bool Ready; }
    // Host -> clients: bounded snapshot of the party. Clients only ever display it.
    public struct PNLobbyState : NetworkMessage { public ulong Revision; public bool Locked, Started; public int Capacity; public PNLobbyPeer[] Peers; }

    public sealed partial class PNNetworkManager
    {
        // When closed, new connections are refused with a readable reason (the roguelite run has started).
        public bool AdmissionClosed { get; private set; }
        public bool RunStarted { get; private set; }
        private readonly Dictionary<long,bool> lobbyReady=new Dictionary<long,bool>();
        private ulong lobbyRevision;
        private string lobbySignature;
        private PNLobbyState clientLobby;
        private bool hasClientLobby;
        private const int LobbyPeerLimit=64;

        private void ResetLobby()
        { AdmissionClosed=false;RunStarted=false;lobbyReady.Clear();lobbyRevision=0;lobbySignature=null;hasClientLobby=false; }
        // A new play in an open room starts with an open, un-started lobby; the revision keeps counting so clients accept it.
        private void ResetLobbyForPlay(){AdmissionClosed=false;RunStarted=false;lobbyReady.Clear();lobbySignature=null;}
        private void RegisterLobbyServer()=>NetworkServer.RegisterHandler<PNLobbyIntent>(ReceiveLobbyIntent);
        private void RegisterLobbyClient()=>NetworkClient.RegisterHandler<PNLobbyState>(ReceiveLobbyState);
        private void ReceiveLobbyIntent(NetworkConnectionToClient conn,PNLobbyIntent intent)
        {
            // A stale or early intent is not hostile: ignore it.
            if(stopping||!IsPlaying||RunStarted||!(conn.authenticationData is PNPeer peer)||peer.Owner<=0)return;
            lobbyReady[peer.Owner]=intent.Ready;
        }
        private void ReceiveLobbyState(PNLobbyState state)
        {
            if(stopping||NetworkServer.active)return;
            if(state.Peers==null||state.Peers.Length>LobbyPeerLimit||state.Capacity<1||state.Capacity>LobbyPeerLimit)return;
            if(hasClientLobby&&state.Revision<=clientLobby.Revision)return;
            clientLobby=state;hasClientLobby=true;
        }
        private List<PNLobbyPeer> BuildLobbyPeers()
        {
            var peers=new List<PNLobbyPeer>();
            foreach(var conn in NetworkServer.connections.Values)
                if(conn.isAuthenticated&&conn.authenticationData is PNPeer peer)
                    peers.Add(new PNLobbyPeer{Owner=peer.Owner,Host=peer.Owner==0,Ready=peer.Owner==0||lobbyReady.TryGetValue(peer.Owner,out var ready)&&ready});
            peers.Sort((a,b)=>a.Owner.CompareTo(b.Owner));
            return peers;
        }
        // Host only, once per frame: publish the party when anything about it changed.
        private void PumpLobby()
        {
            if(!NetworkServer.active||stopping)return;
            var peers=BuildLobbyPeers();
            var present=new HashSet<long>(peers.Select(p=>p.Owner));
            foreach(var owner in lobbyReady.Keys.ToArray())if(!present.Contains(owner))lobbyReady.Remove(owner);
            string signature=string.Join(",",peers.Select(p=>p.Owner+(p.Ready?"r":"w")))+"|"+AdmissionClosed+"|"+RunStarted;
            if(signature==lobbySignature)return;
            lobbySignature=signature;lobbyRevision++;
            var state=new PNLobbyState{Revision=lobbyRevision,Locked=AdmissionClosed,Started=RunStarted,Capacity=Profile.MaximumClients+1,Peers=peers.ToArray()};
            foreach(var conn in NetworkServer.connections.Values.ToArray())
                if(!(conn is LocalConnectionToClient)&&conn.isAuthenticated&&conn.authenticationData is PNPeer)conn.Send(state);
        }
        public PNLobbyInfo GetLobbyInfo()
        {
            if(stopping)return null;
            List<PNLobbyPeer> peers;bool locked,started;int capacity;long local;
            if(NetworkServer.active){peers=BuildLobbyPeers();locked=AdmissionClosed;started=RunStarted;capacity=Profile.MaximumClients+1;local=0;}
            else if(hasClientLobby){peers=clientLobby.Peers.ToList();locked=clientLobby.Locked;started=clientLobby.Started;capacity=clientLobby.Capacity;local=RoomOwner;}
            else return null;
            return new PNLobbyInfo(peers.Select(p=>new PNLobbyPeerInfo{Owner=p.Owner,Host=p.Host,Ready=p.Ready,Local=p.Owner==local}).ToList(),locked,started,capacity);
        }
        public void SetLobbyReady(bool ready)
        {
            if(NetworkServer.active||!NetworkClient.isConnected||stopping)return; // the host is always ready
            NetworkClient.Send(new PNLobbyIntent{Ready=ready});
        }
        public void SetAdmissionClosed(bool closed)
        {
            if(!NetworkServer.active)throw new InvalidOperationException("Only the host closes admissions.");
            if(RunStarted&&!closed)return; // a started run stays closed
            AdmissionClosed=closed;
        }
        public void MarkRunStarted()
        {
            if(!NetworkServer.active)throw new InvalidOperationException("Only the host starts a run.");
            RunStarted=true;AdmissionClosed=true;
        }
    }
}
