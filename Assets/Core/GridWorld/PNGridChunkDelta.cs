using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

// EN: The two helpers of the chunk channel that G8b adds (Docs/SandboxPlan.md). A DELTA brings a chunk the client holds from one content revision to the current one: the cells that changed in between, each with
//     the value it has NOW (state, not commands: a cell edited ten times is one entry), and the checksum of the whole chunk afterwards, so a client that ends up with different cells knows it and asks for the chunk
//     again. The BLOB CACHE keeps, per world, the newest blob of each chunk (up to 4 MiB, least recently used out first) so a blast that three clients need is compressed once. Pure C#.
// VI: Hai phần G8b của kênh mảnh. DELTA đưa mảnh client đang giữ từ một revision nội dung lên hiện tại: các ô đã đổi, mỗi ô với giá trị BÂY GIỜ (trạng thái, không phải lệnh) và checksum cả mảnh sau khi áp để client
//     phát hiện lệch và xin lại cả mảnh. BỘ NHỚ ĐỆM BLOB giữ blob mới nhất của mỗi mảnh theo world (tới 4 MiB, bỏ cái ít dùng nhất) để một vụ nổ ba client cần chỉ nén một lần.
public static class PNGridChunkDelta
{
    public const byte Format = 1;
    public const int MaxEntries = PNGridChunkJournal.Capacity;

    // Builds the delta from content revision `from` to the current one; false when the journal does not reach back that far (the caller sends the whole chunk). `scratch` is a reusable list.
    public static bool TryBuild(PNGridWorldData data, int index, int from, List<int> scratch, out byte[] payload, out int revision)
    {
        payload = null; revision = data.ChunkRevisionAt(index);
        if (!data.TryChangedCells(index, from, scratch) || scratch.Count == 0) return false;
        int cx = index % data.ChunksX, cz = index / data.ChunksX, c = PNGridWorldLimits.ChunkCells, g = data.GroundPerChunk;
        int size = 3 + 4;
        foreach (int key in scratch) size += 3 + Width(PNGridChunkJournal.KindOf(key));
        var bytes = new byte[size]; int at = 0;
        bytes[at++] = Format; bytes[at++] = (byte)scratch.Count; bytes[at++] = (byte)(scratch.Count >> 8);
        foreach (int key in scratch)
        {
            var kind = PNGridChunkJournal.KindOf(key); int local = PNGridChunkJournal.LocalOf(key);
            bytes[at++] = (byte)kind; bytes[at++] = (byte)local; bytes[at++] = (byte)(local >> 8);
            switch (kind)
            {
                case PNGridJournalKind.Solid: bytes[at++] = data.IsSolid(cx * c + local % c, cz * c + local / c) ? (byte)1 : (byte)0; break;
                case PNGridJournalKind.Ground: { int v = data.GroundCell(cx * g + local % g, cz * g + local / g); bytes[at++] = (byte)v; bytes[at++] = (byte)(v >> 8); break; }
                default: { int v = data.HeightRaw(cx * c + local % c, cz * c + local / c); bytes[at++] = (byte)v; bytes[at++] = (byte)(v >> 8); break; }
            }
        }
        uint sum = PNGridChunkCopy.Checksum(data, index);
        bytes[at++] = (byte)sum; bytes[at++] = (byte)(sum >> 8); bytes[at++] = (byte)(sum >> 16); bytes[at++] = (byte)(sum >> 24);
        payload = bytes; return true;
    }

    private static int Width(PNGridJournalKind kind) => kind == PNGridJournalKind.Solid ? 1 : 2;

    // Checks the whole payload first (a bad one changes nothing), then writes the cells into the world; `expected` is the checksum the chunk must have afterwards.
    public static void Apply(PNGridWorldData data, int index, byte[] payload, out uint expected)
    {
        if (payload == null || payload.Length < 3 + 4 + 4 || payload[0] != Format) throw new FormatException("A chunk delta has an unknown format.");
        int count = payload[1] | payload[2] << 8;
        if (count < 1 || count > MaxEntries) throw new FormatException("A chunk delta holds 1 to " + MaxEntries + " cells.");
        int cx = index % data.ChunksX, cz = index / data.ChunksX, c = PNGridWorldLimits.ChunkCells, g = data.GroundPerChunk, at = 3;
        for (int i = 0; i < count; i++)
        {
            if (at + 3 > payload.Length) throw new FormatException("A chunk delta is cut off.");
            if (payload[at] > (byte)PNGridJournalKind.Height) throw new FormatException("A chunk delta holds an unknown cell kind.");
            var kind = (PNGridJournalKind)payload[at]; int local = payload[at + 1] | payload[at + 2] << 8; at += 3;
            if (local >= (kind == PNGridJournalKind.Ground ? g * g : c * c)) throw new FormatException("A chunk delta cell is outside its chunk.");
            if (at + Width(kind) > payload.Length) throw new FormatException("A chunk delta is cut off.");
            if (kind == PNGridJournalKind.Solid && payload[at] > 1) throw new FormatException("A solid flag is 0 or 1.");
            if (kind == PNGridJournalKind.Ground && (payload[at] | payload[at + 1] << 8) >= data.Palette.Count) throw new FormatException("A chunk delta names a ground type that is not in the palette.");
            at += Width(kind);
        }
        if (at + 4 != payload.Length) throw new FormatException("A chunk delta has the wrong length.");
        expected = (uint)(payload[at] | payload[at + 1] << 8 | payload[at + 2] << 16 | payload[at + 3] << 24);
        at = 3;
        for (int i = 0; i < count; i++)
        {
            var kind = (PNGridJournalKind)payload[at]; int local = payload[at + 1] | payload[at + 2] << 8; at += 3;
            switch (kind)
            {
                case PNGridJournalKind.Solid: data.SetSolid(cx * c + local % c, cz * c + local / c, payload[at] != 0, false); at += 1; break;
                case PNGridJournalKind.Ground: data.SetGroundCell(cx * g + local % g, cz * g + local / g, (ushort)(payload[at] | payload[at + 1] << 8)); at += 2; break;
                default: data.SetHeightRaw(cx * c + local % c, cz * c + local / c, (short)(payload[at] | payload[at + 1] << 8)); at += 2; break;
            }
        }
    }
}

// The newest blob of each chunk of one world, shared by every client the host streams it to.
public sealed class PNGridChunkBlobCache
{
    private static readonly ConditionalWeakTable<PNGridWorldData, PNGridChunkBlobCache> caches = new ConditionalWeakTable<PNGridWorldData, PNGridChunkBlobCache>();
    private readonly PNGridWorldData data;
    private readonly int maxBytes;
    private readonly Dictionary<int, LinkedListNode<Entry>> entries = new Dictionary<int, LinkedListNode<Entry>>();
    private readonly LinkedList<Entry> order = new LinkedList<Entry>();
    private struct Entry { public int Index, Revision; public byte[] Blob; }

    public int Bytes { get; private set; }
    public int Count => entries.Count;
    public long Hits { get; private set; }
    public long Misses { get; private set; }

    public PNGridChunkBlobCache(PNGridWorldData data, int maxBytes = PNGridChunkLimits.BlobCacheBytes)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        if (maxBytes < PNGridSaveLimits.MaxChunkBlobBytes) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        this.maxBytes = maxBytes;
    }

    public static PNGridChunkBlobCache For(PNGridWorldData data) => caches.GetValue(data, d => new PNGridChunkBlobCache(d));

    // The blob of the chunk as it is now, and the content revision it belongs to.
    public byte[] Blob(int index, out int revision)
    {
        revision = data.ChunkRevisionAt(index);
        if (entries.TryGetValue(index, out var node))
        {
            if (node.Value.Revision == revision) { order.Remove(node); order.AddFirst(node); Hits++; return node.Value.Blob; }
            Bytes -= node.Value.Blob.Length; order.Remove(node); entries.Remove(index);
        }
        Misses++;
        var copy = PNGridChunkCopy.Capture(data, index % data.ChunksX, index / data.ChunksX);
        byte[] blob;
        try { blob = copy.Encode(); } finally { copy.Release(); }
        entries[index] = order.AddFirst(new Entry { Index = index, Revision = revision, Blob = blob }); Bytes += blob.Length;
        while (Bytes > maxBytes && order.Count > 1)
        {
            var last = order.Last; order.RemoveLast(); entries.Remove(last.Value.Index); Bytes -= last.Value.Blob.Length;
        }
        return blob;
    }
}
