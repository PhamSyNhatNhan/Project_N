using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: A trait of an area (Docs/InstanceLifecyclePlan.md 3.4): a named rule an area can carry, 0..n per area, in declaration order. A trait is a registered definition with a public ID, a
//     stateless behavior class (`PNAreaTrait`, hooks `OnCreated` / `OnEnded`, Authority only) and a payload validator for the payload an area gives it. Content declares the traits of an area in the
//     entry field `traits` of its `instances` entry: `"traits":[{"id":"project_n:solizeitastronhringur"}]` (an optional `data` object is the trait's payload).
// VI: Trait cua area: mot luat co ten ma area co the mang, 0..n moi area, theo thu tu khai bao. Trait la dinh nghia dang ky voi ID cong khai, lop hanh vi khong giu trang thai (hook `OnCreated`/`OnEnded`,
//     chi Authority) va ham kiem payload. Noi dung khai bao trait cua area o truong muc `traits` cua muc `instances`.
public sealed class PNAreaTraitDefinition : EveIIdentified
{
    private readonly Action<JToken> validatePayload;
#if UNITY_5_3_OR_NEWER
    private PNAreaTrait behavior;
#endif
    public EveId Id { get; }
    public Type ImplementationType { get; }
    // `payloadValidator` checks the payload an area gives this trait (an empty object when the area gave none).
    public PNAreaTraitDefinition(EveId id, Type implementation, Action<JToken> payloadValidator)
    {
        if (!id.IsValid || id.Namespace == "base") throw new ArgumentException("Original area trait ID required.");
        Id = id; ImplementationType = implementation ?? throw new ArgumentNullException(nameof(implementation));
        validatePayload = payloadValidator ?? throw new ArgumentNullException(nameof(payloadValidator));
#if UNITY_5_3_OR_NEWER
        if (!typeof(PNAreaTrait).IsAssignableFrom(implementation) || implementation.IsAbstract || implementation.GetConstructor(Type.EmptyTypes) == null)
            throw new ArgumentException("An area trait implementation must be a concrete PNAreaTrait with a public parameterless constructor.");
#endif
    }
    // The payload a trait that takes none accepts: absent or an empty object.
    public static void NoPayload(JToken payload) { if (payload != null && payload.Type != JTokenType.Null && !(payload is JObject o && o.Count == 0)) throw new FormatException("This area trait takes no data."); }
    public void ValidatePayload(JToken payload) => validatePayload(payload?.DeepClone() ?? new JObject());
#if UNITY_5_3_OR_NEWER
    // The one shared behavior of this definition, made here (not by `new` at the use) and kept: a trait keeps no state of its own.
    public PNAreaTrait Behavior => behavior ?? (behavior = (PNAreaTrait)Activator.CreateInstance(ImplementationType));
#endif
}

// One trait as an area declares it: the trait's ID and the payload it gets on this area.
public sealed class PNAreaTraitUse
{
    private readonly JToken payload;
    public EveId Id { get; }
    public JToken Payload => payload.DeepClone();
    public PNAreaTraitUse(EveId id, JToken data = null)
    {
        if (!id.IsValid) throw new ArgumentException("Area trait ID required.", nameof(id));
        Id = id; payload = data?.DeepClone() ?? new JObject();
    }
    // The `traits` entry field: an array of { "id": "...", "data": { } }, no repeated id.
    public static IReadOnlyList<PNAreaTraitUse> ParseList(JToken token)
    {
        if (!(token is JArray array)) throw new FormatException("Area traits must be an array.");
        var list = new List<PNAreaTraitUse>(); var seen = new HashSet<EveId>();
        foreach (var item in array)
        {
            if (!(item is JObject obj)) throw new FormatException("An area trait must be an object.");
            foreach (var property in obj.Properties()) if (property.Name != "id" && property.Name != "data") throw new FormatException("Unknown area trait field " + property.Name + ".");
            if (obj["id"] == null || obj["id"].Type != JTokenType.String || !EveId.TryParse(obj["id"].Value<string>(), out var id)) throw new FormatException("An area trait needs an id string.");
            if (obj["data"] != null && obj["data"].Type != JTokenType.Object) throw new FormatException("Area trait data must be an object.");
            if (!seen.Add(id)) throw new FormatException("Area trait " + id + " is declared twice.");
            list.Add(new PNAreaTraitUse(id, obj["data"]));
        }
        return list;
    }
}
