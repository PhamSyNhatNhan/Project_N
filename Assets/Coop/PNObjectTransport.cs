using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EveEngine;
using Mirror;

namespace ProjectN.Coop
{
    // EN: The wire of the spawned-object sync (Docs/SpawnedObjectSyncPlan.md, stage O3). One Reliable message carries up to 32 records of ONE world (Spawn / Retarget / End of the projectiles the host made),
    //     under the ticket of the subscription the client holds for that world; a packet of an old ticket is dropped, a malformed one is a protocol failure. A peer that qualifies for a world (it reported
    //     the world loaded) gets first the baseline of the world (the live objects, as a run of packets flagged Start .. End), then the records as they happen. The host keeps at most QueueRecords waiting
    //     per peer; past that it forgets the queue and sends a new baseline (the client ends the copies the baseline does not list), so nothing queues without bound and nothing is lost for good.
    //     Core and Game never see Mirror: Game's PNObjectSyncService asks this class to Send, and calls the sinks on the client.
    // VI: Duong day cua dong bo vat the sinh ra (O3). Mot thong diep Reliable mang toi 32 ban ghi cua MOT world, kem ve cua dang ky; goi cu bi bo, goi sai la loi giao thuc. Peer moi vao world nhan baseline truoc
    //     (cac vat dang song, mot loat goi co co Start .. End), roi moi ban ghi. Hang cho toi da QueueRecords moi peer; qua do thi bo hang va gui baseline moi.
    public struct PNObjectPacket : NetworkMessage
    {
        public PNWorldTicket Ticket;
        public byte Flags;
        public PNObjectRecord[] Records;
    }

    public static class PNObjectPacketFlags
    {
        // Baseline: the records are the live objects of the world. Start: the first packet of a baseline (the client forgets a half baseline). End: the last one (the client applies it).
        public const byte Baseline = 1, Start = 2, End = 4;
    }

    public static class PNObjectLimits
    {
        public const int RecordsPerPacket = 32, PacketBytes = 12288, PacketBudget = 8000, QueueRecords = 2048, PacketsPerFrame = 4, DefinitionChars = 128;
    }

    public static class PNObjectWire
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static void Text(NetworkWriter w, string value, int maximum)
        {
            var bytes = string.IsNullOrEmpty(value) ? Array.Empty<byte>() : Utf8.GetBytes(value);
            if (bytes.Length > maximum) throw new FormatException("Object string bound.");
            w.WriteUShort((ushort)bytes.Length); if (bytes.Length > 0) w.WriteBytes(bytes, 0, bytes.Length);
        }
        private static string Text(NetworkReader r, int maximum)
        {
            int size = r.ReadUShort(); if (size > maximum) throw new FormatException("Object string bound.");
            if (size == 0) return null; var b = r.ReadBytesSegment(size); return Utf8.GetString(b.Array, b.Offset, b.Count);
        }
        private static void Put(NetworkWriter w, PNObjectVector v) { w.WriteFloat(v.X); w.WriteFloat(v.Y); w.WriteFloat(v.Z); }
        private static PNObjectVector Take(NetworkReader r) => new PNObjectVector(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());

        private static void Put(NetworkWriter w, PNObjectRecord record)
        {
            w.WriteByte((byte)record.Type); w.WriteByte((byte)record.Kind); w.WriteLong(record.Id);
            switch (record.Type)
            {
                case PNObjectRecordType.Spawn:
                    Text(w, record.DefinitionId.ToString(), PNObjectLimits.DefinitionChars); Text(w, record.Input, PNObjectSyncLimits.InputChars); Put(w, record.Position); Put(w, record.Direction);
                    w.WriteLong(record.Target); w.WriteLong(record.Owner); w.WriteLong(record.Follow); Text(w, record.CastKey, PNObjectSyncLimits.CastKeyChars); w.WriteByte((byte)record.CastIndex);
                    w.WriteDouble(record.Speed); w.WriteDouble(record.Scale); w.WriteDouble(record.Lifetime); w.WriteDouble(record.Stamp); w.WriteFloat(record.Age); w.WriteFloat(record.LifeHint); break;
                case PNObjectRecordType.Retarget: w.WriteLong(record.Target); Put(w, record.Position); Put(w, record.Direction); w.WriteDouble(record.Stamp); break;
                case PNObjectRecordType.End: w.WriteByte(record.Reason); Put(w, record.Position); Put(w, record.Direction); w.WriteLong(record.Target); break;
                default: throw new FormatException("Object record type.");
            }
        }
        // The world is the packet's: a record never names its own, so a client cannot be told about an object of a world the ticket does not cover.
        private static PNObjectRecord Take(NetworkReader r, PNObjectWorld world)
        {
            var type = (PNObjectRecordType)r.ReadByte(); var kind = (PNObjectKind)r.ReadByte(); long id = r.ReadLong();
            switch (type)
            {
                case PNObjectRecordType.Spawn:
                {
                    var definition = EveId.Parse(Text(r, PNObjectLimits.DefinitionChars) ?? throw new FormatException("Object definition.")); string input = Text(r, PNObjectSyncLimits.InputChars);
                    var position = Take(r); var direction = Take(r); long target = r.ReadLong(), owner = r.ReadLong(), follow = r.ReadLong(); string castKey = Text(r, PNObjectSyncLimits.CastKeyChars); int castIndex = r.ReadByte();
                    double speed = r.ReadDouble(), scale = r.ReadDouble(), lifetime = r.ReadDouble(), stamp = r.ReadDouble(); float age = r.ReadFloat(), lifeHint = r.ReadFloat();
                    return PNObjectRecord.Spawn(kind, id, world, definition, input, position, direction, target, owner, follow, castKey, castIndex, speed, scale, lifetime, stamp, age, lifeHint);
                }
                case PNObjectRecordType.Retarget: { long target = r.ReadLong(); var position = Take(r); var direction = Take(r); return PNObjectRecord.Retarget(kind, id, world, target, position, direction, r.ReadDouble()); }
                case PNObjectRecordType.End: { byte reason = r.ReadByte(); var at = Take(r); var normal = Take(r); return PNObjectRecord.End(kind, id, world, reason, at, normal, r.ReadLong()); }
                default: throw new FormatException("Object record type.");
            }
        }

        public static void WriteObjectPacket(this NetworkWriter w, PNObjectPacket packet)
        {
            Validate(packet); w.WriteByte(1); w.WriteWorldTicket(packet.Ticket); w.WriteByte(packet.Flags); w.WriteByte((byte)packet.Records.Length);
            foreach (var record in packet.Records) Put(w, record);
            if (w.Position > PNObjectLimits.PacketBytes) throw new FormatException("Object packet size.");
        }
        public static PNObjectPacket ReadObjectPacket(this NetworkReader r)
        {
            if (r.Remaining > PNObjectLimits.PacketBytes) throw new FormatException("Object packet bound.");
            if (r.ReadByte() != 1) throw new FormatException("Object packet schema.");
            var packet = new PNObjectPacket { Ticket = r.ReadWorldTicket(), Flags = r.ReadByte() }; int count = r.ReadByte();
            if (count > PNObjectLimits.RecordsPerPacket) throw new FormatException("Object record count.");
            var world = new PNObjectWorld(packet.Ticket.World, packet.Ticket.Incarnation);
            packet.Records = new PNObjectRecord[count]; for (int i = 0; i < count; i++) packet.Records[i] = Take(r, world);
            if (r.Remaining != 0) throw new FormatException("Object packet length."); Validate(packet); return packet;
        }
        public static void Validate(PNObjectPacket packet)
        {
            packet.Ticket.Validate(); byte flags = packet.Flags;
            if ((flags & ~7) != 0 || (flags & 6) != 0 && (flags & PNObjectPacketFlags.Baseline) == 0) throw new FormatException("Object packet flags.");
            if (packet.Records == null || packet.Records.Length > PNObjectLimits.RecordsPerPacket || packet.Records.Length == 0 && (flags & PNObjectPacketFlags.Baseline) == 0) throw new FormatException("Object record count.");
            var world = new PNObjectWorld(packet.Ticket.World, packet.Ticket.Incarnation);
            foreach (var record in packet.Records)
            {
                if (record == null || !record.World.Equals(world)) throw new FormatException("Object record world.");
                if ((flags & PNObjectPacketFlags.Baseline) != 0 && record.Type != PNObjectRecordType.Spawn) throw new FormatException("A baseline lists Spawn records only.");
            }
        }
    }

    public sealed partial class PNNetworkManager
    {
        // Set by the composition once the play session exists; the service is the game's (PNGameSession.ObjectSync). Null offline.
        public Func<PNObjectSyncService> ObjectSyncProvider;
        private PNObjectSyncService objects;
        private ObjectTransport objectTransport;
        // Client: the baseline of a world that is still arriving.
        private readonly Dictionary<Guid, List<PNObjectRecord>> clientObjectBaselines = new Dictionary<Guid, List<PNObjectRecord>>();
        // Evidence for the object channel (plan O6): what the host sent and how often a baseline was built, how often the queue overflowed, how many baselines this client applied.
        public long ObjectBytesSent { get; private set; }
        public int ObjectPacketsSent { get; private set; }
        public int ObjectRecordsSent { get; private set; }
        public int ObjectOverflows { get; private set; }
        public int ObjectBaselinesSent { get; private set; }
        public int ObjectBaselinesApplied { get; private set; }
        // Maintenance: every observer gets a fresh baseline of the living objects (what a late joiner or an overflow gets).
        public void RebaselineObjects() { foreach (var entry in worldPeers.Values) if (!(entry.Connection is LocalConnectionToClient)) entry.ObjectOverflow = true; }
        public int ObjectRecordsQueued => worldPeers.Values.Sum(x => x.ObjectQueue.Count + x.ObjectBaseline.Count);
        public PNObjectSyncService ObjectSyncService => objects;

        private void InitializeObjectSync()
        {
            objects = ObjectSyncProvider?.Invoke();
            if (objects != null && objects.IsAuthority) { objectTransport = new ObjectTransport(this); objects.Transport = objectTransport; }
        }
        private void DisposeObjectSync()
        {
            if (objects != null && ReferenceEquals(objects.Transport, objectTransport)) objects.Transport = null;
            objectTransport = null; objects = null; clientObjectBaselines.Clear();
        }

        private sealed class ObjectTransport : IPNObjectSyncTransport
        {
            private readonly PNNetworkManager owner;
            public ObjectTransport(PNNetworkManager owner) { this.owner = owner; }
            public void Send(PNObjectRecord record) => owner.QueueObject(record);
        }

        // Only projectiles have a client sink yet (areas and blasts: stage O5), so only they travel.
        private static bool ObjectTravels(PNObjectKind kind) => kind == PNObjectKind.Projectile;

        // ---- host ----
        private void QueueObject(PNObjectRecord record)
        {
            if (!NetworkServer.active || !IsPlaying || stopping || record == null || !ObjectTravels(record.Kind)) return;
            foreach (var entry in worldPeers.Values)
            {
                if (entry.Connection is LocalConnectionToClient || entry.Closing || !entry.Loaded || !entry.ObjectBaselined || entry.World == null) continue;
                if (entry.World.InstanceId != record.World.Instance || entry.World.Incarnation != record.World.Incarnation) continue;
                if (entry.ObjectQueue.Count >= PNObjectLimits.QueueRecords) { if (!entry.ObjectOverflow) ObjectOverflows++; entry.ObjectOverflow = true; continue; }
                entry.ObjectQueue.Add(record);
            }
        }

        // Called every frame by the host's pump (after the sync layer's).
        private void PumpObjectSync()
        {
            if (objects == null || !NetworkServer.active || !objects.IsAuthority || stopping) return;
            foreach (var entry in worldPeers.Values.ToArray())
            {
                if (entry.Connection is LocalConnectionToClient || entry.Closing || !entry.Loaded || entry.World == null) continue;
                if (entry.World.State != EveWorldState.Ready && entry.World.State != EveWorldState.Paused) continue;
                if (!NetworkServer.connections.ContainsKey(entry.Connection.connectionId)) continue;
                try { PumpObjects(entry); }
                catch (Exception error) { LastFailure = "Object channel failed: " + error.Message; UnsubscribeWorld(entry.Connection, entry.World.InstanceId); }
            }
        }
        private void PumpObjects(WorldSubscription entry)
        {
            if (entry.ObjectOverflow) { entry.ObjectOverflow = false; entry.ObjectBaselined = false; entry.ObjectQueue.Clear(); entry.ObjectBaseline.Clear(); }
            if (!entry.ObjectBaselined)
            {
                entry.ObjectBaseline.Clear(); entry.ObjectBaseline.AddRange(objects.Baseline(PNObjectWorld.Of(entry.World)).Where(x => ObjectTravels(x.Kind)));
                entry.ObjectBaselined = true; ObjectBaselinesSent++; entry.ObjectBaselineStarted = false; entry.ObjectBaselineDone = false; entry.ObjectQueue.Clear();
            }
            int connection = entry.Connection.connectionId;
            for (int packets = 0; packets < PNObjectLimits.PacketsPerFrame; packets++)
            {
                bool baseline = !entry.ObjectBaselineDone; var source = baseline ? entry.ObjectBaseline : entry.ObjectQueue;
                if (!baseline && source.Count == 0) return;
                int take = 0, bytes = 0;
                while (take < source.Count && take < PNObjectLimits.RecordsPerPacket && (take == 0 || bytes + source[take].EstimatedBytes <= PNObjectLimits.PacketBudget)) bytes += source[take++].EstimatedBytes;
                byte flags = 0;
                if (baseline) { flags = PNObjectPacketFlags.Baseline; if (!entry.ObjectBaselineStarted) flags |= PNObjectPacketFlags.Start; if (take == source.Count) flags |= PNObjectPacketFlags.End; }
                var packet = new PNObjectPacket { Ticket = entry.Offer.Ticket, Flags = flags, Records = source.GetRange(0, take).ToArray() };
                int wire = 0;
                using (var writer = NetworkWriterPool.Get())
                {
                    writer.WriteObjectPacket(packet);
                    if (writer.Position > NetworkMessages.MaxContentSize(Channels.Reliable)) throw new FormatException("Object packet exceeds the configured channel.");
                    wire = writer.Position;
                    if (!TakeSendBudget(connection, writer.Position + 16)) return;
                }
                ObjectBytesSent += wire; ObjectPacketsSent++; ObjectRecordsSent += take;
                entry.Connection.Send(packet, Channels.Reliable); source.RemoveRange(0, take);
                if (baseline) { entry.ObjectBaselineStarted = true; if ((flags & PNObjectPacketFlags.End) != 0) entry.ObjectBaselineDone = true; }
            }
        }

        // ---- client ----
        private void ReceiveObjectPacket(PNObjectPacket packet)
        {
            if (stopping || !IsPlaying || NetworkServer.active || objects == null || objects.IsAuthority || !MatchTicket(packet.Ticket)) return;
            // Stale: the subscription it belongs to was replaced or closed. Dropped, never a disconnect.
            if (!clientWorlds.TryGetValue(packet.Ticket.World, out var entry) || !entry.Loaded || entry.Closing || entry.World == null || entry.World.Incarnation != packet.Ticket.Incarnation || entry.Offer.Ticket.Subscription != packet.Ticket.Subscription) return;
            try
            {
                var world = new PNObjectWorld(packet.Ticket.World, packet.Ticket.Incarnation);
                if ((packet.Flags & PNObjectPacketFlags.Baseline) == 0) { foreach (var record in packet.Records) objects.Receive(record); return; }
                if ((packet.Flags & PNObjectPacketFlags.Start) != 0) clientObjectBaselines[packet.Ticket.World] = new List<PNObjectRecord>();
                if (!clientObjectBaselines.TryGetValue(packet.Ticket.World, out var parts)) return;
                parts.AddRange(packet.Records);
                if ((packet.Flags & PNObjectPacketFlags.End) == 0) return;
                clientObjectBaselines.Remove(packet.Ticket.World); objects.ApplyBaseline(world, parts); ObjectBaselinesApplied++;
            }
            catch (Exception error) { ProtocolFailure(error, false); }
        }

        // A client world is closing: its copies and half baseline are over.
        private void ForgetClientObjects(Guid world, ulong incarnation)
        {
            clientObjectBaselines.Remove(world);
            if (objects == null || objects.IsAuthority) return;
            try { objects.ReleaseWorld(new PNObjectWorld(world, incarnation)); } catch (Exception error) { UnityEngine.Debug.LogWarning("Object forget failed: " + error.Message); }
        }
    }
}
