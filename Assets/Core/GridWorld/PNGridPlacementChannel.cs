using System;
using System.Collections.Generic;

// EN: The data side of the placement stream (Docs/SandboxPlan.md, step G9c part 2): what the host tells a client about the placed things of a grid world, and what the client holds until its world applies it.
//     The cells of a thing that blocks the way already reach the client as chunks; this stream carries the LIST (id, definition, cell, direction, health, key) so a client can draw markers and make the world
//     objects of interactable things. It is state, not commands: the host compares what the client was told with what it holds now and sends the difference (Add, Remove, Health). Only things inside the client's
//     interest ring (the chunk ring of the chunk channel, plus one chunk of slack before a thing is taken away again) are sent; a removal is sent whether or not the client was told about the thing, because a
//     client may have built it from the map by itself. Pure C#: no Mirror, no Unity. The wire message and the pump are in Assets/Coop, the applying side in Assets/Game/GridWorld.
// VI: Phần dữ liệu của luồng vật đặt (G9c phần 2): host báo gì cho client về vật đặt của world lưới. Ô của vật chắn đường đã tới client qua mảnh; luồng này mang DANH SÁCH để client vẽ marker và dựng world object cho vật
//     tương tác. Là trạng thái, không phải lệnh: host so điều client đã biết với điều host đang có và gửi phần chênh (Add, Remove, Health), chỉ cho vật trong vòng quan tâm; lệnh gỡ luôn được gửi.
public static class PNGridPlacementLimits
{
    // Items per packet, packets per peer per frame, and the most a client keeps waiting to be applied (a bound, not a promise: an honest host paces itself).
    public const int MaxItemsPerPacket = 64, PacketsPerFrame = 2, MaxQueued = 8192;
    public const int DefinitionChars = 96;
    // A thing leaves the client's list only when it is this many chunks beyond the ring it was sent in.
    public const int Slack = 1;
    // The longest a host waits between two scans of the placed things when they changed (a scan costs one pass over the list).
    public const double ScanSeconds = 0.1;
}

public enum PNGridPlacementKind : byte { Add = 0, Remove = 1, Health = 2 }

public struct PNGridPlacementItem
{
    public PNGridPlacementKind Kind;
    public int Id;
    // Add only.
    public string Definition;
    public int X, Z, Direction;
    public string Key;
    // Add and Health.
    public int Health;

    public static void Validate(PNGridPlacementItem item)
    {
        if (item.Kind > PNGridPlacementKind.Health) throw new FormatException("A placement item kind is 0 to 2.");
        if (item.Id < 1) throw new FormatException("A placement item needs a positive id.");
        if (item.Kind == PNGridPlacementKind.Remove) return;
        if (item.Health < 0 || item.Health > PNPlaceableLimits.MaxHealth) throw new FormatException("A placement health is 0 to " + PNPlaceableLimits.MaxHealth + ".");
        if (item.Kind == PNGridPlacementKind.Health) return;
        if (string.IsNullOrEmpty(item.Definition) || item.Definition.Length > PNGridPlacementLimits.DefinitionChars) throw new FormatException("A placement definition is 1 to " + PNGridPlacementLimits.DefinitionChars + " characters.");
        if (item.X < 0 || item.Z < 0 || item.X >= PNGridWorldLimits.MaxCells || item.Z >= PNGridWorldLimits.MaxCells) throw new FormatException("A placement cell is inside the largest world.");
        if (item.Direction < 0 || item.Direction > 3) throw new FormatException("A placement direction is 0 to 3.");
        if (item.Key != null && item.Key.Length > PNSyncLimits.KeyChars) throw new FormatException("A placement key is at most " + PNSyncLimits.KeyChars + " characters.");
    }
}

// What a host keeps for ONE client of ONE grid world: which things it was told about (and their health), and what to send next.
public sealed class PNGridPlacementSender
{
    private readonly PNGridPlacements placements;
    private readonly PNGridWorldData data;
    private readonly Dictionary<int, int> known = new Dictionary<int, int>();
    private readonly List<PNGridPlacementItem> pending = new List<PNGridPlacementItem>();
    private int pendingFront;
    private readonly List<PNGridPlacementItem> held = new List<PNGridPlacementItem>();
    private readonly List<int> removed = new List<int>();
    private readonly List<(int Distance, int Id)> adds = new List<(int, int)>();
    private int scanRevision = -1;
    private long scanFocusKey;
    private long scanTime;
    private bool started;

    public PNGridPlacementSender(PNGridPlacements placements, PNGridWorldData data)
    {
        this.placements = placements ?? throw new ArgumentNullException(nameof(placements)); this.data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public int KnownCount => known.Count;
    public int ItemsSent { get; private set; }
    public int PacketsSent { get; private set; }
    public bool HasWork => held.Count > 0 || pendingFront < pending.Count;
    public bool Knows(int id) => known.ContainsKey(id);

    // The next packet: up to `maxItems` items, removals first, then new things nearest to the client first, then health changes. Nothing counts as sent until `Commit`; a packet the transport did not take is returned
    // again by the next call. `focus` holds world positions in metres (x, z); with none the world's middle is used. `now` is a Stopwatch tick count. False when there is nothing to send.
    public bool Collect(IReadOnlyList<(float X, float Z)> focus, int maxItems, long now, out List<PNGridPlacementItem> items)
    {
        items = held;
        if (held.Count > 0) return true;
        if (maxItems < 1) return false;
        if (pendingFront >= pending.Count)
        {
            pending.Clear(); pendingFront = 0;
            Scan(focus, now);
            if (pending.Count == 0) return false;
        }
        int take = Math.Min(maxItems, Math.Min(PNGridPlacementLimits.MaxItemsPerPacket, pending.Count - pendingFront));
        for (int i = 0; i < take; i++) held.Add(pending[pendingFront + i]);
        return true;
    }

    // The held packet went out.
    public void Commit()
    {
        if (held.Count == 0) throw new InvalidOperationException("There is no packet to commit.");
        foreach (var item in held)
        {
            if (item.Kind == PNGridPlacementKind.Remove) known.Remove(item.Id); else known[item.Id] = item.Health;
        }
        pendingFront += held.Count; ItemsSent += held.Count; PacketsSent++; held.Clear();
    }

    // Drops what is held and queued (the connection went away or the client asked to start over); the next scan offers everything again.
    public void Discard() { held.Clear(); pending.Clear(); pendingFront = 0; scanRevision = -1; known.Clear(); started = false; }

    private void Scan(IReadOnlyList<(float X, float Z)> focus, long now)
    {
        var chunks = FocusChunks(focus, out long focusKey);
        bool first = !started;
        if (!first && placements.Revision == scanRevision && focusKey == scanFocusKey) return;
        if (!first && now - scanTime < (long)(PNGridPlacementLimits.ScanSeconds * System.Diagnostics.Stopwatch.Frequency)) return;
        removed.Clear(); adds.Clear();
        int fromRevision = scanRevision; scanRevision = placements.Revision; scanFocusKey = focusKey; scanTime = now; started = true;

        // Removals: everything that left the list since the last scan, told or not; if the log no longer reaches back, every thing the client was told of that is not in the list any more.
        if (!first)
        {
            if (placements.RemovedSince(fromRevision, removed)) { foreach (int id in removed) pending.Add(new PNGridPlacementItem { Kind = PNGridPlacementKind.Remove, Id = id }); }
            else foreach (var pair in known) if (placements.Get(pair.Key) == null) pending.Add(new PNGridPlacementItem { Kind = PNGridPlacementKind.Remove, Id = pair.Key });
        }
        var healths = new List<PNGridPlacementItem>();
        int interest = PNGridChunkLimits.InterestRadius;
        foreach (var placed in placements.All)
        {
            int distance = Distance(placed, chunks);
            if (known.TryGetValue(placed.Id, out int health))
            {
                if (distance > interest + PNGridPlacementLimits.Slack) pending.Add(new PNGridPlacementItem { Kind = PNGridPlacementKind.Remove, Id = placed.Id });
                else if (health != placed.Health) healths.Add(new PNGridPlacementItem { Kind = PNGridPlacementKind.Health, Id = placed.Id, Health = placed.Health });
            }
            else if (distance <= interest) adds.Add((distance, placed.Id));
        }
        adds.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Id.CompareTo(b.Id));
        foreach (var add in adds)
        {
            var placed = placements.Get(add.Id);
            pending.Add(new PNGridPlacementItem { Kind = PNGridPlacementKind.Add, Id = placed.Id, Definition = placed.Definition.Id.ToString(), X = placed.X, Z = placed.Z, Direction = placed.Direction, Health = placed.Health, Key = placed.Key ?? "" });
        }
        pending.AddRange(healths);
    }

    private List<(int X, int Z)> FocusChunks(IReadOnlyList<(float X, float Z)> focus, out long key)
    {
        var chunks = new List<(int X, int Z)>();
        float size = data.CellSize * PNGridWorldLimits.ChunkCells;
        if (focus != null) foreach (var point in focus) chunks.Add((Clamp((int)Math.Floor(point.X / size), data.ChunksX), Clamp((int)Math.Floor(point.Z / size), data.ChunksZ)));
        if (chunks.Count == 0) chunks.Add((data.ChunksX / 2, data.ChunksZ / 2));
        long hash = 17;
        foreach (var c in chunks) hash = unchecked(hash * 31 + c.X * 4099 + c.Z);
        key = hash; return chunks;
    }

    // Chebyshev distance in chunks from the thing's lowest cell to the nearest focus chunk.
    private int Distance(PNPlacedObject placed, List<(int X, int Z)> chunks)
    {
        int cx = Clamp(placed.X / PNGridWorldLimits.ChunkCells, data.ChunksX), cz = Clamp(placed.Z / PNGridWorldLimits.ChunkCells, data.ChunksZ), best = int.MaxValue;
        foreach (var f in chunks) best = Math.Min(best, Math.Max(Math.Abs(cx - f.X), Math.Abs(cz - f.Z)));
        return best;
    }
    private static int Clamp(int value, int count) => value < 0 ? 0 : value >= count ? count - 1 : value;
}

// What a client holds for one grid world: the items that arrived and have not been applied yet.
public sealed class PNGridPlacementQueue
{
    private readonly Queue<PNGridPlacementItem> queue = new Queue<PNGridPlacementItem>();
    public int Count => queue.Count;
    public long ItemsReceived { get; private set; }
    public long ItemsApplied { get; private set; }

    // Keeps a packet's items. Malformed items or a queue that would pass the bound are a protocol failure (an honest host paces itself).
    public void Offer(IReadOnlyList<PNGridPlacementItem> items)
    {
        if (items == null || items.Count == 0 || items.Count > PNGridPlacementLimits.MaxItemsPerPacket) throw new FormatException("A placement packet holds 1 to " + PNGridPlacementLimits.MaxItemsPerPacket + " items.");
        if (queue.Count + items.Count > PNGridPlacementLimits.MaxQueued) throw new FormatException("The placement queue is full.");
        foreach (var item in items) PNGridPlacementItem.Validate(item);
        foreach (var item in items) queue.Enqueue(item);
        ItemsReceived += items.Count;
    }

    public bool TryTake(out PNGridPlacementItem item)
    {
        if (queue.Count == 0) { item = default; return false; }
        item = queue.Dequeue(); ItemsApplied++; return true;
    }

    public void Clear() { queue.Clear(); ItemsReceived = 0; ItemsApplied = 0; }
}
