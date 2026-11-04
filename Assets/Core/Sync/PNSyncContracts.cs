using System;
using System.Collections.Generic;
using System.Text;
using EveEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// EN: Neutral contract of the shared state-and-command layer (Docs/NetworkStatePlan.md): registered state blocks the authority writes, commands a client sends to the
//     authority, and the three kinds of target they address. Nothing here knows Mirror; Assets/Coop carries the same contract over the wire.
// VI: Hợp đồng trung lập của lớp đồng bộ trạng thái và lệnh dùng chung: khối trạng thái do authority ghi, lệnh client gửi lên authority, và ba loại đích.
public static class PNSyncLimits
{
    public const int StateBytes = 4096, CommandBytes = 512, KeyChars = 64, SlotChars = 128, StatesPerTarget = 8, MaxEntries = 4096, QueuePackets = 256, QueueBytes = 64 * 1024;
}

// EN: A world object's key is stable authored text: lower-case letters, digits, '_', '.' and '-', at most PNSyncLimits.KeyChars.
public static class PNSyncKey
{
    public static bool IsValid(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > PNSyncLimits.KeyChars) return false;
        foreach (var c in key) if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_' || c == '.' || c == '-')) return false;
        return true;
    }
    public static string Require(string key, string what)
    {
        if (!IsValid(key)) throw new FormatException(what + " must be 1-" + PNSyncLimits.KeyChars + " characters of a-z, 0-9, '_', '.' or '-'.");
        return key;
    }
}

public enum PNSyncTargetKind : byte { Entity = 1, WorldObject = 2, Session = 3 }

[Flags] public enum PNSyncTargets { None = 0, Entity = 1, WorldObject = 2, Session = 4 }

// EN: What a block or command is addressed to. Entity: a live character-like entity (its binding). WorldObject: an authored object of one world instance, told apart by
//     world instance + incarnation + content slot + key, so two worlds built from the same map never mix up. Session: the whole play, no world.
//     An Entity target is one life of the entity (id + generation, the loader's LoadContext.Generation): a pooled or rebuilt entity is a new target, so state of an old life never reaches the new one.
public readonly struct PNSyncTarget : IEquatable<PNSyncTarget>
{
    public PNSyncTargetKind Kind { get; }
    public EveEntityId Entity { get; }
    public long Generation { get; }
    public Guid World { get; }
    public ulong Incarnation { get; }
    public string Slot { get; }
    public string Key { get; }

    private PNSyncTarget(PNSyncTargetKind kind, EveEntityId entity, long generation, Guid world, ulong incarnation, string slot, string key)
    { Kind = kind; Entity = entity; Generation = generation; World = world; Incarnation = incarnation; Slot = slot; Key = key; }

    public static PNSyncTarget Session => new PNSyncTarget(PNSyncTargetKind.Session, default, 0, Guid.Empty, 0, null, null);
    public static PNSyncTarget ForEntity(EveEntityId entity, long generation)
    {
        if (!entity.IsValid) throw new ArgumentException("A valid entity id is required.", nameof(entity));
        if (generation < 0) throw new ArgumentOutOfRangeException(nameof(generation));
        return new PNSyncTarget(PNSyncTargetKind.Entity, entity, generation, Guid.Empty, 0, null, null);
    }
    public static PNSyncTarget ForWorldObject(Guid world, ulong incarnation, string slot, string key)
    {
        if (world == Guid.Empty) throw new ArgumentException("A world instance id is required.", nameof(world));
        if (incarnation == 0) throw new ArgumentOutOfRangeException(nameof(incarnation));
        if (string.IsNullOrEmpty(slot) || slot.Length > PNSyncLimits.SlotChars) throw new ArgumentException("A content slot id of 1-" + PNSyncLimits.SlotChars + " characters is required.", nameof(slot));
        PNSyncKey.Require(key, "A world object key");
        return new PNSyncTarget(PNSyncTargetKind.WorldObject, default, 0, world, incarnation, slot, key);
    }

    public PNSyncTargets Mask => Kind == PNSyncTargetKind.Entity ? PNSyncTargets.Entity : Kind == PNSyncTargetKind.WorldObject ? PNSyncTargets.WorldObject : PNSyncTargets.Session;
    public bool InWorld(Guid world, ulong incarnation) => Kind == PNSyncTargetKind.WorldObject && World == world && Incarnation == incarnation;

    public bool Equals(PNSyncTarget other)
        => Kind == other.Kind && Entity.Equals(other.Entity) && Generation == other.Generation && World == other.World && Incarnation == other.Incarnation && string.Equals(Slot, other.Slot, StringComparison.Ordinal) && string.Equals(Key, other.Key, StringComparison.Ordinal);
    public override bool Equals(object obj) => obj is PNSyncTarget other && Equals(other);
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = (int)Kind;
            hash = hash * 31 + Entity.GetHashCode(); hash = hash * 31 + Generation.GetHashCode(); hash = hash * 31 + World.GetHashCode(); hash = hash * 31 + Incarnation.GetHashCode();
            hash = hash * 31 + (Slot?.GetHashCode() ?? 0); hash = hash * 31 + (Key?.GetHashCode() ?? 0);
            return hash;
        }
    }
    public static bool operator ==(PNSyncTarget left, PNSyncTarget right) => left.Equals(right);
    public static bool operator !=(PNSyncTarget left, PNSyncTarget right) => !left.Equals(right);
    public override string ToString()
        => Kind == PNSyncTargetKind.Session ? "session" : Kind == PNSyncTargetKind.Entity ? "entity " + Entity + "/" + Generation : "world " + World.ToString("N") + "/" + Incarnation + "/" + Slot + "/" + Key;
}

// EN: A payload is plain JSON (null, bool, number, string, array, object), finite, at most 16 levels deep and within the byte bound of its definition.
public static class PNSyncPayload
{
    public const int MaxDepth = 16;
    public static int Measure(JToken payload, int maxBytes, string what)
    {
        payload = payload ?? JValue.CreateNull();
        CheckPlain(payload, 0, what);
        int bytes = Encoding.UTF8.GetByteCount(payload.ToString(Formatting.None));
        if (bytes > maxBytes) throw new FormatException(what + " exceeds " + maxBytes + " bytes (" + bytes + ").");
        return bytes;
    }
    private static void CheckPlain(JToken token, int depth, string what)
    {
        if (depth > MaxDepth) throw new FormatException(what + " nests deeper than " + MaxDepth + " levels.");
        switch (token.Type)
        {
            case JTokenType.Null: case JTokenType.Boolean: case JTokenType.Integer: case JTokenType.String: break;
            case JTokenType.Float:
                double value = token.Value<double>();
                if (double.IsNaN(value) || double.IsInfinity(value)) throw new FormatException(what + " holds a number that is not finite.");
                break;
            case JTokenType.Object: foreach (var property in ((JObject)token).Properties()) CheckPlain(property.Value, depth + 1, what); break;
            case JTokenType.Array: foreach (var item in (JArray)token) CheckPlain(item, depth + 1, what); break;
            default: throw new FormatException(what + " holds a value that is not plain JSON (" + token.Type + ").");
        }
    }
}

// EN: A registered block of state: id, schema number, byte bound and the target kinds it may be put on. Immutable; the value lives in the service.
public sealed class PNSyncStateDefinition : EveIIdentified
{
    public EveId Id { get; }
    public int Schema { get; }
    public int MaxBytes { get; }
    public PNSyncTargets Targets { get; }
    public PNSyncStateDefinition(EveId id, int schema, int maxBytes, PNSyncTargets targets)
    {
        if (!id.IsValid || id.ToString().Length > 128) throw new ArgumentException("A valid state id of at most 128 characters is required.", nameof(id));
        if (schema < 1) throw new ArgumentOutOfRangeException(nameof(schema), "Schema numbers start at 1.");
        if (maxBytes < 1 || maxBytes > PNSyncLimits.StateBytes) throw new ArgumentOutOfRangeException(nameof(maxBytes), "A state block holds 1-" + PNSyncLimits.StateBytes + " bytes.");
        if (targets == PNSyncTargets.None || (targets & ~(PNSyncTargets.Entity | PNSyncTargets.WorldObject | PNSyncTargets.Session)) != 0) throw new ArgumentException("A state names at least one known target kind.", nameof(targets));
        Id = id; Schema = schema; MaxBytes = maxBytes; Targets = targets;
    }
}

// EN: A registered command: id, byte bound, the target kinds it may be sent to, and the handler that runs on the authority. The handler comes from a factory given at
//     registration (no reflection: safe under IL2CPP stripping); one shared instance serves every command of this definition, so it keeps no state of its own.
public sealed class PNSyncCommandDefinition : EveIIdentified
{
    public EveId Id { get; }
    public int MaxBytes { get; }
    public PNSyncTargets Targets { get; }
    public string Implementation => ImplementationType.FullName;
    [JsonIgnore] public Type ImplementationType { get; }
    private readonly Func<IPNSyncCommandHandler> create;
    private IPNSyncCommandHandler handler;
    [JsonIgnore] public IPNSyncCommandHandler Handler => handler ?? (handler = Make());
    public PNSyncCommandDefinition(EveId id, Type implementation, Func<IPNSyncCommandHandler> create, int maxBytes, PNSyncTargets targets)
    {
        if (!id.IsValid || id.ToString().Length > 128) throw new ArgumentException("A valid command id of at most 128 characters is required.", nameof(id));
        if (implementation == null || implementation.IsAbstract || implementation.IsInterface || implementation.ContainsGenericParameters || !typeof(IPNSyncCommandHandler).IsAssignableFrom(implementation))
            throw new ArgumentException("A command implementation must be a concrete IPNSyncCommandHandler class.", nameof(implementation));
        if (maxBytes < 1 || maxBytes > PNSyncLimits.CommandBytes) throw new ArgumentOutOfRangeException(nameof(maxBytes), "A command carries 1-" + PNSyncLimits.CommandBytes + " bytes.");
        if (targets == PNSyncTargets.None || (targets & ~(PNSyncTargets.Entity | PNSyncTargets.WorldObject | PNSyncTargets.Session)) != 0) throw new ArgumentException("A command names at least one known target kind.", nameof(targets));
        Id = id; ImplementationType = implementation; this.create = create ?? throw new ArgumentNullException(nameof(create)); MaxBytes = maxBytes; Targets = targets;
    }
    private IPNSyncCommandHandler Make()
    {
        var made = create();
        if (made == null || made.GetType() != ImplementationType) throw new InvalidOperationException("Command " + Id + " handler factory must return a " + ImplementationType.Name + ".");
        return made;
    }
}

// EN: Writer side, on the authority: Capture returns the current value of one block. Called when the block was marked dirty, never every frame.
public interface IPNSyncStateSource { EveId StateId { get; } JToken Capture(); }

// EN: Reader side, on every machine (the authority too, so a view reads the same way everywhere): Apply gets the latest value and its revision, in revision order.
public interface IPNSyncStateSink { EveId StateId { get; } void Apply(JToken value, ulong revision); }

// EN: Runs on the authority when a command arrives (or when the authority sends one to itself). It validates (range, permission, state) and acts; if it changes
//     state it marks the block dirty. It receives a copy of the payload.
public interface IPNSyncCommandHandler { void Execute(PNSyncCommandContext context); }

public sealed class PNSyncCommandContext
{
    public PNSyncService Service { get; }
    public EveSessionContext Session { get; }
    public long SenderOwnerId { get; }
    public PNSyncTarget Target { get; }
    public object TargetObject { get; }
    public PNSyncCommandDefinition Command { get; }
    public JToken Payload { get; }
    public PNSyncCommandContext(PNSyncService service, EveSessionContext session, long senderOwnerId, PNSyncTarget target, object targetObject, PNSyncCommandDefinition command, JToken payload)
    { Service = service; Session = session; SenderOwnerId = senderOwnerId; Target = target; TargetObject = targetObject; Command = command; Payload = payload; }
}

// EN: The wire seam. Null when there is no network (offline). Assets/Coop implements it with Mirror messages; Core and Game never see Mirror.
public interface IPNSyncTransport
{
    // authority -> clients: the latest value of one block (the transport decides who may see the target)
    void SendState(PNSyncTarget target, PNSyncStateDefinition state, ulong revision, JToken payload);
    // client -> authority
    void SendCommand(PNSyncTarget target, PNSyncCommandDefinition command, JToken payload);
}

// EN: One block as the late joiner needs it.
public readonly struct PNSyncSnapshot
{
    public PNSyncTarget Target { get; }
    public PNSyncStateDefinition State { get; }
    public ulong Revision { get; }
    public JToken Payload { get; }
    public PNSyncSnapshot(PNSyncTarget target, PNSyncStateDefinition state, ulong revision, JToken payload) { Target = target; State = state; Revision = revision; Payload = payload; }
}
