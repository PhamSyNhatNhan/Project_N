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
        public double LastTraffic;
        public PNGraceWindow Grace;
        public double InterruptedAt=>Grace.StartedAt;
        public bool InputReady=true;
        public uint Sequence;
        public uint ResumeChallenge;
        public uint ResumeFloor;
        public bool SelectPlayer;
    }
    public sealed class PNNetworkAuthenticator : NetworkAuthenticator
    {
        public const int NetworkVersion=4;
        public PNNetworkManager Manager;
        private readonly Dictionary<NetworkConnectionToClient,double> pending=new Dictionary<NetworkConnectionToClient,double>();
        public static string PlatformGroup => Application.platform==RuntimePlatform.Android||Application.platform==RuntimePlatform.IPhonePlayer?"Mobile":"PC";
        public override void OnStartServer()=>NetworkServer.RegisterHandler<PNAdmissionRequest>(ReceiveRequest,false);
        public override void OnStopServer(){NetworkServer.UnregisterHandler<PNAdmissionRequest>();pending.Clear();}
        public override void OnServerAuthenticate(NetworkConnectionToClient conn)=>pending[conn]=Time.realtimeSinceStartupAsDouble;
        public override void OnStartClient()=>NetworkClient.RegisterHandler<PNAdmissionResponse>(ReceiveResponse,false);
        public override void OnStopClient()=>NetworkClient.UnregisterHandler<PNAdmissionResponse>();
        public override void OnClientAuthenticate()
        {NetworkClient.Send(new PNAdmissionRequest {NetworkVersion=NetworkVersion,Platform=PlatformGroup,Mode=Manager.ModeId,Limits=Manager.Profile.ProtocolLimits,SelectPlayer=Manager.Profile.SelectPlayer,Content=PNContentFingerprint.From(Manager.Runtime.Content.Compatibility)});}
        public void ReceiveRequest(NetworkConnectionToClient conn,PNAdmissionRequest request)
        {
            if(!pending.Remove(conn)||conn.isAuthenticated){conn.Disconnect();return;}
            try
            {
                bool local=conn is LocalConnectionToClient;
                if(request.Limits!=Manager.Profile.ProtocolLimits)throw new InvalidOperationException("Protocol limit profile mismatch.");
                var content=PNAdmissionRules.Validate(request,Manager.Runtime.Content.Compatibility,PlatformGroup,Manager.ModeId,NetworkServer.connections.Values.Count(x=>x.authenticationData is PNPeer p&&p.Owner>0),Manager.Policy.MaximumClients,local);
                long owner=local?0:Manager.AllocateOwner();
                if(!local)EveRegistrySession.Context.Coop.AdmitPeer(owner,content);
                conn.authenticationData=new PNPeer {Owner=owner,LastTraffic=Time.realtimeSinceStartupAsDouble,SelectPlayer=request.SelectPlayer,Grace=new PNGraceWindow(Manager.Profile.GraceSeconds)};
                conn.Send(new PNAdmissionResponse {Accepted=true,Limits=Manager.Profile.ProtocolLimits,HostSession=EveRegistrySession.Context.SessionId,Owner=owner,Content=PNContentFingerprint.From(Manager.Runtime.Content.Compatibility)});
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
                if(response.HostSession==Guid.Empty)throw new InvalidOperationException("Host session missing.");
                var context=EveRegistrySession.Context;
                if(context.Role==EveSessionRole.Client)context.Coop.AdmitHost(response.HostSession,response.Content.Read(),response.Owner);
                else if(response.Owner!=0||response.HostSession!=context.SessionId)throw new InvalidOperationException("Invalid host-local admission.");
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
