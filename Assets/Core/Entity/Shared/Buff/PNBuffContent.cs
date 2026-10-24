using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// Core owns the Buff schema; Engine only provides the extension envelope and atomic batch boundary.
public sealed partial class PNBuffContentBatch : EveIContentExtensionBatch
{
    private readonly EveRegistryHub hub;
    private readonly Dictionary<EveId, PNBuffTagDefinition> tags = new Dictionary<EveId, PNBuffTagDefinition>();
    private readonly Dictionary<EveId, PNBuffDefinition> buffs = new Dictionary<EveId, PNBuffDefinition>();
    private readonly Dictionary<EveId, PNInstanceDefinition> instances = new Dictionary<EveId, PNInstanceDefinition>();
    private readonly Dictionary<EveId,PNSkillBindingProfile> skillProfiles=new Dictionary<EveId,PNSkillBindingProfile>();
    private readonly Dictionary<EveId,PNSkillBaseBindings> skillMappings=new Dictionary<EveId,PNSkillBaseBindings>();
    private readonly List<Action> checks = new List<Action>();
    private readonly HashSet<EveId> pendingStats;
    public IEnumerable<EveIIdentified> Definitions => tags.Values.Cast<EveIIdentified>().Concat(buffs.Values).Concat(instances.Values).Concat(skillProfiles.Values).Concat(skillMappings.Values);
    public PNBuffContentBatch(IReadOnlyList<EveContentExtensionDocument> documents, EveRegistryHub hub, IReadOnlyList<EveIIdentified> pendingDefinitions)
    {
        this.hub = hub;
        pendingStats = new HashSet<EveId>((pendingDefinitions ?? System.Array.Empty<EveIIdentified>()).OfType<EveStatDefinition>().Select(x => x.Id));
        foreach (var document in documents)
        {
            var root = document.Extensions;
            // Each registered processor consumes its own namespaced extension.
            if (root["project_n"] == null) continue;
            var data = Object(root["project_n"]); Fields(data, "buffTags", "buffs", "instances", "skillBindingProfiles", "skillBaseBindings");
            ParseSkillBindings(data,document,pendingDefinitions);
            foreach (var value in Array(data["instances"]))
            {
                // The kind comes from the factory; the kind owns the extra entry fields it accepts (for example a projectile's destroyable).
                var obj = Object(value); var id = Owned(String(obj, "id"), document);
                var registry = hub.Instances();
                if (instances.ContainsKey(id) || registry.TryGet(id, out _)) throw new FormatException("Duplicate instance " + id);
                var factoryId = EveId.Parse(String(obj, "factoryId")); Reference(factoryId, document, registry);
                var factory = registry.Get(factoryId);
                Fields(obj, new[] { "id", "factoryId", "data" }.Concat(factory.EntryFields).ToArray());
                var definition = factory.Derive(id, obj["data"], obj); instances.Add(id, definition);
                checks.Add(() => { definition.ValidatePayload(hub); definition.ValidateReferences(References(document, pendingDefinitions)); });
            }
            foreach (var value in Array(data["buffTags"]))
            {
                var obj = Object(value); Fields(obj, "id"); var id = Owned(String(obj, "id"), document);
                if (tags.ContainsKey(id) || hub.BuffTags().TryGet(id, out _)) throw new FormatException("Duplicate Buff tag " + id);
                tags.Add(id, new PNBuffTagDefinition(id));
            }
            foreach (var value in Array(data["buffs"]))
            {
                var obj = Object(value); Fields(obj, "id", "factoryId", "data", "tags", "multipleInstances", "maximumInstances", "scope", "canDispel", "iconPath", "glow", "showInUI", "countdown");
                var id = Owned(String(obj, "id"), document);
                if (buffs.ContainsKey(id) || hub.Buffs().TryGet(id, out _)) throw new FormatException("Duplicate Buff " + id);
                var factoryId = EveId.Parse(String(obj, "factoryId")); Reference(factoryId, document, hub.Buffs());
                var factory = hub.Buffs().Get(factoryId);
                var tagIds = Array(obj["tags"]).Select(x => x.Type == JTokenType.String ? EveId.Parse(x.Value<string>()) : throw new FormatException("Tag ID string required.")).ToArray();
                bool multiple = Bool(obj, "multipleInstances"); int maximum = Integer(obj, "maximumInstances");
                string scopeText = String(obj, "scope");
                var scope = scopeText == "Target" ? PNBuffInstanceScope.Target : scopeText == "Source" ? PNBuffInstanceScope.Source : throw new FormatException("Unknown Buff scope.");
                var profile = factory.Derive(id, obj["data"], tagIds, multiple, maximum, scope, Bool(obj, "canDispel"), OptionalString(obj, "iconPath"), OptionalString(obj, "glow"), Bool(obj, "showInUI"));
                // Opt-in only; Derive never inherits the factory's descriptor, so a mod profile declares its own.
                var countdown = Countdown(obj["countdown"]);
                if (countdown != null) profile = profile.WithCountdown(countdown);
                buffs.Add(id, profile);
                checks.Add(() =>
                {
                    profile.ValidatePayload(hub);
                    foreach (var tag in tagIds)
                    {
                        Reference(tag, document, hub.BuffTags());
                        if (!tags.ContainsKey(tag) && !hub.BuffTags().TryGet(tag, out _)) throw new FormatException("Unknown Buff tag " + tag);
                    }
                    profile.ValidateReferences(References(document, pendingDefinitions));
                });
            }
        }
    }
    private PNContentReferenceContext References(EveContentExtensionDocument document, IReadOnlyList<EveIIdentified> initial)
        => new PNContentReferenceContext(hub, document, (initial ?? System.Array.Empty<EveIIdentified>()).Concat(Definitions).ToArray());
    private static EveId Owned(string text, EveContentExtensionDocument document)
    {
        var id = EveId.Parse(text);
        if (id.Namespace == "base" || document.ContentNamespace != null && id.Namespace != document.ContentNamespace) throw new FormatException("Buff definition must belong to pack namespace."); return id;
    }
    private static void Reference<T>(EveId id, EveContentExtensionDocument document, EveRegistry<T> registry) where T : class, EveIIdentified
    {
        string owner = id.Namespace == "base" ? registry.BindingOwner(id) : id.Namespace;
        if (document.ContentNamespace != null && document.Dependencies != null && owner != document.ContentNamespace && !document.Dependencies.Contains(owner)) throw new FormatException("Buff reference requires dependency on " + owner);
    }
    public void Validate() { if (hub.Buffs().IsSealed || hub.BuffTags().IsSealed) throw new InvalidOperationException("Buff registries sealed."); foreach (var check in checks) check(); }
    public void Commit() { foreach(var profile in skillProfiles.Values)hub.Get<PNSkillBindingProfile>().Register(profile); foreach(var mapping in skillMappings.Values)hub.Get<PNSkillBaseBindings>().Register(mapping); foreach (var tag in tags.Values) hub.BuffTags().Register(tag); foreach (var buff in buffs.Values) hub.Buffs().Register(buff); foreach (var instance in instances.Values) hub.Instances().Register(instance); }
    // Absent or null disables the countdown. Required fields; v1 accepts Ceiling/Hide only.
    internal static PNBuffCountdown Countdown(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null) return null;
        var obj = Object(token); Fields(obj, "slot", "rounding", "zero");
        string slotText = String(obj, "slot");
        PNBuffCountdownSlot slot;
        if (slotText == "TopLeft") slot = PNBuffCountdownSlot.TopLeft;
        else if (slotText == "TopRight") slot = PNBuffCountdownSlot.TopRight;
        else if (slotText == "BottomLeft") slot = PNBuffCountdownSlot.BottomLeft;
        else if (slotText == "BottomRight") slot = PNBuffCountdownSlot.BottomRight;
        else throw new FormatException("Unknown countdown slot.");
        string rounding = String(obj, "rounding"), zero = String(obj, "zero");
        if (rounding != PNBuffCountdown.CeilingRounding) throw new FormatException("Unsupported countdown rounding.");
        if (zero != PNBuffCountdown.HideZero) throw new FormatException("Unsupported countdown zero behavior.");
        return new PNBuffCountdown(slot, rounding, zero);
    }
    public static JObject Object(JToken token) => token as JObject ?? throw new FormatException("Object required.");
    public static void Fields(JObject obj, params string[] fields) { foreach (var p in obj.Properties()) if (!fields.Contains(p.Name)) throw new FormatException("Unknown Buff field " + p.Name); }
    public static IEnumerable<JToken> Array(JToken token) => token == null ? Enumerable.Empty<JToken>() : token is JArray array ? (IEnumerable<JToken>)array : throw new FormatException("Array required.");
    public static string String(JObject obj, string field) => obj[field]?.Type == JTokenType.String ? obj[field].Value<string>() : throw new FormatException("String required: " + field);
    public static string OptionalString(JObject obj, string field) => obj[field] == null ? "" : String(obj, field);
    public static bool Bool(JObject obj, string field) => obj[field]?.Type == JTokenType.Boolean ? obj[field].Value<bool>() : throw new FormatException("Boolean required: " + field);
    public static int Integer(JObject obj, string field) => obj[field]?.Type == JTokenType.Integer ? checked(obj[field].Value<int>()) : throw new FormatException("Integer required: " + field);
    public static double Number(JObject obj, string field)
    {
        if (obj[field]?.Type != JTokenType.Float && obj[field]?.Type != JTokenType.Integer) throw new FormatException("Number required: " + field);
        double value = obj[field].Value<double>(); if (double.IsNaN(value) || double.IsInfinity(value)) throw new FormatException("Finite number required: " + field); return value;
    }
}
