using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

namespace ProjectN.Coop
{
    public sealed partial class PNNetworkManager
    {
        private sealed class RetiredStream
        {
            public NetworkConnectionToClient Connection;
            public PNMoveStream Move;
            public PNBuffStream Buff;
            public double Until;
            public IDisposable Lease;
        }
        private readonly Dictionary<(int,uint,byte),RetiredStream> retiredStreams=new Dictionary<(int,uint,byte),RetiredStream>();
        public int RetiredStreamCount=>retiredStreams.Count;
        private void PruneRetiredStreams(NetworkConnectionToClient connection=null)
        {
            foreach(var pair in retiredStreams.Where(x=>x.Value.Until<Time.realtimeSinceStartupAsDouble||connection!=null&&ReferenceEquals(x.Value.Connection,connection)).ToArray())
            {pair.Value.Lease.Dispose();retiredStreams.Remove(pair.Key);}
        }
        private void RememberRetiredMove(MovePeer peer)
        {
            if(stopping||!IsPlaying||subscribedSession?.IsActive!=true)return;PruneRetiredStreams();var key=(peer.Connection.connectionId,peer.Stream.NetId,(byte)0);
            if(retiredStreams.ContainsKey(key))return;if(retiredStreams.Count>=4096)throw new InvalidOperationException("Retired adapter scope capacity exceeded.");
            retiredStreams.Add(key,new RetiredStream{Connection=peer.Connection,Move=peer.Stream,Until=Time.realtimeSinceStartupAsDouble+60,Lease=ReserveWorld(256)});
        }
        private void RememberRetiredBuff(BuffPeer peer)
        {
            if(stopping||!IsPlaying||subscribedSession?.IsActive!=true)return;PruneRetiredStreams();var key=(peer.Connection.connectionId,peer.Stream.NetId,(byte)1);
            if(retiredStreams.ContainsKey(key))return;if(retiredStreams.Count>=4096)throw new InvalidOperationException("Retired adapter scope capacity exceeded.");
            retiredStreams.Add(key,new RetiredStream{Connection=peer.Connection,Buff=peer.Stream,Until=Time.realtimeSinceStartupAsDouble+60,Lease=ReserveWorld(256)});
        }
        private bool IsRetiredMove(NetworkConnectionToClient connection,PNMoveStream stream)
        {PruneRetiredStreams();return retiredStreams.TryGetValue((connection.connectionId,stream.NetId,0),out var entry)&&ReferenceEquals(entry.Connection,connection)&&entry.Move.Equals(stream);}
        private bool IsRetiredBuff(NetworkConnectionToClient connection,PNBuffStream stream)
        {PruneRetiredStreams();return retiredStreams.TryGetValue((connection.connectionId,stream.NetId,1),out var entry)&&ReferenceEquals(entry.Connection,connection)&&entry.Buff.Equals(stream);}
        private bool IsRetiredCommand(NetworkConnectionToClient connection,Guid session,long entity,long generation,PNWorldBindingScope scope)
        {
            PruneRetiredStreams();return retiredStreams.Any(x=>ReferenceEquals(x.Value.Connection,connection)&&(x.Key.Item3==0?x.Value.Move.Session==session&&x.Value.Move.Entity==entity&&x.Value.Move.Generation==generation&&x.Value.Move.Scope.Equals(scope):x.Value.Buff.Session==session&&x.Value.Buff.Entity==entity&&x.Value.Buff.Generation==generation&&x.Value.Buff.Scope.Equals(scope)));
        }
        private void ClearRetiredStreams(){foreach(var entry in retiredStreams.Values)entry.Lease.Dispose();retiredStreams.Clear();}
    }
}
