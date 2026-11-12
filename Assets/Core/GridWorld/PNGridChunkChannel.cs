using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: The data side of the chunk channel (Docs/SandboxPlan.md, step G8): what the host sends a client and what the client holds until the world applies it. Pure C#, no Mirror and no Unity: the wire
//     messages and the pump are in Assets/Coop, the applying side is in Assets/Game/GridWorld. A client never generates the ground; it builds an empty world of the host's size and receives the chunks
//     the host chooses for it: the ones around its own players first, then the ring around them, and again any chunk it already has that changed (the whole chunk, a blob of tens of bytes to about 3 KB, not
//     a list of cell edits: it cannot disagree with the host's cells and needs no ordering between edits and chunks). The chunk blobs are the ones of the save (`PNGridChunkCopy.Encode`).
// VI: Phần dữ liệu của kênh mảnh (G8): host gửi gì cho client và client giữ gì cho tới khi world áp. Thuần C#, không Mirror, không Unity. Client không sinh nền: dựng world rỗng đúng cỡ host rồi nhận các mảnh
//     host chọn (quanh người chơi của nó trước, rồi vòng ngoài, và gửi lại cả mảnh nếu mảnh đã gửi bị đổi). Blob mảnh là blob của file lưu.
// G8b (Docs/SandboxPlan.md): a chunk the client already holds is brought up to date with a DELTA (the cells that changed, read from the world's journal, with the checksum of the chunk afterwards) when that is smaller
// than the whole chunk, and with the whole chunk otherwise. Both are STATE, not commands: whatever happened in between, the host sends what the chunk is now, so edits that pile up before a packet leaves are one delta.
// A client that sees a checksum or base revision that does not match asks for the whole chunk again (`PNGridChunkInbox.TakeResyncs`).
public static class PNGridChunkLimits
{
    // One packet carries whole chunks up to this many payload bytes (a chunk blob is at most about 3.4 KB, so a packet always holds at least four).
    public const int PacketBytes = 16384, MaxChunksPerPacket = 64;
    // The per-peer limits of the host (the adapter's shared `TakeSendBudget` still applies on top).
    public const int ChunksPerFrame = 16, BytesPerFrame = 32 * 1024, PacketsPerFrame = 4;
    // The client keeps at most this many bytes of blobs that are waiting to be applied.
    public const int InboxBytes = 8 << 20;
    // Chunks (not cells) around a player: the set the client needs before its world counts as ready, and the ring that follows.
    public const int InitialRadius = 3, InterestRadius = 8;
    // A mobile client refuses a world with more cells than this (512 x 512), with a message.
    public const long MobileMaxCells = 512L * 512L;
    public const int DescriptorBytes = 1024;
    // A delta this small is sent without even looking at the whole chunk's blob; a larger one is compared with it.
    public const int SmallDeltaBytes = 256;
    // Shared blob cache of a world (one blob, the newest, per chunk), the longest the host holds updates back while a batch of edits is still being applied, and the resync requests per packet.
    public const int BlobCacheBytes = 4 << 20, MaxResyncPerPacket = 64;
    public const double SettleSeconds = 0.5;
}

public struct PNGridChunkItem
{
    // Version is the content revision of the chunk (`PNGridWorldData.ChunkRevisionAt`) the item brings the client to. A whole chunk (the default) has the G7 blob in Blob; a delta (IsDelta) has a `PNGridChunkDelta` payload and Base,
    // the revision the client must hold for it to apply.
    public int Index, Version, Base;
    public bool IsDelta;
    public byte[] Blob;
}

// What a host keeps for ONE client of ONE grid world: which version of each chunk the client has, and what to send next.
public sealed class PNGridChunkSender
{
    private readonly PNGridWorldData data;
    private readonly int[] sent;
    private readonly bool[] lost;
    private readonly PNGridChunkBlobCache cache;
    private readonly List<int> scratch = new List<int>();
    private readonly int initialRadius, interestRadius;
    private bool[] initial;
    private int initialRemaining;
    private bool finalSent;
    private readonly List<PNGridChunkItem> held = new List<PNGridChunkItem>();
    private bool heldFinal;
    private readonly List<int> candidates = new List<int>();
    private readonly List<long> keys = new List<long>();

    public PNGridChunkSender(PNGridWorldData data, int initialRadius = PNGridChunkLimits.InitialRadius, int interestRadius = PNGridChunkLimits.InterestRadius)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        if (initialRadius < 0 || interestRadius < initialRadius) throw new ArgumentOutOfRangeException(nameof(interestRadius));
        this.initialRadius = initialRadius; this.interestRadius = interestRadius;
        sent = new int[data.ChunkCount]; lost = new bool[sent.Length];
        for (int i = 0; i < sent.Length; i++) sent[i] = -1;
        data.EnableJournal(); cache = PNGridChunkBlobCache.For(data);
    }

    // The client has every chunk of the first set (the ones around its players when the stream started) and has been told so.
    public bool InitialDone => finalSent;
    public int ChunksSent { get; private set; }
    public int PacketsSent { get; private set; }
    public long BytesSent { get; private set; }
    public int DeltasSent { get; private set; }
    public int FullsSent { get; private set; }
    public bool HasHeldPacket => held.Count > 0;
    public int SentVersion(int index) => sent[index];
    // True when the client holds this chunk at the version the host has now.
    public bool IsCurrent(int index) => sent[index] == data.ChunkRevisionAt(index);
    // The client said it holds a wrong copy of this chunk (a delta failed its checksum): the whole chunk goes out again, wherever it is.
    public void MarkLost(int index) { if (index < 0 || index >= sent.Length) throw new ArgumentOutOfRangeException(nameof(index)); sent[index] = -1; lost[index] = true; }

    // The next packet: up to `maxChunks` chunks and `maxBytes` of blob bytes (the first chunk is always taken), nearest to `focus` first. `focus` holds world positions in metres (x, z); with none the world's
    // middle is used. Nothing counts as sent until `Commit`: a packet the transport did not take is returned again, the same one, by the next call. `deadline` (a Stopwatch tick count, 0 = none) stops the
    // encoding early so a frame is never held by a long list. Returns false when there is nothing to send.
    public bool Collect(IReadOnlyList<(float X, float Z)> focus, int maxChunks, int maxBytes, long deadline, out List<PNGridChunkItem> items, out bool final)
    {
        if (held.Count > 0) { items = held; final = heldFinal; return true; }
        items = held; final = false;
        if (maxChunks < 1 || maxBytes < 1) return false;
        var chunkFocus = new List<(int X, int Z)>(focus == null ? 1 : Math.Max(1, focus.Count));
        float size = data.CellSize * PNGridWorldLimits.ChunkCells;
        if (focus != null) foreach (var point in focus) chunkFocus.Add((Clamp((int)Math.Floor(point.X / size), data.ChunksX), Clamp((int)Math.Floor(point.Z / size), data.ChunksZ)));
        if (chunkFocus.Count == 0) chunkFocus.Add((data.ChunksX / 2, data.ChunksZ / 2));
        if (initial == null) StartInitial(chunkFocus);

        candidates.Clear(); keys.Clear();
        // While a batch of edits is being applied the updates of chunks the client has wait a moment, so a blast arrives as one piece (a bound, not a promise: SettleSeconds later they go out as they are).
        bool holding = data.HoldsUpdates(System.Diagnostics.Stopwatch.GetTimestamp());
        for (int i = 0; i < sent.Length; i++)
        {
            int revision = data.ChunkRevisionAt(i);
            if (sent[i] == revision) continue;
            if (holding && sent[i] >= 0) continue;
            int distance = Distance(i, chunkFocus);
            // A chunk the client has is kept right wherever it is; one it does not have is sent only inside the ring.
            if (sent[i] < 0 && !lost[i] && distance > interestRadius && !initial[i]) continue;
            candidates.Add(i); keys.Add(((long)distance << 32) | (uint)i);
        }
        if (candidates.Count == 0) return false;

        // The nearest first, by selection: a packet takes a few chunks and the list can hold thousands.
        int taken = 0, bytes = 0, remaining = initialRemaining;
        while (taken < maxChunks && taken < PNGridChunkLimits.MaxChunksPerPacket && keys.Count > 0)
        {
            if (taken > 0 && deadline != 0 && System.Diagnostics.Stopwatch.GetTimestamp() > deadline) break;
            int best = 0;
            for (int k = 1; k < keys.Count; k++) if (keys[k] < keys[best]) best = k;
            int index = (int)(keys[best] & 0xffffffffL);
            keys[best] = keys[keys.Count - 1]; keys.RemoveAt(keys.Count - 1);
            var item = Build(index);
            if (taken > 0 && bytes + item.Blob.Length > maxBytes) break;
            held.Add(item);
            bytes += item.Blob.Length; taken++;
            if (!finalSent && initial[index] && sent[index] < 0) remaining--;
        }
        if (held.Count == 0) return false;
        heldFinal = !finalSent && remaining == 0;
        items = held; final = heldFinal;
        return true;
    }

    // The held packet went out.
    public void Commit()
    {
        if (held.Count == 0) throw new InvalidOperationException("There is no packet to commit.");
        long bytes = 0;
        foreach (var item in held)
        {
            if (!finalSent && initial[item.Index] && sent[item.Index] < 0) initialRemaining--;
            sent[item.Index] = item.Version; lost[item.Index] = false; bytes += item.Blob.Length; ChunksSent++;
            if (item.IsDelta) DeltasSent++; else FullsSent++;
        }
        if (heldFinal) finalSent = true;
        BytesSent += bytes; PacketsSent++;
        held.Clear(); heldFinal = false;
    }

    // Drops a held packet that could not be sent (the connection went away); the chunks are offered again.
    public void Discard() { held.Clear(); heldFinal = false; }

    // A delta when the journal covers what the client misses and it is smaller than the whole chunk; the whole chunk (from the shared cache) otherwise.
    private PNGridChunkItem Build(int index)
    {
        int have = sent[index];
        if (have >= 0 && PNGridChunkDelta.TryBuild(data, index, have, scratch, out var payload, out int revision))
        {
            if (payload.Length <= PNGridChunkLimits.SmallDeltaBytes) return new PNGridChunkItem { Index = index, Version = revision, Base = have, IsDelta = true, Blob = payload };
            var whole = cache.Blob(index, out int wholeRevision);
            if (payload.Length < whole.Length) return new PNGridChunkItem { Index = index, Version = revision, Base = have, IsDelta = true, Blob = payload };
            return new PNGridChunkItem { Index = index, Version = wholeRevision, Blob = whole };
        }
        var blob = cache.Blob(index, out int version);
        return new PNGridChunkItem { Index = index, Version = version, Blob = blob };
    }

    private void StartInitial(List<(int X, int Z)> chunkFocus)
    {
        initial = new bool[sent.Length];
        for (int z = 0; z < data.ChunksZ; z++) for (int x = 0; x < data.ChunksX; x++)
        {
            int best = int.MaxValue;
            foreach (var f in chunkFocus) best = Math.Min(best, Math.Max(Math.Abs(x - f.X), Math.Abs(z - f.Z)));
            if (best <= initialRadius) { initial[z * data.ChunksX + x] = true; initialRemaining++; }
        }
    }

    // Chebyshev distance in chunks to the nearest focus point: the ring around a player is a square, like the chunks.
    private int Distance(int index, List<(int X, int Z)> chunkFocus)
    {
        int cx = index % data.ChunksX, cz = index / data.ChunksX, best = int.MaxValue;
        foreach (var f in chunkFocus) best = Math.Min(best, Math.Max(Math.Abs(cx - f.X), Math.Abs(cz - f.Z)));
        return best;
    }

    private static int Clamp(int value, int count) => value < 0 ? 0 : value >= count ? count - 1 : value;
}

// What a client holds for one grid world: the descriptor the host offered, the blobs that arrived and have not been applied yet, and whether the first set is complete.
public sealed class PNGridChunkInbox
{
    private readonly Queue<PNGridChunkItem> queue = new Queue<PNGridChunkItem>();
    private int queuedBytes;
    private int[] holding;
    private readonly List<int> resyncQueue = new List<int>();
    private readonly HashSet<int> resyncPending = new HashSet<int>();

    public string Descriptor { get; private set; }
    // The world's shape, read from the descriptor.
    public PNGridWorldDescriptor Shape { get; private set; }
    public int ChunkCount { get; private set; }
    // The subscription of the host's offer this inbox belongs to; a newer offer resets it.
    public ulong Subscription { get; private set; }
    public bool InitialComplete { get; private set; }
    public int QueuedCount => queue.Count;
    public int QueuedBytes => queuedBytes;
    public long ChunksReceived { get; private set; }
    public long BytesReceived { get; private set; }
    public long DeltasApplied { get; private set; }
    public long ResyncsRequested { get; private set; }

    public void Declare(string descriptor, ulong subscription)
    {
        if (string.IsNullOrEmpty(descriptor) || descriptor.Length > PNGridChunkLimits.DescriptorBytes) throw new FormatException("A grid descriptor is 1 to " + PNGridChunkLimits.DescriptorBytes + " characters.");
        if (subscription == 0) throw new ArgumentOutOfRangeException(nameof(subscription));
        if (subscription < Subscription) return;
        ulong prior = Subscription;
        var shape = PNGridWorldDescriptor.Parse(JToken.Parse(descriptor));
        if (shape.Restore != null) throw new FormatException("A world offered to a client carries no restore token.");
        if (subscription > Subscription) { queue.Clear(); queuedBytes = 0; InitialComplete = false; ChunksReceived = 0; BytesReceived = 0; }
        Subscription = subscription; Descriptor = descriptor; Shape = shape; ChunkCount = shape.Width / PNGridWorldLimits.ChunkCells * (shape.Depth / PNGridWorldLimits.ChunkCells);
        if (holding == null || holding.Length != ChunkCount || subscription > prior) { holding = new int[ChunkCount]; for (int i = 0; i < holding.Length; i++) holding[i] = -1; resyncQueue.Clear(); resyncPending.Clear(); DeltasApplied = 0; ResyncsRequested = 0; }
    }

    // Keeps a packet's chunks (the inbox must have been declared). A packet that would put more than the bound into the inbox is a protocol failure (the host paces itself, so it cannot happen between honest peers).
    public void Offer(IReadOnlyList<PNGridChunkItem> items, bool final)
    {
        if (Shape == null) throw new InvalidOperationException("The inbox has no descriptor yet.");
        if (items == null || items.Count == 0 || items.Count > PNGridChunkLimits.MaxChunksPerPacket) throw new FormatException("A chunk packet holds 1 to " + PNGridChunkLimits.MaxChunksPerPacket + " chunks.");
        long add = 0;
        foreach (var item in items)
        {
            if (item.Index < 0 || item.Index >= ChunkCount || item.Version < 0 || item.IsDelta && (item.Base < 0 || item.Base >= item.Version) || item.Blob == null || item.Blob.Length < 6 || item.Blob.Length > PNGridSaveLimits.MaxChunkBlobBytes) throw new FormatException("A chunk packet holds an invalid chunk.");
            add += item.Blob.Length;
        }
        if (queuedBytes + add > PNGridChunkLimits.InboxBytes) throw new FormatException("The chunk inbox is full.");
        foreach (var item in items) queue.Enqueue(item);
        queuedBytes += (int)add; ChunksReceived += items.Count; BytesReceived += add;
        if (final) InitialComplete = true;
    }

    public bool TryTake(out PNGridChunkItem item)
    {
        if (queue.Count == 0) { item = default; return false; }
        item = queue.Dequeue(); queuedBytes -= item.Blob.Length; return true;
    }

    // Decodes up to `maxChunks` waiting chunks into the world (or fewer when `deadline`, a Stopwatch tick count, passes); returns how many.
    public int Apply(PNGridWorldData data, int maxChunks, long deadline = 0)
    {
        int done = 0;
        while (done < maxChunks && TryTake(out var item))
        {
            if (item.IsDelta) ApplyDelta(data, item);
            else
            {
                PNGridChunkCopy.Decode(item.Blob, data.GroundPerChunk, out var ground, out var height, out var solid);
                data.ImportChunk(item.Index % data.ChunksX, item.Index / data.ChunksX, ground, height, solid);
                holding[item.Index] = item.Version; resyncPending.Remove(item.Index);
            }
            done++;
            if (deadline != 0 && System.Diagnostics.Stopwatch.GetTimestamp() > deadline) break;
        }
        return done;
    }

    // A delta applies only on the revision it was made from; anything else (or a checksum that disagrees afterwards) asks the host for the whole chunk and ignores deltas until it comes.
    private void ApplyDelta(PNGridWorldData data, PNGridChunkItem item)
    {
        if (resyncPending.Contains(item.Index)) return;
        if (holding[item.Index] != item.Base) { RequestResync(item.Index); return; }
        PNGridChunkDelta.Apply(data, item.Index, item.Blob, out uint expected);
        if (PNGridChunkCopy.Checksum(data, item.Index) != expected) { RequestResync(item.Index); return; }
        holding[item.Index] = item.Version; DeltasApplied++;
    }
    private void RequestResync(int index) { if (resyncPending.Add(index)) { resyncQueue.Add(index); ResyncsRequested++; } }

    // The chunks whose copy is wrong and that the host has not been asked for yet (at most `max`); each is asked once until the whole chunk arrives.
    public int TakeResyncs(List<int> into, int max = PNGridChunkLimits.MaxResyncPerPacket)
    {
        int n = Math.Min(max, resyncQueue.Count);
        for (int i = 0; i < n; i++) into.Add(resyncQueue[i]);
        resyncQueue.RemoveRange(0, n); return n;
    }
    public int ResyncWaiting => resyncQueue.Count;
    // The revision of a chunk the client holds (-1: none); for tests.
    public int HeldRevision(int index) => holding == null ? -1 : holding[index];

    // The world has what the host called the first set, and nothing waits to be applied.
    public bool Ready => InitialComplete && queue.Count == 0;
}

// The inboxes of a session's client worlds, by world id. The network layer fills them (the offer brings the descriptor, packets bring chunks); the world provider reads them while it loads. They are weakly
// tied to the session, and the network layer removes one when its world closes.
public static class PNGridChunkInboxes
{
    private static readonly ConditionalWeakTable<EveSessionContext, Dictionary<Guid, PNGridChunkInbox>> tables = new ConditionalWeakTable<EveSessionContext, Dictionary<Guid, PNGridChunkInbox>>();
    public const int MaxWorlds = 8;

    public static PNGridChunkInbox Find(EveSessionContext session, Guid world)
        => session != null && tables.TryGetValue(session, out var table) && table.TryGetValue(world, out var inbox) ? inbox : null;

    public static PNGridChunkInbox Get(EveSessionContext session, Guid world)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        var table = tables.GetOrCreateValue(session);
        if (table.TryGetValue(world, out var inbox)) return inbox;
        if (table.Count >= MaxWorlds) throw new InvalidOperationException("A session holds at most " + MaxWorlds + " grid worlds.");
        inbox = new PNGridChunkInbox(); table.Add(world, inbox); return inbox;
    }

    public static void Remove(EveSessionContext session, Guid world)
    {
        if (session != null && tables.TryGetValue(session, out var table)) table.Remove(world);
    }
}
