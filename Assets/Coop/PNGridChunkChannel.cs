using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using EveEngine;
using Mirror;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ProjectN.Coop
{
    // EN: The chunk channel of a grid world (Docs/SandboxPlan.md, step G8). One Reliable message carries up to 64 whole chunks (at most 16 KB of blobs); a chunk is at most about 3.4 KB, so no chunk is ever
    //     split and "applied only when complete" is "applied when it arrives". Every packet repeats the ticket of the world offer it belongs to; a packet of an old offer is dropped, a malformed one is a protocol failure.
    // VI: Kênh mảnh của world lưới (G8). Một thông báo Reliable mang tới 64 mảnh nguyên (tối đa 16 KB blob); một mảnh nhiều nhất khoảng 3,4 KB nên không bao giờ bị chia đoạn. Mỗi gói lặp lại vé của offer.
    public struct PNGridChunkPacket : NetworkMessage
    {
        public PNWorldTicket Ticket;
        // The last chunk of the first set (the ones around the client's players) is in this packet or an earlier one.
        public bool Final;
        public PNGridChunkItem[] Items;
    }

    // Client -> host (G8b): the chunks whose copy disagreed with the host's checksum (or whose base revision did not match); the host sends each again whole.
    public struct PNGridChunkResync : NetworkMessage
    {
        public PNWorldTicket Ticket;
        public int[] Indices;
    }

    public static class PNGridChunkWire
    {
        public static void WriteGridChunkResync(this NetworkWriter w,PNGridChunkResync m)
        {
            m.Ticket.Validate();if(m.Indices==null||m.Indices.Length<1||m.Indices.Length>PNGridChunkLimits.MaxResyncPerPacket)throw new FormatException("Grid resync count.");
            w.WriteByte(1);w.WriteWorldTicket(m.Ticket);w.WriteByte((byte)m.Indices.Length);foreach(int i in m.Indices){if(i<0)throw new FormatException("Grid resync index.");w.WriteInt(i);}
        }
        public static PNGridChunkResync ReadGridChunkResync(this NetworkReader r)
        {
            if(r.Remaining>PNWorldWire.ControlBytes)throw new FormatException("Grid resync bound.");
            if(r.ReadByte()!=1)throw new FormatException("Grid resync schema.");
            var m=new PNGridChunkResync{Ticket=r.ReadWorldTicket()};int count=r.ReadByte();if(count<1||count>PNGridChunkLimits.MaxResyncPerPacket)throw new FormatException("Grid resync count.");
            m.Indices=new int[count];for(int i=0;i<count;i++){m.Indices[i]=r.ReadInt();if(m.Indices[i]<0)throw new FormatException("Grid resync index.");}
            if(r.Remaining!=0)throw new FormatException("Grid resync length.");return m;
        }
        public static void WriteGridChunkPacket(this NetworkWriter w,PNGridChunkPacket p)
        {
            Validate(p);w.WriteByte(1);w.WriteWorldTicket(p.Ticket);w.WriteByte((byte)(p.Final?1:0));w.WriteByte((byte)p.Items.Length);
            foreach(var item in p.Items){w.WriteInt(item.Index);w.WriteInt(item.Version);w.WriteInt(item.IsDelta?item.Base:-1);w.WriteUShort((ushort)item.Blob.Length);w.WriteBytes(item.Blob,0,item.Blob.Length);}
        }
        public static PNGridChunkPacket ReadGridChunkPacket(this NetworkReader r)
        {
            if(r.Remaining>PNGridChunkLimits.PacketBytes+PNWorldWire.ControlBytes)throw new FormatException("Grid chunk packet bound.");
            if(r.ReadByte()!=1)throw new FormatException("Grid chunk packet schema.");
            var p=new PNGridChunkPacket{Ticket=r.ReadWorldTicket()};byte flags=r.ReadByte();if(flags>1)throw new FormatException("Grid chunk packet flags.");p.Final=flags==1;
            int count=r.ReadByte();if(count<1||count>PNGridChunkLimits.MaxChunksPerPacket)throw new FormatException("Grid chunk count.");
            p.Items=new PNGridChunkItem[count];int total=0;
            for(int i=0;i<count;i++)
            {
                int index=r.ReadInt(),version=r.ReadInt(),baseRevision=r.ReadInt(),size=r.ReadUShort();
                total+=size;if(index<0||version<0||baseRevision<-1||baseRevision>=version||size<6||size>PNGridSaveLimits.MaxChunkBlobBytes||total>PNGridChunkLimits.PacketBytes)throw new FormatException("Grid chunk item.");
                p.Items[i]=new PNGridChunkItem{Index=index,Version=version,Base=Math.Max(baseRevision,0),IsDelta=baseRevision>=0,Blob=r.ReadBytes(size)};
            }
            if(r.Remaining!=0)throw new FormatException("Grid chunk packet length.");
            return p;
        }
        public static void Validate(PNGridChunkPacket p)
        {
            p.Ticket.Validate();if(p.Items==null||p.Items.Length<1||p.Items.Length>PNGridChunkLimits.MaxChunksPerPacket)throw new FormatException("Grid chunk count.");
            int total=0;foreach(var item in p.Items){if(item.Index<0||item.Version<0||item.IsDelta&&(item.Base<0||item.Base>=item.Version)||item.Blob==null||item.Blob.Length<6||item.Blob.Length>PNGridSaveLimits.MaxChunkBlobBytes)throw new FormatException("Grid chunk item.");total+=item.Blob.Length;}
            if(total>PNGridChunkLimits.PacketBytes)throw new FormatException("Grid chunk packet size.");
        }
    }

    // Host -> client (G9c part 2): the placed things of a grid world, as the difference to what the client was told (Add, Remove, Health). Every packet repeats the ticket of the offer; an old one is dropped.
    public struct PNGridPlacementPacket : NetworkMessage
    {
        public PNWorldTicket Ticket;
        public PNGridPlacementItem[] Items;
    }

    public static class PNGridPlacementWire
    {
        // The most a packet can hold: the ticket and counts, plus 64 Add items of the longest definition and key.
        public const int PacketBytes=64*(1+4+2+PNGridPlacementLimits.DefinitionChars+4+4+1+4+2+PNSyncLimits.KeyChars)+256;
        public static void WriteGridPlacementPacket(this NetworkWriter w,PNGridPlacementPacket p)
        {
            p.Ticket.Validate();if(p.Items==null||p.Items.Length<1||p.Items.Length>PNGridPlacementLimits.MaxItemsPerPacket)throw new FormatException("Grid placement count.");
            w.WriteByte(1);w.WriteWorldTicket(p.Ticket);w.WriteByte((byte)p.Items.Length);
            foreach(var item in p.Items)
            {
                PNGridPlacementItem.Validate(item);w.WriteByte((byte)item.Kind);w.WriteInt(item.Id);
                if(item.Kind==PNGridPlacementKind.Remove)continue;
                if(item.Kind==PNGridPlacementKind.Health){w.WriteInt(item.Health);continue;}
                w.WriteString(item.Definition);w.WriteInt(item.X);w.WriteInt(item.Z);w.WriteByte((byte)item.Direction);w.WriteInt(item.Health);w.WriteString(item.Key??string.Empty);
            }
        }
        public static PNGridPlacementPacket ReadGridPlacementPacket(this NetworkReader r)
        {
            if(r.Remaining>PacketBytes+PNWorldWire.ControlBytes)throw new FormatException("Grid placement packet bound.");
            if(r.ReadByte()!=1)throw new FormatException("Grid placement packet schema.");
            var p=new PNGridPlacementPacket{Ticket=r.ReadWorldTicket()};int count=r.ReadByte();if(count<1||count>PNGridPlacementLimits.MaxItemsPerPacket)throw new FormatException("Grid placement count.");
            p.Items=new PNGridPlacementItem[count];
            for(int i=0;i<count;i++)
            {
                var item=new PNGridPlacementItem{Kind=(PNGridPlacementKind)r.ReadByte(),Id=r.ReadInt()};
                if(item.Kind==PNGridPlacementKind.Health)item.Health=r.ReadInt();
                else if(item.Kind==PNGridPlacementKind.Add)
                {
                    item.Definition=r.ReadString();item.X=r.ReadInt();item.Z=r.ReadInt();item.Direction=r.ReadByte();item.Health=r.ReadInt();item.Key=r.ReadString();
                    if(item.Key.Length==0)item.Key=null;
                }
                PNGridPlacementItem.Validate(item);p.Items[i]=item;
            }
            if(r.Remaining!=0)throw new FormatException("Grid placement packet length.");
            return p;
        }
    }

    public sealed partial class PNNetworkManager
    {
        private static readonly List<(float X,float Z)> gridFocus=new List<(float,float)>();
        // Per-peer counters for tests and the log: chunks and bytes the host sent, per world and peer.
        public (long Chunks,long Bytes,bool InitialDone,int Held)? GridChunkStats(int connection,Guid world)
            =>worldPeers.TryGetValue((connection,world),out var e)&&e.Chunks!=null?(e.Chunks.ChunksSent,e.Chunks.BytesSent,e.Chunks.InitialDone,e.Chunks.HasHeldPacket?1:0):((long,long,bool,int)?)null;
        // The same peer's G8b counters: deltas and whole chunks sent.
        public (int Deltas,int Fulls)? GridChunkKinds(int connection,Guid world)
            =>worldPeers.TryGetValue((connection,world),out var e)&&e.Chunks!=null?(e.Chunks.DeltasSent,e.Chunks.FullsSent):((int,int)?)null;
        public PNGridChunkInbox ClientGridInbox(Guid world)=>PNGridChunkInboxes.Find(subscribedSession,world);

        // Where this client's players stand in the world: the chunks around them are what it needs first. Before the client has an actor, the world's other actors, then the world's middle.
        private void CollectGridFocus(WorldSubscription entry)
        {
            gridFocus.Clear();var world=entry.World.InstanceId;long owner=entry.Connection.authenticationData is PNPeer peer?peer.Owner:-1;
            foreach(var pass in new[]{true,false})
            {
                foreach(var b in bindings.Values)
                {
                    if(b.Retiring||b.Metadata.World!=world||(b.Metadata.Owner==owner)!=pass)continue;
                    var loader=b.Candidate?.Loader;if(loader==null||!loader.IsReady)continue;
                    var at=loader.transform.position;gridFocus.Add((at.x,at.z));
                }
                if(gridFocus.Count>0)return;
            }
        }
        // The host's side: up to a few packets per frame, nearest chunks first, inside the shared send budget and a time budget. A packet the transport does not take stays held and goes out first next frame.
        private void PumpGridChunks(WorldSubscription entry)
        {
            var sender=entry.Chunks;CollectGridFocus(entry);
            long deadline=Stopwatch.GetTimestamp()+Stopwatch.Frequency*3/2000;int packets=0,bytes=0;
            while(packets<PNGridChunkLimits.PacketsPerFrame&&bytes<PNGridChunkLimits.BytesPerFrame)
            {
                if(!sender.Collect(gridFocus,PNGridChunkLimits.ChunksPerFrame,PNGridChunkLimits.PacketBytes,deadline,out var items,out bool final))return;
                var packet=new PNGridChunkPacket{Ticket=entry.Offer.Ticket,Final=final,Items=items.ToArray()};
                int size;
                using(var writer=NetworkWriterPool.Get())
                {
                    writer.WriteGridChunkPacket(packet);size=writer.Position;
                    if(size>NetworkMessages.MaxContentSize(Channels.Reliable))throw new FormatException("Grid chunk packet exceeds the configured channel.");
                    if(!TakeSendBudget(entry.Connection.connectionId,size+16))return;
                }
                entry.Connection.Send(packet,Channels.Reliable);sender.Commit();packets++;bytes+=size;
                if(Stopwatch.GetTimestamp()>deadline)return;
            }
        }
        // The host's side (G9c part 2): the difference between the placed things and what this client was told, up to a few packets per frame, inside the shared send budget.
        private void PumpGridPlacements(WorldSubscription entry)
        {
            var sender=entry.Placements;if(sender==null)return;
            CollectGridFocus(entry);int packets=0;long now=Stopwatch.GetTimestamp();
            while(packets<PNGridPlacementLimits.PacketsPerFrame)
            {
                if(!sender.Collect(gridFocus,PNGridPlacementLimits.MaxItemsPerPacket,now,out var items))return;
                var packet=new PNGridPlacementPacket{Ticket=entry.Offer.Ticket,Items=items.ToArray()};
                using(var writer=NetworkWriterPool.Get())
                {
                    writer.WriteGridPlacementPacket(packet);int size=writer.Position;
                    if(size>NetworkMessages.MaxContentSize(Channels.Reliable))throw new FormatException("Grid placement packet exceeds the configured channel.");
                    if(!TakeSendBudget(entry.Connection.connectionId,size+16))return;
                }
                entry.Connection.Send(packet,Channels.Reliable);sender.Commit();packets++;
            }
        }
        private void ReceiveGridPlacements(PNGridPlacementPacket packet)
        {
            if(NetworkServer.active||!MatchTicket(packet.Ticket))return;
            try
            {
                var inbox=PNGridChunkInboxes.Find(subscribedSession,packet.Ticket.World);
                // Stale: the offer it belongs to was replaced or closed. Dropped, never a disconnect.
                if(inbox==null||inbox.Subscription!=packet.Ticket.Subscription)return;
                if(!clientWorlds.TryGetValue(packet.Ticket.World,out var entry)&&!waitingWorldOffers.ContainsKey(packet.Ticket.World))return;
                inbox.Placements.Offer(packet.Items);
            }
            catch(Exception error){ProtocolFailure(error,false);}
        }
        // The client's side of an active offer: a grid world brings its descriptor, and a phone refuses a large one. False = the offer is not taken further.
        private bool AcceptGridOffer(PNWorldOffer offer)
        {
            if(string.IsNullOrEmpty(offer.Descriptor))return true;
            var descriptor=PNGridWorldDescriptor.Parse(JToken.Parse(offer.Descriptor));
            if(PNNetworkAuthenticator.PlatformGroup=="Mobile"&&(long)descriptor.Width*descriptor.Depth>PNGridChunkLimits.MobileMaxCells)
            {
                SetKeyedFailure("project_n:error.map_too_large_mobile","Map too large for a mobile device ("+descriptor.Width+" x "+descriptor.Depth+" cells, at most 512 x 512).");
                NetworkClient.Send(new PNWorldLoaded{Ticket=offer.Ticket,Success=false});return false;
            }
            PNGridChunkInboxes.Get(subscribedSession,offer.Ticket.World).Declare(offer.Descriptor,offer.Ticket.Subscription);return true;
        }
        // The client's side: the chunks whose copy is wrong are asked for again (once each until the whole chunk arrives).
        private static readonly List<int> gridResync=new List<int>();
        private void PumpGridResync(WorldSubscription entry)
        {
            var inbox=PNGridChunkInboxes.Find(subscribedSession,entry.Offer.Ticket.World);
            if(inbox==null||inbox.ResyncWaiting==0)return;
            gridResync.Clear();inbox.TakeResyncs(gridResync);
            if(gridResync.Count>0)NetworkClient.Send(new PNGridChunkResync{Ticket=entry.Offer.Ticket,Indices=gridResync.ToArray()},Channels.Reliable);
        }
        // The host's side: each chunk named goes out whole again.
        private void ReceiveGridResync(NetworkConnectionToClient connection,PNGridChunkResync value)
        {
            if(!MatchTicket(value.Ticket)||!worldPeers.TryGetValue((connection.connectionId,value.Ticket.World),out var entry)||!entry.Offer.Ticket.Equals(value.Ticket)||entry.Chunks==null)return;
            try{foreach(int index in value.Indices)entry.Chunks.MarkLost(index);}
            catch(Exception error){LastFailure=error.Message;connection.Disconnect();}
        }
        private void ForgetGridInbox(Guid world)=>PNGridChunkInboxes.Remove(subscribedSession,world);
        private void ReceiveGridChunks(PNGridChunkPacket packet)
        {
            if(NetworkServer.active||!MatchTicket(packet.Ticket))return;
            try
            {
                var inbox=PNGridChunkInboxes.Find(subscribedSession,packet.Ticket.World);
                // Stale: the offer it belongs to was replaced or closed. Dropped, never a disconnect.
                if(inbox==null||inbox.Subscription!=packet.Ticket.Subscription)return;
                if(!clientWorlds.TryGetValue(packet.Ticket.World,out var entry)&&!waitingWorldOffers.ContainsKey(packet.Ticket.World))return;
                inbox.Offer(packet.Items,packet.Final);
            }
            catch(Exception error){ProtocolFailure(error,false);}
        }
    }
}
