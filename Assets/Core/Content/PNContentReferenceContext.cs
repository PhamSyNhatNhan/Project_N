using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// Reference checks belong to the selected payload schema, not to property-name inference.
public sealed class PNContentReferenceContext
{
    public EveRegistryHub Registry { get; }
    private readonly EveContentExtensionDocument document;
    private readonly IReadOnlyList<EveIIdentified> pending;
    public PNContentReferenceContext(EveRegistryHub registry, EveContentExtensionDocument document = null,
        IReadOnlyList<EveIIdentified> pending = null)
    { Registry = registry ?? throw new ArgumentNullException(nameof(registry)); this.document = document; this.pending = pending ?? System.Array.Empty<EveIIdentified>(); }
    public EveId Read(JToken token, string path)
    {
        if (token == null || token.Type != JTokenType.String || !EveId.TryParse(token.Value<string>(), out var id))
            throw new FormatException(path + ": registered ID string required.");
        return id;
    }
    public void Require<T>(JToken token, string path) where T : class, EveIIdentified => Require<T>(Read(token, path), path);
    public void Require<T>(EveId id, string path) where T : class, EveIIdentified
    {
        // A kind of the shared instance registry is a subclass: the id must exist and be of that kind, not just any spawnable thing.
        if (typeof(PNInstanceDefinition).IsAssignableFrom(typeof(T)) && typeof(T) != typeof(PNInstanceDefinition))
        {
            var instances = Registry.Instances();
            PNInstanceDefinition found;
            if (!instances.TryGet(id, out found)) found = pending.OfType<PNInstanceDefinition>().FirstOrDefault(x => x.Id == id);
            if (!(found is T)) throw new FormatException(path + ": unknown " + typeof(T).Name + " " + id + (found != null ? " (it is a " + found.Kind + " definition)" : ""));
            RequireOwner(id, path, instances.BindingOwner);
            return;
        }
        var registry = Registry.Get<T>();
        if (!registry.TryGet(id, out _) && !pending.OfType<T>().Any(x => x.Id == id))
            throw new FormatException(path + ": unknown " + typeof(T).Name + " " + id);
        RequireOwner(id, path, registry.BindingOwner);
    }
    private void RequireOwner(EveId id, string path, Func<EveId, string> bindingOwner)
    {
        var owner = id.Namespace == "base" ? bindingOwner(id) : id.Namespace;
        if (document?.ContentNamespace != null && owner != document.ContentNamespace &&
            document.Dependencies != null && !document.Dependencies.Contains(owner))
            throw new FormatException(path + ": reference requires dependency on " + owner);
    }
    public void Array<T>(JToken token, string path) where T : class, EveIIdentified
    {
        if (!(token is JArray values)) throw new FormatException(path + ": ID array required.");
        for (int i = 0; i < values.Count; i++) Require<T>(values[i], path + "[" + i + "]");
    }
    public void Optional<T>(JObject data, string field) where T : class, EveIIdentified
    { if (data.Property(field) != null) Require<T>(data[field], field); }
    public void StatOrTag(JToken token, string path)
    {
        var id = Read(token, path);
        if (Registry.Stats.TryGet(id, out _) || pending.OfType<EveStatDefinition>().Any(x => x.Id == id)) Require<EveStatDefinition>(id, path);
        else Require<PNTagDefinition>(id, path);
    }
}
