using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: The per-play service of the state-and-command layer (Docs/NetworkStatePlan.md N2). It knows the objects a play addresses, the writers and readers of every state
//     block, the latest value and revision of each, and the commands. It is pure C#: the world glue (Game) tells it which objects exist, the network glue (Coop) sets
//     Transport. Offline and host-without-clients use it as it is; a command sent on the authority runs the handler directly, the same path a remote command takes.
//     An Entity target is one life of an entity (id + generation); RemoveEntity retires it for good (N4).
// VI: Dịch vụ theo phiên chơi của lớp đồng bộ. Thuần C#: phần world (Game) báo object nào tồn tại, phần mạng (Coop) đặt Transport.
public sealed class PNSyncService : IDisposable
{
    private sealed class Entry
    {
        public PNSyncStateDefinition State;
        public IPNSyncStateSource Source;
        public readonly List<IPNSyncStateSink> Sinks = new List<IPNSyncStateSink>();
        public ulong Revision;
        public JToken Latest;
        public bool Dirty;
    }
    private sealed class Lease : IDisposable
    {
        private Action release;
        public Lease(Action release) { this.release = release; }
        public void Dispose() { var run = release; release = null; run?.Invoke(); }
    }

    private readonly EveRegistryHub registry;
    private readonly EveSessionContext session;
    private readonly Dictionary<(PNSyncTarget, EveId), Entry> entries = new Dictionary<(PNSyncTarget, EveId), Entry>();
    private readonly Dictionary<PNSyncTarget, object> objects = new Dictionary<PNSyncTarget, object>();
    private readonly List<(PNSyncTarget, EveId)> dirty = new List<(PNSyncTarget, EveId)>();
    private bool disposed;

    public bool IsAuthority { get; }
    // The owner id commands from this machine carry when the authority sends to itself (host and offline are owner 0).
    public long LocalOwnerId { get; }
    public IPNSyncTransport Transport { get; set; }
    public int ObjectCount => objects.Count;
    public EveRegistryHub Registry => registry;
    public EveSessionContext Session => session;
    public int EntryCount => entries.Count;

    public PNSyncService(EveRegistryHub registry, bool authority, long localOwnerId = 0, EveSessionContext session = null)
    { this.registry = registry ?? throw new ArgumentNullException(nameof(registry)); IsAuthority = authority; LocalOwnerId = localOwnerId; this.session = session; }

    // ---- targets ----
    // An Entity target names one life of an entity: id + generation. Once RemoveEntity(entity, g) ran, every generation up to g is retired for good: nothing registers on it and
    // a late packet for it is stale (pool reuse gives the entity a new, higher generation, so the old life's state can never reach the new one).
    private readonly Dictionary<EveEntityId, long> retiredEntities = new Dictionary<EveEntityId, long>();
    private bool Retired(PNSyncTarget target)
        => target.Kind == PNSyncTargetKind.Entity && retiredEntities.TryGetValue(target.Entity, out var floor) && target.Generation <= floor;
    private void RefuseRetired(PNSyncTarget target)
    { if (Retired(target)) throw new InvalidOperationException("The sync target is retired: " + target); }
    private static void Allow(PNSyncTargets allowed, PNSyncTarget target, EveId id, string what)
    { if ((allowed & target.Mask) == 0) throw new FormatException(what + " " + id + " cannot address a " + target.Kind + " target."); }
    private void Live() { if (disposed) throw new ObjectDisposedException(nameof(PNSyncService)); }
    private PNSyncStateDefinition StateOf(EveId id)
        => registry.SyncStates().TryGet(id, out var state) ? state : throw new FormatException("Unknown sync state " + id);
    private PNSyncCommandDefinition CommandOf(EveId id)
        => registry.SyncCommands().TryGet(id, out var command) ? command : throw new FormatException("Unknown sync command " + id);

    // EN: Make an object addressable. The owner is whatever the glue wants handed back to command handlers (a component). One object per target.
    public IDisposable RegisterObject(PNSyncTarget target, object owner)
    {
        Live(); RefuseRetired(target);
        if (target.Kind == PNSyncTargetKind.Session) throw new FormatException("The session target needs no object.");
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        if (objects.ContainsKey(target)) throw new InvalidOperationException("Sync object already registered: " + target);
        if (objects.Count >= PNSyncLimits.MaxEntries) throw new InvalidOperationException("Too many sync objects.");
        objects.Add(target, owner);
        return new Lease(() => { if (objects.TryGetValue(target, out var current) && ReferenceEquals(current, owner)) objects.Remove(target); });
    }
    public bool TryGetObject(PNSyncTarget target, out object owner) => objects.TryGetValue(target, out owner);
    // EN: Every object of one world instance (a scanner looks for the ones near the player). A copy: the glue may register and forget objects while it is used.
    public List<KeyValuePair<PNSyncTarget, object>> ObjectsOf(Guid world, ulong incarnation)
    {
        var list = new List<KeyValuePair<PNSyncTarget, object>>();
        foreach (var pair in objects) if (pair.Key.InWorld(world, incarnation)) list.Add(pair);
        return list;
    }

    // ---- state ----
    private Entry EntryOf(PNSyncTarget target, PNSyncStateDefinition state, bool create)
    {
        var key = (target, state.Id);
        if (entries.TryGetValue(key, out var entry)) return entry;
        if (!create) return null;
        if (entries.Count >= PNSyncLimits.MaxEntries) throw new InvalidOperationException("Too many sync state entries.");
        int onTarget = 0; foreach (var existing in entries.Keys) if (existing.Item1.Equals(target)) onTarget++;
        if (onTarget >= PNSyncLimits.StatesPerTarget) throw new InvalidOperationException("A target holds at most " + PNSyncLimits.StatesPerTarget + " state blocks: " + target);
        entry = new Entry { State = state }; entries.Add(key, entry); return entry;
    }
    private void Prune(PNSyncTarget target, EveId id)
    {
        var key = (target, id);
        if (entries.TryGetValue(key, out var entry) && entry.Source == null && entry.Sinks.Count == 0 && entry.Latest == null) entries.Remove(key);
    }

    // EN: The writer of a block. Authority only, one per (target, block). Mark the block dirty after changing what Capture returns.
    public IDisposable RegisterSource(PNSyncTarget target, IPNSyncStateSource source)
    {
        Live(); RefuseRetired(target);
        if (!IsAuthority) throw new InvalidOperationException("Only the authority writes state.");
        if (source == null) throw new ArgumentNullException(nameof(source));
        var state = StateOf(source.StateId); Allow(state.Targets, target, state.Id, "State");
        var entry = EntryOf(target, state, true);
        if (entry.Source != null) throw new InvalidOperationException("State " + state.Id + " already has a writer on " + target);
        entry.Source = source;
        return new Lease(() => { if (ReferenceEquals(entry.Source, source)) entry.Source = null; entry.Dirty = false; dirty.Remove((target, state.Id)); Prune(target, state.Id); });
    }

    // EN: A reader of a block, on any machine. A block that already has a value is applied at once, so a late reader sees the latest.
    public IDisposable RegisterSink(PNSyncTarget target, IPNSyncStateSink sink)
    {
        Live(); RefuseRetired(target);
        if (sink == null) throw new ArgumentNullException(nameof(sink));
        var state = StateOf(sink.StateId); Allow(state.Targets, target, state.Id, "State");
        var entry = EntryOf(target, state, true);
        entry.Sinks.Add(sink);
        var lease = new Lease(() => { entry.Sinks.Remove(sink); Prune(target, state.Id); });
        if (entry.Latest != null)
        {
            try { sink.Apply(entry.Latest.DeepClone(), entry.Revision); }
            catch { lease.Dispose(); throw; }
        }
        return lease;
    }

    public void MarkDirty(PNSyncTarget target, EveId stateId)
    {
        Live(); if (Retired(target)) return;
        if (!IsAuthority) throw new InvalidOperationException("Only the authority writes state.");
        var entry = EntryOf(target, StateOf(stateId), false);
        if (entry == null || entry.Source == null) throw new InvalidOperationException("State " + stateId + " has no writer on " + target);
        if (entry.Dirty) return;
        entry.Dirty = true; dirty.Add((target, stateId));
    }

    // EN: Capture every dirty block, give the changed ones the next revision, hand them to this machine's readers and to the transport. A block whose value did not change
    //     keeps its revision. All blocks are processed; failures are collected and thrown together at the end.
    public int Flush()
    {
        Live();
        if (dirty.Count == 0) return 0;
        var batch = dirty.ToArray(); dirty.Clear();
        var errors = new List<Exception>(); int sent = 0;
        foreach (var key in batch)
        {
            if (!entries.TryGetValue(key, out var entry) || !entry.Dirty) continue;
            entry.Dirty = false;
            try
            {
                if (entry.Source == null) continue;
                var value = entry.Source.Capture() ?? JValue.CreateNull();
                PNSyncPayload.Measure(value, entry.State.MaxBytes, "State " + entry.State.Id);
                if (entry.Latest != null && JToken.DeepEquals(entry.Latest, value)) continue;
                entry.Latest = value.DeepClone(); entry.Revision = checked(entry.Revision + 1); sent++;
                Deliver(entry, key.Item1);
                Transport?.SendState(key.Item1, entry.State, entry.Revision, entry.Latest.DeepClone());
            }
            catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count == 1) throw errors[0];
        if (errors.Count > 1) throw new AggregateException("Sync flush failed.", errors);
        return sent;
    }

    private void Deliver(Entry entry, PNSyncTarget target)
    {
        foreach (var sink in entry.Sinks.ToArray())
            sink.Apply(entry.Latest.DeepClone(), entry.Revision);
    }

    // EN: Client side: a value from the authority. Older or repeated revisions are ignored (returns false). The transport has already checked who may send it.
    public bool ReceiveState(PNSyncTarget target, EveId stateId, int schema, ulong revision, JToken payload)
    {
        Live(); if (Retired(target)) return false;
        if (IsAuthority) throw new InvalidOperationException("The authority does not receive state.");
        var state = StateOf(stateId); Allow(state.Targets, target, state.Id, "State");
        if (schema != state.Schema) throw new FormatException("State " + stateId + " has schema " + state.Schema + ", received " + schema + ".");
        if (revision == 0) throw new FormatException("Revision 0 is not a value.");
        PNSyncPayload.Measure(payload, state.MaxBytes, "State " + stateId);
        var entry = EntryOf(target, state, true);
        if (revision <= entry.Revision) return false;
        entry.Revision = revision; entry.Latest = (payload ?? JValue.CreateNull()).DeepClone();
        Deliver(entry, target);
        return true;
    }

    public bool TryGetLatest(PNSyncTarget target, EveId stateId, out ulong revision, out JToken value)
    {
        if (entries.TryGetValue((target, stateId), out var entry) && entry.Latest != null) { revision = entry.Revision; value = entry.Latest.DeepClone(); return true; }
        revision = 0; value = null; return false;
    }

    // EN: Every block that has a value, for a peer that just joined or whose world just loaded. The filter decides which targets the peer may see.
    public List<PNSyncSnapshot> Snapshot(Func<PNSyncTarget, bool> filter = null)
    {
        var list = new List<PNSyncSnapshot>();
        foreach (var pair in entries)
            if (pair.Value.Latest != null && (filter == null || filter(pair.Key.Item1)))
                list.Add(new PNSyncSnapshot(pair.Key.Item1, pair.Value.State, pair.Value.Revision, pair.Value.Latest.DeepClone()));
        return list;
    }

    // ---- commands ----
    // EN: Send a command to a target. On the authority it runs the handler now (the same path a remote command takes); elsewhere it goes through the transport.
    //     Returns false when the authority dropped it because the target no longer exists.
    public bool Send(PNSyncTarget target, EveId commandId, JToken payload = null)
    {
        Live(); if (Retired(target)) return false;
        var command = CommandOf(commandId); Allow(command.Targets, target, command.Id, "Command");
        PNSyncPayload.Measure(payload, command.MaxBytes, "Command " + commandId);
        if (IsAuthority) return Dispatch(LocalOwnerId, target, command, payload);
        if (Transport == null) throw new InvalidOperationException("No transport to send command " + commandId + ".");
        Transport.SendCommand(target, command, (payload ?? JValue.CreateNull()).DeepClone());
        return true;
    }

    // EN: Authority side: a command from a peer. Malformed means a protocol error (throws); a target that is gone is stale, not hostile (returns false, nothing runs).
    public bool ReceiveCommand(long senderOwnerId, PNSyncTarget target, EveId commandId, JToken payload)
    {
        Live(); if (Retired(target)) return false;
        if (!IsAuthority) throw new InvalidOperationException("Only the authority receives commands.");
        var command = CommandOf(commandId); Allow(command.Targets, target, command.Id, "Command");
        PNSyncPayload.Measure(payload, command.MaxBytes, "Command " + commandId);
        return Dispatch(senderOwnerId, target, command, payload);
    }

    private bool Dispatch(long sender, PNSyncTarget target, PNSyncCommandDefinition command, JToken payload)
    {
        object owner = null;
        if (target.Kind != PNSyncTargetKind.Session && !objects.TryGetValue(target, out owner)) return false;
        command.Handler.Execute(new PNSyncCommandContext(this, session, sender, target, owner, command, (payload ?? JValue.CreateNull()).DeepClone()));
        return true;
    }

    // ---- lifetime ----
    // EN: A world is going away: forget its objects, blocks, readers, writers and anything queued for it. The session target is untouched.
    public void RemoveWorld(Guid world, ulong incarnation)
    {
        foreach (var target in objects.Keys.Where(x => x.InWorld(world, incarnation)).ToArray()) objects.Remove(target);
        foreach (var key in entries.Keys.Where(x => x.Item1.InWorld(world, incarnation)).ToArray()) entries.Remove(key);
        dirty.RemoveAll(x => x.Item1.InWorld(world, incarnation));
    }

    // EN: One life of an entity is over (it retired, or its pooled shell is about to be reused): forget the objects, blocks, readers and writers of every generation up to this one
    //     and refuse them from now on. A newer generation of the same entity is a different target and is untouched.
    public void RemoveEntity(EveEntityId entity, long generation)
    {
        if (!entity.IsValid || generation < 0) throw new ArgumentException("A valid entity id and generation are required.");
        if (!retiredEntities.TryGetValue(entity, out var floor) || generation > floor)
        {
            if (retiredEntities.Count >= PNSyncLimits.MaxEntries && !retiredEntities.ContainsKey(entity)) retiredEntities.Remove(retiredEntities.Keys.First());
            retiredEntities[entity] = generation;
        }
        bool Old(PNSyncTarget x) => x.Kind == PNSyncTargetKind.Entity && x.Entity.Equals(entity) && x.Generation <= generation;
        foreach (var target in objects.Keys.Where(Old).ToArray()) objects.Remove(target);
        foreach (var key in entries.Keys.Where(x => Old(x.Item1)).ToArray()) entries.Remove(key);
        dirty.RemoveAll(x => Old(x.Item1));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; objects.Clear(); entries.Clear(); dirty.Clear(); retiredEntities.Clear(); Transport = null;
    }
}
