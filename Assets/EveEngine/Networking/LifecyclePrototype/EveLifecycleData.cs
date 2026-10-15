using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

// Experimental step-2 model. Not connected to EveCoopSession or entity loaders.
namespace EveEngine.Experimental.Lifecycle
{
    public enum HostPhase { Handshake, WaitCapture, SendingSnapshot, AwaitApplied, Drain, Live, Closed, PreparingSnapshot }
    public enum ClientPhase { AwaitSnapshot, Assembling, Preparing, Live, Recovering, Closed }
    public enum MessageKind : byte { SnapshotStart = 1, SnapshotChunk, SnapshotEnd, Spawn, Updated, Despawn, Dormant, World, IntentResult }
    public enum ErrorCode : ushort
    {
        UnsupportedProtocol = 1001, ContentMismatch = 1002, HandshakeTimeout = 1003, PolicyMismatch = 1004,
        EntityPrepareFailed = 2001, EntityPreparationTimeout = 2002, EntityCommitFailed = 2003, InvalidEntityLifecycle = 2004,
        QueueLimitExceeded = 3001, SnapshotTimeout = 3002, RecoveryExhausted = 3003, SequenceGap = 3004,
        SnapshotInvalid = 3005, InboundRateExceeded = 3006, SnapshotAppliedTimeout = 3007,
        ResidentBudgetExceeded = 3008, SnapshotTooLarge = 3009
    }

    public readonly struct PresenceKey : IEquatable<PresenceKey>
    {
        public long EntityId { get; }
        public long Generation { get; }
        public PresenceKey(long entityId, long generation)
        {
            if (entityId <= 0 || generation <= 0) throw new ArgumentOutOfRangeException();
            EntityId = entityId; Generation = generation;
        }
        public bool Equals(PresenceKey other) => EntityId == other.EntityId && Generation == other.Generation;
        public override bool Equals(object obj) => obj is PresenceKey other && Equals(other);
        public override int GetHashCode() => EntityId.GetHashCode() * 397 ^ Generation.GetHashCode();
        public override string ToString() => EntityId + ":" + Generation;
    }

    // Opaque, immutable subsystem sample. No gameplay schema is inferred from these bytes.
    public sealed class ActorState
    {
        private readonly byte[] data;
        public PresenceKey Key { get; }
        public bool IsDormant { get; }
        public byte[] Data => (byte[])data.Clone();
        internal byte[] Bytes => data;
        public ActorState(PresenceKey key, bool dormant, byte[] data)
            : this(key, dormant, data, false) { }
        // Only freshly encoded or already privately owned immutable buffers may be shared.
        internal ActorState(PresenceKey key, bool dormant, byte[] data, bool owned)
        {
            if (key.EntityId <= 0 || key.Generation <= 0) throw new ArgumentException("Invalid presence.");
            Key = key; IsDormant = dormant; this.data = owned ? data ?? throw new ArgumentNullException(nameof(data)) : (byte[])(data ?? throw new ArgumentNullException(nameof(data))).Clone();
        }
    }

    public readonly struct PreparationToken : IEquatable<PreparationToken>
    {
        public long Incarnation { get; }
        public long LoadId { get; }
        public PresenceKey Key { get; }
        public PreparationToken(long incarnation, long loadId, PresenceKey key)
        {
            if (incarnation <= 0 || loadId <= 0 || key.EntityId <= 0) throw new ArgumentOutOfRangeException();
            Incarnation = incarnation; LoadId = loadId; Key = key;
        }
        public bool Equals(PreparationToken other) => Incarnation == other.Incarnation && LoadId == other.LoadId && Key.Equals(other.Key);
        public override bool Equals(object obj) => obj is PreparationToken other && Equals(other);
        public override int GetHashCode() => Incarnation.GetHashCode() ^ LoadId.GetHashCode() ^ Key.GetHashCode();
    }

    // All limits are required test inputs; deliberately no production defaults.
    public sealed class ModelPolicy
    {
        public int MaximumActors { get; }
        public int MaximumSnapshotBytes { get; }
        public int MaximumPayloadBytes { get; }
        public int ChunkBytes { get; }
        public int MaximumChunks { get; }
        public int MaximumQueuedMessages { get; }
        public int MaximumQueuedBytes { get; }
        public int MaximumAcceptedBytes { get; }
        public int MaximumControlMessages { get; }
        public int DiagnosticHistoryCapacity { get; }
        public int MaximumRounds { get; }
        public double HandshakeMs { get; }
        public double AssemblyMs { get; }
        public double PreparationMs { get; }
        public double AppliedMs { get; }
        public double RoundMs { get; }
        public double CycleMs { get; }
        public ModelPolicy(int actors, int snapshotBytes, int payloadBytes, int chunkBytes, int chunks,
            int queuedMessages, int queuedBytes, int acceptedBytes, int controls, int historyCapacity, int rounds,
            double handshakeMs, double assemblyMs, double preparationMs, double appliedMs, double roundMs, double cycleMs)
        {
            if (actors < 1 || snapshotBytes < 4 || payloadBytes < 1 || chunkBytes < 1 || chunks < 1 ||
                queuedMessages < 1 || queuedBytes < 1 || acceptedBytes < 1 || controls < 1 || historyCapacity < 1 || rounds < 1) throw new ArgumentOutOfRangeException();
            foreach (double value in new[] { handshakeMs, assemblyMs, preparationMs, appliedMs, roundMs, cycleMs })
                if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0) throw new ArgumentOutOfRangeException();
            if ((long)chunks * chunkBytes < snapshotBytes || payloadBytes < 32 || payloadBytes > snapshotBytes || chunkBytes > payloadBytes ||
                acceptedBytes < (long)Envelope.HeaderBytes + payloadBytes || cycleMs < roundMs * rounds || roundMs < appliedMs) throw new ArgumentException("Inconsistent model capacity/deadline.");
            MaximumActors = actors; MaximumSnapshotBytes = snapshotBytes; MaximumPayloadBytes = payloadBytes;
            ChunkBytes = chunkBytes; MaximumChunks = chunks; MaximumQueuedMessages = queuedMessages;
            MaximumQueuedBytes = queuedBytes; MaximumAcceptedBytes = acceptedBytes; MaximumControlMessages = controls;
            DiagnosticHistoryCapacity = historyCapacity;
            MaximumRounds = rounds; HandshakeMs = handshakeMs; AssemblyMs = assemblyMs; PreparationMs = preparationMs;
            AppliedMs = appliedMs; RoundMs = roundMs; CycleMs = cycleMs;
        }
    }

    public sealed class Envelope
    {
        public const ushort Protocol = 1;
        // Header size includes the length prefix. Snapshot/body limits are separate from wire bytes.
        public const int HeaderBytes = 2 + 16 + 8 + 1 + 8 + 1 + 8 + 8 + 8 + 4 + 4 + 4;
        private readonly byte[] payload;
        internal bool IsOptional; // queue metadata only; never serialized
        public Guid Session { get; }
        public long Incarnation { get; }
        public long Sequence { get; }
        public MessageKind Kind { get; }
        public PresenceKey Key { get; }
        public long SnapshotId { get; }
        public int Index { get; }
        public int Count { get; }
        public byte[] Payload => (byte[])payload.Clone();
        internal byte[] Bytes => payload;
        public int WireBytes => checked(HeaderBytes + payload.Length);
        public Envelope(Guid session, long incarnation, long sequence, MessageKind kind, byte[] payload,
            PresenceKey key = default, long snapshotId = 0, int index = 0, int count = 0)
            : this(session, incarnation, sequence, kind, payload, key, snapshotId, index, count, false) { }
        internal Envelope(Guid session, long incarnation, long sequence, MessageKind kind, byte[] payload,
            PresenceKey key, long snapshotId, int index, int count, bool owned)
        {
            if (session == Guid.Empty || incarnation <= 0 || sequence < 0 || !Enum.IsDefined(typeof(MessageKind), kind)) throw new ArgumentException("Invalid envelope.");
            Session = session; Incarnation = incarnation; Sequence = sequence; Kind = kind; Key = key;
            SnapshotId = snapshotId; Index = index; Count = count; this.payload = owned ? payload ?? Array.Empty<byte>() : (byte[])(payload ?? Array.Empty<byte>()).Clone();
        }
        internal Envelope WithSequence(long sequence) => new Envelope(Session, Incarnation, sequence, Kind, payload, Key, SnapshotId, Index, Count, true);
    }

    public static class ModelCodec
    {
        public static byte[] Encode(Envelope packet)
        {
            var bytes = new byte[packet.WireBytes];
            using (var stream = new MemoryStream(bytes, 0, bytes.Length, true, true)) using (var w = new BinaryWriter(stream))
            {
                w.Write(Envelope.Protocol); w.Write(packet.Session.ToByteArray()); w.Write(packet.Incarnation); w.Write((byte)0);
                w.Write(packet.Sequence); w.Write((byte)packet.Kind); w.Write(packet.Key.EntityId); w.Write(packet.Key.Generation);
                w.Write(packet.SnapshotId); w.Write(packet.Index); w.Write(packet.Count); w.Write(packet.Bytes.Length); w.Write(packet.Bytes);
                return bytes;
            }
        }
        public static Envelope Decode(byte[] bytes, ModelPolicy policy)
        {
            if (bytes == null || bytes.Length < Envelope.HeaderBytes || bytes.Length > (long)Envelope.HeaderBytes + policy.MaximumPayloadBytes) throw new FormatException("Envelope size.");
            try
            {
                using (var stream = new MemoryStream(bytes, false)) using (var r = new BinaryReader(stream))
                {
                    if (r.ReadUInt16() != Envelope.Protocol) throw new FormatException("Protocol version.");
                    var session = new Guid(r.ReadBytes(16)); long incarnation = r.ReadInt64();
                    if (r.ReadByte() != 0) throw new FormatException("Stream id.");
                    long sequence = r.ReadInt64(); var kind = (MessageKind)r.ReadByte();
                    long id = r.ReadInt64(), gen = r.ReadInt64(), snapshot = r.ReadInt64(); int index = r.ReadInt32(), count = r.ReadInt32(), length = r.ReadInt32();
                    if (length < 0 || length > policy.MaximumPayloadBytes || length != stream.Length - stream.Position || sequence <= 0) throw new FormatException("Payload length/sequence.");
                    var key = id == 0 && gen == 0 ? default : new PresenceKey(id, gen);
                    return new Envelope(session, incarnation, sequence, kind, r.ReadBytes(length), key, snapshot, index, count);
                }
            }
            catch (Exception ex) when (ex is EndOfStreamException || ex is ArgumentException) { throw new FormatException("Malformed envelope.", ex); }
        }
        public static byte[] EncodeActors(IEnumerable<ActorState> actors, ModelPolicy policy)
        {
            var list = actors.OrderBy(x => x.Key.EntityId).ToArray();
            if (list.Length > policy.MaximumActors || list.Select(x => x.Key.EntityId).Distinct().Count() != list.Length) throw new FormatException("Snapshot actor count/duplicate.");
            long size = 4;
            foreach (var actor in list)
            {
                if (actor.Bytes.Length > policy.MaximumPayloadBytes) throw new FormatException("Actor payload.");
                size = checked(size + 21 + actor.Bytes.Length);
            }
            if (size > policy.MaximumSnapshotBytes) throw new FormatException("Snapshot body size.");
            using (var stream = new MemoryStream()) using (var w = new BinaryWriter(stream))
            {
                w.Write(list.Length);
                foreach (var actor in list) { w.Write(actor.Key.EntityId); w.Write(actor.Key.Generation); w.Write(actor.IsDormant); w.Write(actor.Bytes.Length); w.Write(actor.Bytes); }
                return stream.ToArray();
            }
        }
        public static IReadOnlyList<ActorState> DecodeActors(byte[] bytes, ModelPolicy policy)
        {
            if (bytes == null || bytes.Length > policy.MaximumSnapshotBytes) throw new FormatException("Snapshot size.");
            try
            {
                using (var stream = new MemoryStream(bytes, false)) using (var r = new BinaryReader(stream))
                {
                    int count = r.ReadInt32(); if (count < 0 || count > policy.MaximumActors) throw new FormatException("Actor count.");
                    var result = new List<ActorState>(); var ids = new HashSet<long>();
                    for (int i = 0; i < count; i++)
                    {
                        var key = new PresenceKey(r.ReadInt64(), r.ReadInt64()); byte flag = r.ReadByte(); int size = r.ReadInt32();
                        if (flag > 1 || !ids.Add(key.EntityId) || size < 0 || size > policy.MaximumPayloadBytes || size > stream.Length - stream.Position) throw new FormatException("Actor state.");
                        result.Add(new ActorState(key, flag == 1, r.ReadBytes(size)));
                    }
                    if (stream.Position != stream.Length) throw new FormatException("Trailing snapshot bytes.");
                    return result.AsReadOnly();
                }
            }
            catch (Exception ex) when (ex is EndOfStreamException || ex is ArgumentException) { throw new FormatException("Malformed snapshot.", ex); }
        }
        public static byte[] Hash(byte[] body) { using (var sha = SHA256.Create()) return sha.ComputeHash(body); }
    }
}
