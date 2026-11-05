using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Mirror;
using UnityEngine;
namespace ProjectN.Coop
{
    public sealed class PNPeer
    {
        public long Owner;
        public EveContentCompatibility Content;
        public ulong PlayRevision,EndedRevision,EnrolledRevision;
        public double LastTraffic;
        public PNGraceWindow Grace;
        public double InterruptedAt=>Grace.StartedAt;
        public bool InputReady=true;
        public uint Sequence;
        public uint ResumeChallenge;
        public uint ResumeFloor;
        // Challenge of the last resume cycle the host closed: a heartbeat that still asks to resume under that cycle or an older one was sent before the client learned it was ready.
        public uint ClearedChallenge;
        public bool SelectPlayer;
        public bool WantsRoomShell;
        // The person behind this connection (PNPlayerId), checked at admission; the owner changes every session, this does not.
        public string PlayerId;
    }
    public sealed class PNNetworkAuthenticator : NetworkAuthenticator
    {
        public const int NetworkVersion=15;
        public PNNetworkManager Manager;
        private readonly Dictionary<NetworkConnectionToClient,double> pending=new Dictionary<NetworkConnectionToClient,double>();
        public static string PlatformGroup => Application.platform==RuntimePlatform.Android||Application.platform==RuntimePlatform.IPhonePlayer?"Mobile":"PC";
        public override void OnStartServer()=>NetworkServer.RegisterHandler<PNAdmissionRequest>(ReceiveRequest,false);
        public override void OnStopServer(){NetworkServer.UnregisterHandler<PNAdmissionRequest>();pending.Clear();}
        public override void OnServerAuthenticate(NetworkConnectionToClient conn)=>pending[conn]=Time.realtimeSinceStartupAsDouble;
        public override void OnStartClient()=>NetworkClient.RegisterHandler<PNAdmissionResponse>(ReceiveResponse,false);
        public override void OnStopClient()=>NetworkClient.UnregisterHandler<PNAdmissionResponse>();
        public override void OnClientAuthenticate()
        {NetworkClient.Send(new PNAdmissionRequest {NetworkVersion=NetworkVersion,Platform=PlatformGroup,Mode=Manager.ModeId,Limits=Manager.Profile.ProtocolLimits,SelectPlayer=Manager.Profile.SelectPlayer,Content=PNContentFingerprint.From(Manager.Runtime.Content.Compatibility),PlayerId=Manager.LocalPlayerId});}
        public void ReceiveRequest(NetworkConnectionToClient conn,PNAdmissionRequest request)
        {
            if(!pending.Remove(conn)||conn.isAuthenticated){conn.Disconnect();return;}
            try
            {
                bool local=conn is LocalConnectionToClient;
                if(!local&&Manager.AdmissionClosed)throw new InvalidOperationException("Run đã bắt đầu: phòng đã đóng, không nhận thêm người chơi.");
                if(request.Limits!=Manager.Profile.ProtocolLimits)throw new InvalidOperationException("Protocol limit profile mismatch.");
                var content=PNAdmissionRules.Validate(request,Manager.Runtime.Content.Compatibility,PlatformGroup,null,NetworkServer.connections.Values.Count(x=>x.authenticationData is PNPeer p&&p.Owner>0),Manager.IsPlaying?Manager.Policy.MaximumClients:Manager.Profile.MaximumClients,local,Manager.OnlinePersons(),Manager.ContinuePersons);
                long owner=local?0:Manager.AllocateOwner();
                Manager.SeatLobbyCharacter(owner,request.PlayerId);
                conn.authenticationData=new PNPeer {Owner=owner,PlayerId=request.PlayerId,Content=content,EndedRevision=Manager.IsPlaying?0:Manager.PlayRevision,LastTraffic=Time.realtimeSinceStartupAsDouble,SelectPlayer=request.SelectPlayer,Grace=new PNGraceWindow(Manager.Profile.GraceSeconds)};
                conn.Send(new PNAdmissionResponse {Accepted=true,Limits=Manager.Profile.ProtocolLimits,Room=Manager.RoomId,Incarnation=Manager.RoomIncarnation,Owner=owner,Content=PNContentFingerprint.From(Manager.Runtime.Content.Compatibility),Run=Manager.RunId});
                ServerAccept(conn);
            }
            catch(Exception error)
            {
                string reason=error.Message;Manager.LastFailure=reason;
                conn.Send(new PNAdmissionResponse {Accepted=false,Reason=reason});StartCoroutine(RejectLater(conn));
            }
        }
        private IEnumerator RejectLater(NetworkConnectionToClient conn)
        {
            // Allow the reliable rejection response to leave Mirror's send interval first.
            yield return new WaitForSecondsRealtime((float)Manager.Profile.RejectDrainSeconds);
            if(NetworkServer.active&&NetworkServer.connections.TryGetValue(conn.connectionId,out var current)&&ReferenceEquals(current,conn))ServerReject(conn);
        }
        public void ReceiveResponse(PNAdmissionResponse response)
        {
            try
            {
                if(!response.Accepted)throw new InvalidOperationException(response.Reason??"Admission refused.");
                if(response.Limits!=Manager.Profile.ProtocolLimits)throw new InvalidOperationException("Host protocol limit profile mismatch.");
                // The map the host chose must be one this machine has (a missing mod is refused at the content check; this is the safety net).
                if(!string.IsNullOrEmpty(response.Run)){if(!EveId.TryParse(response.Run,out var run)||!Manager.Runtime.Content.Registry.RogueliteRuns().TryGet(run,out _))throw new InvalidOperationException("Map của chủ phòng không có trên máy này: "+response.Run);Manager.RunId=response.Run;}
                Manager.AdmitRoom(response.Room,response.Incarnation,response.Owner,response.Content.Read());
                Manager.AdmittedAt=Time.realtimeSinceStartupAsDouble;ClientAccept();
            }
            catch(Exception error){Manager.LastFailure=error.Message;ClientReject();}
        }
        private void Update()
        {
            foreach(var item in pending.ToArray())if(Time.realtimeSinceStartupAsDouble-item.Value>Manager.Profile.AdmissionSeconds){pending.Remove(item.Key);ServerReject(item.Key);}
        }
    }
}
