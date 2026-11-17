using System;
using System.Collections.Generic;
using kcp2k;
using Mirror;
using UnityEngine;
namespace ProjectN.Composition
{
    // Explicit verification transport, selected only by --pn-sim-kcp. Faults
    // occur below KCP, including data/ACK/handshake datagrams on both channels.
    public sealed class PNKcpFaultTransport : KcpTransport
    {
        public float DatagramLatencyMs,DatagramLossPercent;
        public int Seed=12345;
        public long AttemptedDatagrams,AttemptedBytes,DroppedDatagrams,DroppedBytes,SentDatagrams,SentBytes;
        public int PendingBytes {get;private set;}public int PeakPendingBytes {get;private set;}
        // Diagnostic counts of KCP segments, distinct from delayed UDP bytes.
        // Queue/buffer occupancy is correlation evidence, not per-packet HOL proof.
        public void ReliableQueues(out int sendQueue,out int sendBuffer,out int receiveQueue,out int receiveBuffer)
        {
            sendQueue=client!=null?client.SendQueueCount:0;sendBuffer=client!=null?client.SendBufferCount:0;
            receiveQueue=client!=null?client.ReceiveQueueCount:0;receiveBuffer=client!=null?client.ReceiveBufferCount:0;
            if(server!=null)foreach(var connection in server.connections.Values){sendQueue+=connection.SendQueueCount;sendBuffer+=connection.SendBufferCount;receiveQueue+=connection.ReceiveQueueCount;receiveBuffer+=connection.ReceiveBufferCount;}
        }
        public const int QueueLimit=4*1024*1024,PacketLimit=4096;
        struct Delayed {public bool Server;public int Connection;public byte[] Bytes;public double Due;}
        readonly Queue<Delayed> pending=new Queue<Delayed>();System.Random random;bool closing;
        sealed class Client : KcpClient
        {
            readonly PNKcpFaultTransport owner;
            public Client(PNKcpFaultTransport o,KcpConfig c):base(()=>o.OnClientConnected.Invoke(),(m,ch)=>o.OnClientDataReceived.Invoke(m,FromKcpChannel(ch)),()=>o.OnClientDisconnected?.Invoke(),(e,r)=>o.OnClientError?.Invoke(ToTransportError(e),r),c){owner=o;}
            protected override void RawSend(ArraySegment<byte> data)=>owner.Route(false,0,data);
            public void SendDirect(ArraySegment<byte> data)=>base.RawSend(data);
        }
        sealed class Server : KcpServer
        {
            readonly PNKcpFaultTransport owner;
            public Server(PNKcpFaultTransport o,KcpConfig c):base((id,ep)=>o.OnServerConnectedWithAddress.Invoke(id,ep.PrettyAddress()),(id,m,ch)=>o.OnServerDataReceived.Invoke(id,m,FromKcpChannel(ch)),id=>o.OnServerDisconnected.Invoke(id),(id,e,r)=>o.OnServerError.Invoke(id,ToTransportError(e),r),c){owner=o;}
            protected override void RawSend(int connection,ArraySegment<byte> data)=>owner.Route(true,connection,data);
            public void SendDirect(int connection,ArraySegment<byte> data)=>base.RawSend(connection,data);
        }
        protected override void Awake(){base.Awake();random=new System.Random(Seed);client=new Client(this,config);server=new Server(this,config);}
        void Route(bool fromServer,int connection,ArraySegment<byte> data)
        {
            AttemptedDatagrams++;AttemptedBytes+=data.Count;
            if(closing){Send(fromServer,connection,data);return;}
            if(DatagramLossPercent>0&&random.NextDouble()*100<DatagramLossPercent){DroppedDatagrams++;DroppedBytes+=data.Count;return;}
            if(DatagramLatencyMs<=0){Send(fromServer,connection,data);return;}
            if(pending.Count>=PacketLimit||data.Count>QueueLimit-PendingBytes)throw new InvalidOperationException("KCP fault fixture queue bound");
            var bytes=new byte[data.Count];Buffer.BlockCopy(data.Array,data.Offset,bytes,0,data.Count);
            pending.Enqueue(new Delayed{Server=fromServer,Connection=connection,Bytes=bytes,Due=NetworkTime.localTime+DatagramLatencyMs/1000d});
            PendingBytes+=bytes.Length;PeakPendingBytes=Math.Max(PeakPendingBytes,PendingBytes);
        }
        void Send(bool fromServer,int connection,ArraySegment<byte> data)
        {if(fromServer)((Server)server).SendDirect(connection,data);else((Client)client).SendDirect(data);SentDatagrams++;SentBytes+=data.Count;}
        void Flush(){while(pending.Count>0&&pending.Peek().Due<=NetworkTime.localTime){var d=pending.Dequeue();PendingBytes-=d.Bytes.Length;Send(d.Server,d.Connection,new ArraySegment<byte>(d.Bytes));}}
        void Clear(){pending.Clear();PendingBytes=0;}
        public override void ClientLateUpdate(){base.ClientLateUpdate();Flush();}
        public override void ServerLateUpdate(){base.ServerLateUpdate();Flush();}
        // Terminal transport cleanup is immediate; pending test traffic is
        // cancelled. Active-session data/ACK/handshake faults remain simulated.
        public override void ClientDisconnect(){closing=true;try{base.ClientDisconnect();}finally{closing=false;Clear();}}
        public override void ServerDisconnect(int connection){closing=true;try{base.ServerDisconnect(connection);}finally{closing=false;int n=pending.Count;while(n-->0){var d=pending.Dequeue();if(d.Server&&d.Connection==connection)PendingBytes-=d.Bytes.Length;else pending.Enqueue(d);}}}
        public override void ServerStop(){closing=true;try{base.ServerStop();}finally{closing=false;Clear();}}
    }
}
