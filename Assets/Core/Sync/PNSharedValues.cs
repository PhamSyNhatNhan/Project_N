using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: The shared value store (Docs/SkillNetworkFrameworkPlan.md 4.7, G5): named values that belong to a whole world instance or to the whole play, written by the authority and seen the same way
//     on every machine, a late joiner included. It is not per-entity state (that is Ability) and not a stat or a Buff; it is for what a team shares ("the gate is open", "phase 2", "the
//     boss has been hit three times"). Skill, Action and Operation code writes through the context (`SetShared`, `GetShared`) and never sees the network: the service knows the mode
//     and ignores a write from a prediction (K12) or from a replay on another machine.
//     A key is declared by content (`extensions.project_n.sharedValues`, or code registering a PNSharedValueDefinition): id with a pack prefix, scope, kind, initial value, and whether it
//     goes into the save (K11: default no). Each scope is ONE sync block of at most PNSyncLimits.StateBytes, so a write that would not fit is refused, not cut.
// VI: Kho giá trị dùng chung (4.7, G5): giá trị có tên thuộc một world instance hoặc cả phiên chơi, authority ghi, mọi máy thấy giống nhau, kể cả người vào muộn. Không phải trạng thái theo
//     thực thể (đó là Ability). Người viết skill ghi qua context và không thấy mạng; ghi từ cast đoán hoặc phát lại ở máy khác bị bỏ qua.
public enum PNSharedScope : byte { World = 1, Session = 2 }
public enum PNSharedKind : byte { Number = 1, Flag = 2, Text = 3, List = 4 }
// EN: Applied: changed. Unchanged: the same value. Ignored: this machine is not the authority, or the write came from a prediction or a replay. Unknown: no such key. WrongScope: the key is not
//     of the store asked. Invalid: the value breaks the key's declaration. TooLarge: the scope's block would pass its byte bound.
public enum PNSharedWrite { Applied, Unchanged, Ignored, Unknown, WrongScope, Invalid, TooLarge }

public static class PNSharedLimits
{
    public const int KeysPerScope = 64, TextChars = 128, ListItems = 16, IdChars = 128;
    public const int BlockBytes = PNSyncLimits.StateBytes;
}

// EN: One declared key. Immutable. The initial value is what every machine reads until the authority writes something else, and is never sent.
public sealed class PNSharedValueDefinition : EveIIdentified
{
    private readonly JToken initial;
    public EveId Id { get; }
    public PNSharedScope Scope { get; }
    public PNSharedKind Kind { get; }
    // The kind of the entries of a List key (Number, Flag or Text); ignored for the other kinds.
    public PNSharedKind Item { get; }
    public bool Persist { get; }
    public double? Min { get; }
    public double? Max { get; }
    public JToken Initial => initial.DeepClone();

    public PNSharedValueDefinition(EveId id, PNSharedScope scope, PNSharedKind kind, JToken initial, bool persist = false, PNSharedKind item = PNSharedKind.Number, double? min = null, double? max = null)
    {
        if (!id.IsValid || id.ToString().Length > PNSharedLimits.IdChars) throw new ArgumentException("A shared value id of at most " + PNSharedLimits.IdChars + " characters is required.", nameof(id));
        if (scope != PNSharedScope.World && scope != PNSharedScope.Session) throw new ArgumentException("A shared value belongs to a world or to the session.", nameof(scope));
        if (kind < PNSharedKind.Number || kind > PNSharedKind.List) throw new ArgumentException("Unknown shared value kind.", nameof(kind));
        if (kind == PNSharedKind.List && (item < PNSharedKind.Number || item > PNSharedKind.Text)) throw new ArgumentException("A list holds numbers, flags or text.", nameof(item));
        bool numeric = kind == PNSharedKind.Number || kind == PNSharedKind.List && item == PNSharedKind.Number;
        if ((min.HasValue || max.HasValue) && !numeric) throw new ArgumentException("A range needs a number or a list of numbers.");
        if (min.HasValue && (double.IsNaN(min.Value) || double.IsInfinity(min.Value)) || max.HasValue && (double.IsNaN(max.Value) || double.IsInfinity(max.Value))) throw new ArgumentException("A range must be finite.");
        if (min.HasValue && max.HasValue && min.Value > max.Value) throw new ArgumentException("The minimum is above the maximum.");
        Id = id; Scope = scope; Kind = kind; Item = kind == PNSharedKind.List ? item : PNSharedKind.Number; Persist = persist; Min = min; Max = max;
        this.initial = Check(initial);
    }

    // EN: The value in its one stored form (numbers are doubles), or FormatException. A copy: the caller's token is never kept.
    public JToken Check(JToken value)
    {
        if (value == null) throw new FormatException("Shared value " + Id + " needs a value.");
        if (Kind != PNSharedKind.List) return One(Kind, value);
        if (!(value is JArray list)) throw new FormatException("Shared value " + Id + " is a list.");
        if (list.Count > PNSharedLimits.ListItems) throw new FormatException("Shared value " + Id + " holds at most " + PNSharedLimits.ListItems + " entries.");
        var result = new JArray();
        foreach (var entry in list) result.Add(One(Item, entry));
        return result;
    }

    private JToken One(PNSharedKind kind, JToken value)
    {
        switch (kind)
        {
            case PNSharedKind.Number:
                if (value.Type != JTokenType.Integer && value.Type != JTokenType.Float) throw new FormatException("Shared value " + Id + " needs a number.");
                double number = value.Value<double>();
                if (double.IsNaN(number) || double.IsInfinity(number)) throw new FormatException("Shared value " + Id + " must be finite.");
                if (Min.HasValue && number < Min.Value || Max.HasValue && number > Max.Value) throw new FormatException("Shared value " + Id + " is outside its range.");
                return new JValue(number);
            case PNSharedKind.Flag:
                if (value.Type != JTokenType.Boolean) throw new FormatException("Shared value " + Id + " needs true or false.");
                return new JValue(value.Value<bool>());
            case PNSharedKind.Text:
                if (value.Type != JTokenType.String) throw new FormatException("Shared value " + Id + " needs text.");
                string text = value.Value<string>();
                if (text.Length > PNSharedLimits.TextChars) throw new FormatException("Shared value " + Id + " holds at most " + PNSharedLimits.TextChars + " characters.");
                return new JValue(text);
            default: throw new FormatException("Shared value " + Id + " has a kind a list cannot hold.");
        }
    }

    public bool IsInitial(JToken value) => JToken.DeepEquals(initial, value);

    private static readonly string[] Fields = { "id", "scope", "kind", "item", "initial", "persist", "min", "max" };
    // EN: One entry of `sharedValues`. Every unknown field is refused. A per-entity scope is refused on purpose: that state is Ability.
    public static PNSharedValueDefinition Parse(JObject obj, EveId id)
    {
        if (obj == null) throw new FormatException("A shared value is an object.");
        foreach (var property in obj.Properties()) if (System.Array.IndexOf(Fields, property.Name) < 0) throw new FormatException("Shared value " + id + " has an unknown field \"" + property.Name + "\".");
        string scopeText = Text(obj, "scope", id), kindText = Text(obj, "kind", id);
        PNSharedScope scope;
        if (scopeText == "world") scope = PNSharedScope.World;
        else if (scopeText == "session") scope = PNSharedScope.Session;
        else if (scopeText == "entity") throw new FormatException("Shared value " + id + ": state of one entity is an Ability, not a shared value (scope is world or session).");
        else throw new FormatException("Shared value " + id + " has an unknown scope \"" + scopeText + "\".");
        PNSharedKind kind = KindOf(kindText, id, false);
        PNSharedKind item = PNSharedKind.Number;
        if (kind == PNSharedKind.List) item = KindOf(Text(obj, "item", id), id, true);
        else if (obj["item"] != null) throw new FormatException("Shared value " + id + ": only a list has an item kind.");
        if (obj["initial"] == null) throw new FormatException("Shared value " + id + " needs an initial value.");
        bool persist = false;
        if (obj["persist"] != null) persist = obj["persist"].Type == JTokenType.Boolean ? obj["persist"].Value<bool>() : throw new FormatException("Shared value " + id + ": persist is true or false.");
        return new PNSharedValueDefinition(id, scope, kind, obj["initial"], persist, item, Bound(obj, "min", id), Bound(obj, "max", id));
    }
    private static string Text(JObject obj, string field, EveId id)
        => obj[field]?.Type == JTokenType.String ? obj[field].Value<string>() : throw new FormatException("Shared value " + id + " needs the text field \"" + field + "\".");
    private static double? Bound(JObject obj, string field, EveId id)
    {
        if (obj[field] == null) return null;
        if (obj[field].Type != JTokenType.Integer && obj[field].Type != JTokenType.Float) throw new FormatException("Shared value " + id + ": " + field + " is a number.");
        return obj[field].Value<double>();
    }
    private static PNSharedKind KindOf(string text, EveId id, bool item)
    {
        if (text == "number") return PNSharedKind.Number;
        if (text == "flag") return PNSharedKind.Flag;
        if (text == "text") return PNSharedKind.Text;
        if (text == "list" && !item) return PNSharedKind.List;
        throw new FormatException("Shared value " + id + " has an unknown " + (item ? "item " : "") + "kind \"" + text + "\".");
    }
}

// EN: The values of one scope instance (the session, or one world instance): a sync source on the authority and a sync sink everywhere, the same object, as PNRunStateSync does. Only values that
//     differ from their initial value are kept and sent, so an untouched store is an empty block.
public sealed class PNSharedStore : IPNSyncStateSource, IPNSyncStateSink, IDisposable
{
    public static readonly EveId SessionState = EveId.Parse("project_n:shared_session"), WorldState = EveId.Parse("project_n:shared_world");
    // The world scope is addressed as a WorldObject with a reserved slot id: a content slot cannot start with '$', so the store never collides with an authored object.
    public const string WorldSlot = "$shared", WorldKey = "values";

    private readonly PNSyncService sync;
    private readonly PNSyncTarget target;
    private readonly Dictionary<EveId, PNSharedValueDefinition> defs = new Dictionary<EveId, PNSharedValueDefinition>();
    private Dictionary<EveId, JToken> values = new Dictionary<EveId, JToken>();
    private IDisposable sinkLease, sourceLease;
    private bool disposed;
    public PNSharedScope Scope { get; }
    public EveId StateId => Scope == PNSharedScope.Session ? SessionState : WorldState;
    // Fired on every machine when a value changes, after it has changed (the authority at the write, the others when the block arrives). A handler that throws is logged by the caller's frame, not swallowed here.
    public event Action<EveId, JToken> Changed;

    public PNSharedStore(PNSyncService sync, PNSyncTarget target, PNSharedScope scope)
    {
        this.sync = sync ?? throw new ArgumentNullException(nameof(sync));
        if (scope == PNSharedScope.Session ? target.Kind != PNSyncTargetKind.Session : target.Kind != PNSyncTargetKind.WorldObject) throw new ArgumentException("The target does not fit the scope.", nameof(target));
        this.target = target; Scope = scope;
        foreach (var definition in sync.Registry.SharedValues().Snapshot().Values) if (definition.Scope == scope) defs.Add(definition.Id, definition);
        if (defs.Count > PNSharedLimits.KeysPerScope) throw new FormatException("A scope holds at most " + PNSharedLimits.KeysPerScope + " shared values (" + defs.Count + " declared).");
        sinkLease = sync.RegisterSink(target, this);
        if (sync.IsAuthority) sourceLease = sync.RegisterSource(target, this);
    }

    public static PNSharedStore ForSession(PNSyncService sync) => new PNSharedStore(sync, PNSyncTarget.Session, PNSharedScope.Session);
    public static PNSharedStore ForWorld(PNSyncService sync, EveWorldContext world)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        return new PNSharedStore(sync, PNSyncTarget.ForWorldObject(world.InstanceId, world.Incarnation, WorldSlot, WorldKey), PNSharedScope.World);
    }

    public bool Has(EveId id) => defs.ContainsKey(id);
    public IReadOnlyCollection<PNSharedValueDefinition> Definitions => defs.Values;
    private PNSharedValueDefinition Def(EveId id) => defs.TryGetValue(id, out var d) ? d : throw new KeyNotFoundException("The " + Scope + " store has no shared value " + id + ".");

    // ---- read ----
    public JToken Get(EveId id) { var d = Def(id); return values.TryGetValue(id, out var v) ? v.DeepClone() : d.Initial; }
    public double Number(EveId id) { var v = Get(id); return Def(id).Kind == PNSharedKind.Number ? v.Value<double>() : throw new InvalidOperationException(id + " is not a number."); }
    public bool Flag(EveId id) { var v = Get(id); return Def(id).Kind == PNSharedKind.Flag ? v.Value<bool>() : throw new InvalidOperationException(id + " is not a flag."); }
    public string Text(EveId id) { var v = Get(id); return Def(id).Kind == PNSharedKind.Text ? v.Value<string>() : throw new InvalidOperationException(id + " is not text."); }

    // ---- write (the authority only) ----
    public PNSharedWrite Set(EveId id, JToken value)
    {
        if (disposed) return PNSharedWrite.Ignored;
        if (!defs.TryGetValue(id, out var d)) return sync.Registry.SharedValues().TryGet(id, out _) ? PNSharedWrite.WrongScope : PNSharedWrite.Unknown;
        if (!sync.IsAuthority) return PNSharedWrite.Ignored;
        JToken normal;
        try { normal = d.Check(value); } catch (FormatException) { return PNSharedWrite.Invalid; }
        if (JToken.DeepEquals(Get(id), normal)) return PNSharedWrite.Unchanged;
        var next = new Dictionary<EveId, JToken>(values);
        if (d.IsInitial(normal)) next.Remove(id); else next[id] = normal;
        if (Measure(next) > PNSharedLimits.BlockBytes) return PNSharedWrite.TooLarge;
        values = next;
        sync.MarkDirty(target, StateId);
        Changed?.Invoke(id, normal.DeepClone());
        return PNSharedWrite.Applied;
    }

    // ---- sync ----
    private static JObject Encode(Dictionary<EveId, JToken> map)
    {
        var o = new JObject();
        foreach (var pair in map.OrderBy(x => x.Key.ToString(), StringComparer.Ordinal)) o[pair.Key.ToString()] = pair.Value.DeepClone();
        return o;
    }
    private static int Measure(Dictionary<EveId, JToken> map) => Encoding.UTF8.GetByteCount(Encode(map).ToString(Newtonsoft.Json.Formatting.None));

    public JToken Capture() => Encode(values);

    public void Apply(JToken value, ulong revision)
    {
        var o = value as JObject ?? throw new FormatException("A shared value block is an object.");
        var next = new Dictionary<EveId, JToken>();
        foreach (var property in o.Properties())
        {
            if (!EveId.TryParse(property.Name, out var id) || !defs.TryGetValue(id, out var d)) throw new FormatException("The block holds a value this machine does not declare: " + property.Name);
            var normal = d.Check(property.Value);
            if (!d.IsInitial(normal)) next[id] = normal;
        }
        var old = values; values = next;
        var changed = new List<EveId>();
        foreach (var id in old.Keys.Union(next.Keys)) { old.TryGetValue(id, out var before); next.TryGetValue(id, out var after); if (!JToken.DeepEquals(before ?? defs[id].Initial, after ?? defs[id].Initial)) changed.Add(id); }
        foreach (var id in changed) Changed?.Invoke(id, Get(id));
    }

    // ---- persistence ----
    // EN: The values of the keys that declare `persist`, those that differ from their initial value (an object id -> value). Nothing else leaves the store.
    public JObject CapturePersisted()
    {
        var map = new Dictionary<EveId, JToken>();
        foreach (var pair in values) if (defs[pair.Key].Persist) map[pair.Key] = pair.Value;
        return Encode(map);
    }

    // EN: Read-only check of a saved object against the declarations of a scope: every key declared, of this scope, persistent, and its value valid. Throws FormatException.
    public static Dictionary<EveId, JToken> ValidatePersisted(EveRegistryHub hub, PNSharedScope scope, JObject saved)
    {
        if (saved == null) throw new FormatException("Saved shared values are an object.");
        var map = new Dictionary<EveId, JToken>();
        foreach (var property in saved.Properties())
        {
            if (!EveId.TryParse(property.Name, out var id) || !hub.SharedValues().TryGet(id, out var d)) throw new FormatException("The save holds a shared value that is not declared: " + property.Name);
            if (d.Scope != scope) throw new FormatException("The save holds " + id + " in the wrong scope.");
            if (!d.Persist) throw new FormatException("The save holds " + id + ", which is not persistent.");
            map[id] = d.Check(property.Value);
        }
        return map;
    }

    // EN: Put the saved values in (authority only): persistent keys not in the object go back to their initial value, the others keep nothing from before. Checked first, applied all or not at all.
    public void RestorePersisted(JObject saved)
    {
        if (!sync.IsAuthority) throw new InvalidOperationException("Only the authority restores shared values.");
        var map = ValidatePersisted(sync.Registry, Scope, saved);
        var next = new Dictionary<EveId, JToken>();
        foreach (var pair in values) if (!defs[pair.Key].Persist) next[pair.Key] = pair.Value;
        foreach (var pair in map) if (!defs[pair.Key].IsInitial(pair.Value)) next[pair.Key] = pair.Value;
        if (Measure(next) > PNSharedLimits.BlockBytes) throw new FormatException("The saved shared values do not fit the block.");
        var old = values; values = next; sync.MarkDirty(target, StateId);
        foreach (var id in old.Keys.Union(next.Keys))
        {
            old.TryGetValue(id, out var before); next.TryGetValue(id, out var after);
            if (!JToken.DeepEquals(before ?? defs[id].Initial, after ?? defs[id].Initial)) Changed?.Invoke(id, Get(id));
        }
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        sinkLease?.Dispose(); sourceLease?.Dispose(); sinkLease = sourceLease = null; Changed = null;
    }
}

// EN: The stores of one play, found by the session (the Game glue creates it): the session's store and a way to the store of a world. Code with a context reads and writes through it, and the
//     mode decides what a write does.
public sealed class PNSharedValues
{
    private static readonly Dictionary<Guid, PNSharedValues> all = new Dictionary<Guid, PNSharedValues>();
    public static void Attach(EveSessionContext session, PNSharedValues values)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        all[session.SessionId] = values ?? throw new ArgumentNullException(nameof(values));
    }
    public static void Detach(EveSessionContext session) { if (session != null) all.Remove(session.SessionId); }
    public static PNSharedValues Find(EveSessionContext session) => session != null && all.TryGetValue(session.SessionId, out var found) ? found : null;

    private readonly EveRegistryHub registry;
    private readonly Func<EveWorldContext, PNSharedStore> worldStore;
    public PNSharedStore Session { get; }
    public PNSharedValues(EveRegistryHub registry, PNSharedStore session, Func<EveWorldContext, PNSharedStore> worldStore)
    { this.registry = registry ?? throw new ArgumentNullException(nameof(registry)); Session = session ?? throw new ArgumentNullException(nameof(session)); this.worldStore = worldStore ?? throw new ArgumentNullException(nameof(worldStore)); }

    // The store of a world instance (null when this machine has no such world ready), for the persistence provider and for views.
    public PNSharedStore WorldStore(EveWorldContext world) => world == null ? null : worldStore(world);

    // The store that holds a key: the session's, or the world's. Null when the key is world-scoped and there is no world.
    public PNSharedStore Of(EveId id, EveWorldContext world)
    {
        var d = registry.SharedValues().Get(id);
        return d.Scope == PNSharedScope.Session ? Session : WorldStore(world);
    }

    public JToken Read(EveId id, EveWorldContext world)
        => (Of(id, world) ?? throw new InvalidOperationException("The world value " + id + " needs a world.")).Get(id);

    // EN: A write from game code. Only an Authority cast writes: a prediction and a replay on another machine get Ignored (K12, the team's state never jumps and rolls back on one machine).
    public PNSharedWrite Write(EveId id, JToken value, EveWorldContext world, PNSkillMode mode = PNSkillMode.Authority)
    {
        if (!registry.SharedValues().TryGet(id, out _)) return PNSharedWrite.Unknown;
        if (mode != PNSkillMode.Authority) return PNSharedWrite.Ignored;
        var store = Of(id, world);
        return store == null ? PNSharedWrite.Ignored : store.Set(id, value);
    }
}
