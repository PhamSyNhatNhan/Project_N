using System;
using Mirror;
namespace ProjectN.Coop
{
    public sealed class PNActionBaselineReceipt
    {
        readonly PNActionPacket expected;
        public readonly double Deadline;
        public bool Applied {get;private set;}
        public PNActionBaselineReceipt(PNActionPacket baseline,double originalDeadline)
        {PNActionWire.Header(baseline);PNTimeMath.Nonnegative(originalDeadline);if(baseline.Kind!=0)throw new ArgumentException("Expected baseline");baseline.Body=null;baseline.Digest=(byte[])baseline.Digest.Clone();expected=baseline;Deadline=originalDeadline;}
        public bool Confirm(PNActionPacket ack,double now)
        {PNTimeMath.Nonnegative(now);if(Applied||now>=Deadline||!PNActionTransport.MatchesApplied(expected,ack))return false;Applied=true;return true;}
    }
    // Explicitly installed by the owning adapter. No gameplay or observer execution.
    // Authorization must check admitted connection, entity relevance and exact binding.
    public sealed class PNActionTransport : IDisposable
    {
        readonly bool server,client;
        static bool ownsServer,ownsClient;
        bool disposed;
        public PNActionTransport(Func<NetworkConnectionToClient,PNActionStream,bool> authorizeServer,Action<NetworkConnectionToClient,PNActionPacket> onServer,Func<PNActionStream,bool> authorizeHost,Action<PNActionPacket> onClient)
        {
            server=onServer!=null;client=onClient!=null;
            if(server&&authorizeServer==null||client&&authorizeHost==null)throw new ArgumentNullException("Action authorization");
            if(server&&ownsServer||client&&ownsClient)throw new InvalidOperationException("Action handler already owned");
            if(server)ownsServer=true;if(client)ownsClient=true;
            if(server)NetworkServer.RegisterHandler<PNActionPacket>((conn,p)=>{if((p.Kind==2||p.Kind==3)&&authorizeServer(conn,p.Stream))onServer(conn,p);},true);
            if(client)NetworkClient.RegisterHandler<PNActionPacket>(p=>{if((p.Kind==0||p.Kind==1)&&authorizeHost(p.Stream))onClient(p);},true);
        }
        public void Dispose(){if(disposed)return;disposed=true;if(server){NetworkServer.UnregisterHandler<PNActionPacket>();ownsServer=false;}if(client){NetworkClient.UnregisterHandler<PNActionPacket>();ownsClient=false;}}
        public static bool MatchesApplied(PNActionPacket expected,PNActionPacket ack)
        {return ack.Kind==3&&ack.Stream.Equals(expected.Stream)&&ack.Request==expected.Request&&ack.Q==expected.Q&&ack.N==expected.N&&ack.T==expected.T&&PNActionWire.Same(ack.Digest,expected.Digest);}
    }
}
