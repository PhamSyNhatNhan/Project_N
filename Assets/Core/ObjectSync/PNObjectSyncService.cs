using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

// EN: The wire seam of the spawned-object sync. Null on a machine with no network. Assets/Coop implements it in O3; Core and Game never see Mirror.
// VI: Điểm nối với mạng của đồng bộ vật thể. Null khi không có mạng.
public interface IPNObjectSyncTransport
{
    // authority -> clients (the transport decides who may see the world of the record)
    void Send(PNObjectRecord record);
}

// EN: Client side of one kind: builds, corrects and ends the view-only copies. Spawned returns false when the copy could not be made (the record is then dropped, not retried).
//     `elapsed` is how many seconds of its own life the object has already lived at this moment (record age plus the delay since the record's stamp, capped).
// VI: Phía client của một loại: dựng, chỉnh và kết thúc bản hình. Spawned trả false khi không dựng được (bản ghi bị bỏ).
public interface IPNObjectSyncSink
{
    bool Spawned(PNObjectRecord spawn, double elapsed);
    void Retargeted(PNObjectRecord retarget);
    void Ended(PNObjectRecord end);
}

// EN: Authority side of one kind: tells the service the current state of a live object (for the baseline of a late observer). False when the object is gone.
public interface IPNObjectSyncSource
{
    bool TryCapture(long id, out PNObjectVector position, out PNObjectVector direction, out long target, out float age);
}

// EN: The per-play service of the spawned-object sync (stage O2). It is pure C#: Game's managers feed it on the authority (RecordSpawn / RecordRetarget / RecordEnd, then Flush
//     once per frame) and register as sinks on a client (Receive, Tick). It owns the ids, the retarget budget, the list of live objects for a late observer and, on a client,
//     the list of live copies with their leak guard. It never throws for a bad or stale record: it drops it and counts it.
// VI: Dịch vụ theo phiên chơi của đồng bộ vật thể. Thuần C#: manager của Game cấp bản ghi ở authority và đăng ký làm sink ở client. Bản ghi sai hoặc cũ bị bỏ và đếm, không ném.
public sealed class PNObjectSyncService : IDisposable
{
    private sealed class Live
    {
        public PNObjectRecord Spawn; public PNObjectRecord Pending; public double LastRetarget = double.NegativeInfinity;
    }
    private sealed class Replica
    {
        public PNObjectKind Kind; public PNObjectWorld World; public double Deadline; public PNObjectVector Position;
    }

    private readonly Func<double> clock;
    private readonly Dictionary<long, Live> live = new Dictionary<long, Live>();
    private readonly Dictionary<long, Replica> replicas = new Dictionary<long, Replica>();
    private readonly Dictionary<PNObjectKind, IPNObjectSyncSink> sinks = new Dictionary<PNObjectKind, IPNObjectSyncSink>();
    private readonly Dictionary<PNObjectKind, IPNObjectSyncSource> sources = new Dictionary<PNObjectKind, IPNObjectSyncSource>();
    private readonly Queue<long> endedOrder = new Queue<long>();
    private readonly HashSet<long> ended = new HashSet<long>();
    private long sequence; private int pendingCount;
    private bool disposed;

    public bool IsAuthority { get; }
    public IPNObjectSyncTransport Transport { get; set; }
    // Host time as this machine estimates it (the transport glue sets it from the time envelope); the local clock by default.
    public Func<double> HostClock { get; set; }
    public int LiveCount => live.Count;
    public int ReplicaCount => replicas.Count;
    // Counters (bytes are the estimates of the records sent).
    public int SpawnsSent { get; private set; }
    public int RetargetsSent { get; private set; }
    public int EndsSent { get; private set; }
    public long BytesSent { get; private set; }
    public int RetargetsCoalesced { get; private set; }
    public int Dropped { get; private set; }
    public int Stale { get; private set; }
    public int Faults { get; private set; }
    // Objects the authority did not sync because their record could not be made (an input over the limit, a value out of range).
    public int Refused { get; private set; }
    public void NoteRefused() => Refused++;

    public PNObjectSyncService(bool authority, Func<double> clock)
    { IsAuthority = authority; this.clock = clock ?? throw new ArgumentNullException(nameof(clock)); }

    public void SetSink(PNObjectKind kind, IPNObjectSyncSink sink)
    { if (sink == null) sinks.Remove(kind); else sinks[kind] = sink; }
    public void SetSource(PNObjectKind kind, IPNObjectSyncSource source)
    { if (source == null) sources.Remove(kind); else sources[kind] = source; }

    // ---- authority ------------------------------------------------------------------------------------------------------------------
    public long NextId() { Require(true); return ++sequence; }
    // The host's clock as the records stamp it: the host clock estimate when the glue gave one, else the local clock.
    public double Now => (HostClock ?? clock)();

    // Records a new object and sends its Spawn at once (a spawn is never delayed or dropped by the budget). False when the service is closed or the record is not a Spawn.
    public bool RecordSpawn(PNObjectRecord spawn)
    {
        if (disposed || !IsAuthority || spawn == null || spawn.Type != PNObjectRecordType.Spawn || live.ContainsKey(spawn.Id)) { Dropped++; return false; }
        live[spawn.Id] = new Live { Spawn = spawn };
        Send(spawn); SpawnsSent++; return true;
    }

    // A change of target: sent when the object's budget allows it, else kept (the latest one wins) and sent by a later Flush.
    public void RecordRetarget(PNObjectRecord retarget)
    {
        if (disposed || !IsAuthority || retarget == null || retarget.Type != PNObjectRecordType.Retarget || !live.TryGetValue(retarget.Id, out var entry)) { Stale++; return; }
        double now = clock();
        if (now - entry.LastRetarget >= PNObjectSyncLimits.RetargetInterval && entry.Pending == null) { entry.LastRetarget = now; Send(retarget); RetargetsSent++; return; }
        if (entry.Pending != null) RetargetsCoalesced++; else pendingCount++;
        entry.Pending = retarget;
    }

    // The end of an object: drops its pending retarget (the end supersedes it) and sends the End at once; never dropped by the budget.
    public void RecordEnd(PNObjectRecord end)
    {
        if (disposed || !IsAuthority || end == null || end.Type != PNObjectRecordType.End || !live.TryGetValue(end.Id, out var closing)) { Stale++; return; }
        if (closing.Pending != null) pendingCount--;
        live.Remove(end.Id); Send(end); EndsSent++;
    }

    // Once per frame: sends the retargets whose budget came back, oldest first, at most RetargetsPerFlush of them.
    public void Flush()
    {
        if (disposed || !IsAuthority || pendingCount == 0) return;
        double now = clock(); int sent = 0;
        foreach (var entry in live.Values.Where(x => x.Pending != null && now - x.LastRetarget >= PNObjectSyncLimits.RetargetInterval).OrderBy(x => x.LastRetarget).ToArray())
        {
            if (sent == PNObjectSyncLimits.RetargetsPerFlush) break;
            var record = entry.Pending; entry.Pending = null; pendingCount--; entry.LastRetarget = now; Send(record); RetargetsSent++; sent++;
        }
    }

    // The live objects of one world as a late observer needs them: each Spawn with the state it has now (from the registered source of its kind), stamped now.
    public IReadOnlyList<PNObjectRecord> Baseline(PNObjectWorld world)
    {
        var result = new List<PNObjectRecord>(); double now = Now;
        foreach (var entry in live.Values)
        {
            var spawn = entry.Spawn;
            if (!spawn.World.Equals(world)) continue;
            if (!sources.TryGetValue(spawn.Kind, out var source) || !source.TryCapture(spawn.Id, out var position, out var direction, out var target, out var age)) continue;
            try { result.Add(spawn.WithState(position, direction, target, now, age)); } catch (FormatException) { Dropped++; }
        }
        return result;
    }

    // The object left without an End record being wanted (its world is gone): forgets it silently. Used when the whole world is released.
    public void ForgetWorld(PNObjectWorld world)
    { foreach (var id in live.Where(x => x.Value.Spawn.World.Equals(world)).Select(x => x.Key).ToArray()) { if (live[id].Pending != null) pendingCount--; live.Remove(id); } }

    private void Send(PNObjectRecord record)
    {
        BytesSent += record.EstimatedBytes;
        try { Transport?.Send(record); } catch (Exception) { Faults++; }
    }

    // ---- client ---------------------------------------------------------------------------------------------------------------------
    // Applies one record from the authority. Never throws: a record that is invalid, stale, duplicated, for an unknown kind or above the live cap is dropped and counted.
    public void Receive(PNObjectRecord record)
    {
        if (disposed || IsAuthority) { Dropped++; return; }
        if (record == null || !record.TryValidate(out _)) { Dropped++; return; }
        switch (record.Type)
        {
            case PNObjectRecordType.Spawn: ReceiveSpawn(record); break;
            case PNObjectRecordType.Retarget:
                if (!replicas.TryGetValue(record.Id, out var known) || known.Kind != record.Kind || !known.World.Equals(record.World)) { Stale++; return; }
                known.Position = record.Position;
                Call(record.Kind, sink => sink.Retargeted(record), record.Id);
                break;
            case PNObjectRecordType.End:
                if (!replicas.TryGetValue(record.Id, out var closing) || closing.Kind != record.Kind || !closing.World.Equals(record.World)) { Remember(record.Id); Stale++; return; }
                replicas.Remove(record.Id); Remember(record.Id);
                Call(record.Kind, sink => sink.Ended(record), record.Id);
                break;
        }
    }

    private void ReceiveSpawn(PNObjectRecord spawn)
    {
        if (replicas.ContainsKey(spawn.Id) || ended.Contains(spawn.Id)) { Stale++; return; }
        if (replicas.Count >= PNObjectSyncLimits.MaxLive || !sinks.TryGetValue(spawn.Kind, out var sink)) { Dropped++; return; }
        double delay = Math.Max(0d, (HostClock ?? clock)() - spawn.Stamp);
        double elapsed = Math.Min(PNObjectSyncLimits.MaxReplicaSeconds, spawn.Age + Math.Min(delay, PNObjectSyncLimits.MaxCatchUpSeconds));
        double life = spawn.LifeHint > 0f ? spawn.LifeHint + 2d : PNObjectSyncLimits.MaxReplicaSeconds;
        // Registered before the call so a sink that ends the copy at once (it already outlived itself) finds it and the End is not taken for an unknown object.
        replicas[spawn.Id] = new Replica { Kind = spawn.Kind, World = spawn.World, Deadline = clock() + Math.Max(1d, life - elapsed), Position = spawn.Position };
        bool built = false;
        try { built = sink.Spawned(spawn, elapsed); } catch (Exception) { Faults++; }
        if (!built) { replicas.Remove(spawn.Id); Dropped++; }
    }

    // A client copy ended by itself (it ran out its own life, or its world went): the service forgets it so a late End or Retarget is counted as stale.
    public void ReplicaEnded(long id)
    { if (replicas.Remove(id)) Remember(id); }

    // Once per frame: ends the copies that outlived the guard (their End never came, for instance because the world was left).
    public void Tick()
    {
        if (disposed || IsAuthority || replicas.Count == 0) return;
        double now = clock();
        foreach (var pair in replicas.Where(x => now >= x.Value.Deadline).ToArray()) EndReplica(pair.Key, pair.Value, 255);
    }

    // The replicas of a late observer: builds the objects it does not have and ends the ones of that world the authority no longer lists.
    public void ApplyBaseline(PNObjectWorld world, IReadOnlyList<PNObjectRecord> spawns)
    {
        if (disposed || IsAuthority) return;
        var listed = new HashSet<long>();
        foreach (var spawn in spawns ?? Array.Empty<PNObjectRecord>())
        {
            if (spawn == null || spawn.Type != PNObjectRecordType.Spawn || !spawn.World.Equals(world)) { Dropped++; continue; }
            listed.Add(spawn.Id); if (!replicas.ContainsKey(spawn.Id)) Receive(spawn);
        }
        foreach (var pair in replicas.Where(x => x.Value.World.Equals(world) && !listed.Contains(x.Key)).ToArray()) EndReplica(pair.Key, pair.Value, 255);
    }

    // The world is released: ends every copy of it.
    public void ReleaseWorld(PNObjectWorld world)
    { foreach (var pair in replicas.Where(x => x.Value.World.Equals(world)).ToArray()) EndReplica(pair.Key, pair.Value, 255); }

    private void EndReplica(long id, Replica replica, byte reason)
    {
        replicas.Remove(id); Remember(id);
        PNObjectRecord end;
        try { end = PNObjectRecord.End(replica.Kind, id, replica.World, reason, replica.Position); } catch (FormatException) { Faults++; return; }
        Call(replica.Kind, sink => sink.Ended(end), id);
    }

    private void Call(PNObjectKind kind, Action<IPNObjectSyncSink> action, long id)
    {
        if (!sinks.TryGetValue(kind, out var sink)) { Dropped++; return; }
        try { action(sink); } catch (Exception) { Faults++; replicas.Remove(id); }
    }

    private void Remember(long id)
    {
        if (!ended.Add(id)) return;
        endedOrder.Enqueue(id);
        while (endedOrder.Count > PNObjectSyncLimits.EndedMemory) ended.Remove(endedOrder.Dequeue());
    }

    private void Require(bool authority)
    { if (disposed) throw new ObjectDisposedException(nameof(PNObjectSyncService)); if (IsAuthority != authority) throw new InvalidOperationException(authority ? "Only the authority gives out object ids." : "Only a client applies records."); }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (!IsAuthority) foreach (var pair in replicas.ToArray()) { try { var end = PNObjectRecord.End(pair.Value.Kind, pair.Key, pair.Value.World, 255, pair.Value.Position); if (sinks.TryGetValue(pair.Value.Kind, out var sink)) sink.Ended(end); } catch (Exception) { Faults++; } }
        replicas.Clear(); live.Clear(); pendingCount = 0; sinks.Clear(); sources.Clear(); Transport = null;
    }
}
