using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EveEngine;
using Mirror;

namespace ProjectN.Coop
{
    // EN: The wire of the combat event channel (Docs/SpawnedObjectSyncPlan.md 4.4b part 3). One Reliable message carries up to 32 combat events of ONE world, under the ticket of the subscription the client holds for it
    //     (a packet of an old ticket is dropped, a malformed one is a protocol failure). The host keeps at most QueueEvents waiting per peer and drops the oldest past that (events feed presentation and UI only, never
    //     state, so a drop costs a number on screen, not correctness). The world is the packet's, so an event cannot name another world. Core never sees Mirror: PNCombatEvents asks this class to Send.
    // VI: Duong day cua kenh su kien chien dau. Mot thong diep Reliable mang toi 32 su kien cua MOT world kem ve cua dang ky; goi cu bi bo, goi sai la loi giao thuc. Hang cho toi da QueueEvents moi peer, qua do bo cai cu nhat
    //     (su kien chi phuc vu trinh bay va UI nen mat mot cai khong lam sai trang thai).
    public struct PNCombatPacket : NetworkMessage
    {
        public PNWorldTicket Ticket;
        public PNCombatEvent[] Events;
    }

    public static class PNCombatLimits
    {
        public const int EventsPerPacket = 32, PacketBytes = 12288, PacketBudget = 8000, QueueEvents = 1024, PacketsPerFrame = 2;
    }

    public static class PNCombatWire
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static void Text(NetworkWriter w, string value)
        {
            var bytes = string.IsNullOrEmpty(value) ? Array.Empty<byte>() : Utf8.GetBytes(value);
            if (bytes.Length > PNCombatEvent.MaxText) throw new FormatException("Combat string bound.");
            w.WriteByte((byte)bytes.Length); if (bytes.Length > 0) w.WriteBytes(bytes, 0, bytes.Length);
        }
        private static string Text(NetworkReader r)
        {
            int size = r.ReadByte(); if (size > PNCombatEvent.MaxText) throw new FormatException("Combat string bound.");
            if (size == 0) return null; var b = r.ReadBytesSegment(size); return Utf8.GetString(b.Array, b.Offset, b.Count);
        }
        private const byte Crit = 1, Killed = 2, LoricaBroke = 4, IFrame = 8, Point = 16;
        public static void Put(NetworkWriter w, PNCombatEvent e)
        {
            byte flags = (byte)((e.Critical ? Crit : 0) | (e.Killed ? Killed : 0) | (e.LoricaBroke ? LoricaBroke : 0) | (e.InIFrame ? IFrame : 0) | (e.HasPoint ? Point : 0));
            w.WriteByte((byte)e.Kind); w.WriteByte(flags); w.WriteLong(e.Serial); w.WriteLong(e.TargetId); w.WriteLong(e.SourceId);
            w.WriteFloat(e.Applied); w.WriteFloat(e.Calculated); w.WriteFloat(e.LoricaAbsorbed); w.WriteFloat(e.HealthBefore); w.WriteFloat(e.HealthAfter); w.WriteDouble(e.Clock);
            if (e.HasPoint) { w.WriteFloat(e.PointX); w.WriteFloat(e.PointY); w.WriteFloat(e.PointZ); }
            Text(w, e.HitKey);
            var tags = e.Tags ?? PNCombatEvent.NoTags; if (tags.Length > PNCombatEvent.MaxTags) throw new FormatException("Combat tag count.");
            w.WriteByte((byte)tags.Length); foreach (var tag in tags) Text(w, tag);
        }
        public static PNCombatEvent Take(NetworkReader r, PNObjectWorld world)
        {
            var e = new PNCombatEvent { World = world, Kind = (PNCombatEventKind)r.ReadByte() }; byte flags = r.ReadByte();
            e.Critical = (flags & Crit) != 0; e.Killed = (flags & Killed) != 0; e.LoricaBroke = (flags & LoricaBroke) != 0; e.InIFrame = (flags & IFrame) != 0; e.HasPoint = (flags & Point) != 0;
            e.Serial = r.ReadLong(); e.TargetId = r.ReadLong(); e.SourceId = r.ReadLong();
            e.Applied = r.ReadFloat(); e.Calculated = r.ReadFloat(); e.LoricaAbsorbed = r.ReadFloat(); e.HealthBefore = r.ReadFloat(); e.HealthAfter = r.ReadFloat(); e.Clock = r.ReadDouble();
            if (e.HasPoint) { e.PointX = r.ReadFloat(); e.PointY = r.ReadFloat(); e.PointZ = r.ReadFloat(); }
            e.HitKey = Text(r); int count = r.ReadByte(); if (count > PNCombatEvent.MaxTags) throw new FormatException("Combat tag count.");
            var tags = new string[count]; for (int i = 0; i < count; i++) tags[i] = Text(r) ?? throw new FormatException("Combat tag.");
            e.Tags = count == 0 ? PNCombatEvent.NoTags : tags;
            if (!e.TryValidate(out var error)) throw new FormatException(error);
            return e;
        }
        public static void WriteCombatPacket(this NetworkWriter w, PNCombatPacket packet)
        {
            Validate(packet); w.WriteByte(1); w.WriteWorldTicket(packet.Ticket); w.WriteByte((byte)packet.Events.Length);
            foreach (var e in packet.Events) Put(w, e);
            if (w.Position > PNCombatLimits.PacketBytes) throw new FormatException("Combat packet size.");
        }
        public static PNCombatPacket ReadCombatPacket(this NetworkReader r)
        {
            if (r.Remaining > PNCombatLimits.PacketBytes) throw new FormatException("Combat packet bound.");
            if (r.ReadByte() != 1) throw new FormatException("Combat packet schema.");
            var packet = new PNCombatPacket { Ticket = r.ReadWorldTicket() }; int count = r.ReadByte();
            if (count == 0 || count > PNCombatLimits.EventsPerPacket) throw new FormatException("Combat event count.");
            var world = new PNObjectWorld(packet.Ticket.World, packet.Ticket.Incarnation);
            packet.Events = new PNCombatEvent[count]; for (int i = 0; i < count; i++) packet.Events[i] = Take(r, world);
            if (r.Remaining != 0) throw new FormatException("Combat packet length."); Validate(packet); return packet;
        }
        public static void Validate(PNCombatPacket packet)
        {
            packet.Ticket.Validate();
            if (packet.Events == null || packet.Events.Length == 0 || packet.Events.Length > PNCombatLimits.EventsPerPacket) throw new FormatException("Combat event count.");
            var world = new PNObjectWorld(packet.Ticket.World, packet.Ticket.Incarnation);
            foreach (var e in packet.Events)
            {
                if (e == null || !e.World.Equals(world)) throw new FormatException("Combat event world.");
                if (!e.TryValidate(out var error)) throw new FormatException(error);
            }
        }
    }

    public sealed partial class PNNetworkManager
    {
        private CombatTransport combatTransport;
        // Evidence (plan 4.4b): what the host sent, how many events were dropped from a full queue, how many a client received.
        public long CombatBytesSent { get; private set; }
        public int CombatPacketsSent { get; private set; }
        public int CombatEventsSent { get; private set; }
        public int CombatDropped { get; private set; }
        public int CombatEventsReceived { get; private set; }
        public int CombatEventsQueued => worldPeers.Values.Sum(x => x.CombatQueue.Count);

        private void InitializeCombatEvents() { combatTransport = new CombatTransport(this); PNCombatEvents.Transport = combatTransport; }
        private void DisposeCombatEvents() { if (ReferenceEquals(PNCombatEvents.Transport, combatTransport)) PNCombatEvents.Transport = null; combatTransport = null; }

        private sealed class CombatTransport : IPNCombatEventTransport
        {
            private readonly PNNetworkManager owner;
            public CombatTransport(PNNetworkManager owner) { this.owner = owner; }
            public void Send(PNCombatEvent combatEvent) => owner.QueueCombat(combatEvent);
        }

        private void QueueCombat(PNCombatEvent e)
        {
            if (!NetworkServer.active || !IsPlaying || stopping || e == null || !e.World.IsValid) return;
            foreach (var entry in worldPeers.Values)
            {
                if (entry.Connection is LocalConnectionToClient || entry.Closing || !entry.Loaded || entry.World == null) continue;
                if (entry.World.InstanceId != e.World.Instance || entry.World.Incarnation != e.World.Incarnation) continue;
                if (entry.CombatQueue.Count >= PNCombatLimits.QueueEvents) { entry.CombatQueue.RemoveAt(0); CombatDropped++; }
                entry.CombatQueue.Add(e);
            }
        }

        private void PumpCombatEvents()
        {
            if (!NetworkServer.active || stopping || worldPeers.Count == 0) return;
            foreach (var entry in worldPeers.Values.ToArray())
            {
                if (entry.CombatQueue.Count == 0 || entry.Connection is LocalConnectionToClient || entry.Closing || !entry.Loaded || entry.World == null) continue;
                if (!NetworkServer.connections.ContainsKey(entry.Connection.connectionId)) continue;
                try { PumpCombat(entry); }
                catch (Exception error) { LastFailure = "Combat channel failed: " + error.Message; UnsubscribeWorld(entry.Connection, entry.World.InstanceId); }
            }
        }
        private void PumpCombat(WorldSubscription entry)
        {
            int connection = entry.Connection.connectionId;
            for (int packets = 0; packets < PNCombatLimits.PacketsPerFrame && entry.CombatQueue.Count != 0; packets++)
            {
                var source = entry.CombatQueue; int take = 0, bytes = 0;
                while (take < source.Count && take < PNCombatLimits.EventsPerPacket && (take == 0 || bytes + source[take].EstimatedBytes <= PNCombatLimits.PacketBudget)) bytes += source[take++].EstimatedBytes;
                var packet = new PNCombatPacket { Ticket = entry.Offer.Ticket, Events = source.GetRange(0, take).ToArray() };
                int wire;
                using (var writer = NetworkWriterPool.Get())
                {
                    writer.WriteCombatPacket(packet);
                    if (writer.Position > NetworkMessages.MaxContentSize(Channels.Reliable)) throw new FormatException("Combat packet exceeds the configured channel.");
                    wire = writer.Position;
                    if (!TakeSendBudget(connection, writer.Position + 16)) return;
                }
                CombatBytesSent += wire; CombatPacketsSent++; CombatEventsSent += take;
                entry.Connection.Send(packet, Channels.Reliable); source.RemoveRange(0, take);
            }
        }

        private void ReceiveCombatPacket(PNCombatPacket packet)
        {
            if (stopping || !IsPlaying || NetworkServer.active || !MatchTicket(packet.Ticket)) return;
            // Stale: the subscription it belongs to was replaced or closed. Dropped, never a disconnect.
            if (!clientWorlds.TryGetValue(packet.Ticket.World, out var entry) || !entry.Loaded || entry.Closing || entry.World == null || entry.World.Incarnation != packet.Ticket.Incarnation || entry.Offer.Ticket.Subscription != packet.Ticket.Subscription) return;
            try { foreach (var e in packet.Events) { CombatEventsReceived++; PNCombatEvents.Deliver(e); } }
            catch (Exception error) { ProtocolFailure(error, false); }
        }
    }
}
