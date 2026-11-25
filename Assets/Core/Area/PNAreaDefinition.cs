using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;

// Shared immutable registration. Each implementation owns its schema and runtime behavior.
// `traits` (entry field): the traits the area carries, in order (PNAreaTraitDefinition); none by default.
public class PNAreaDefinition : PNInstanceDefinition
{
    private static readonly string[] Fields = { "traits" };
    public override string Kind => "area";
    public override IReadOnlyList<string> EntryFields => Fields;
    public IReadOnlyList<PNAreaTraitUse> Traits { get; }
    public PNAreaDefinition(EveId id, Type implementation, JToken data, Action<JToken, EveRegistryHub> validator, Action<JToken> inputValidator,
        Action<JToken, PNContentReferenceContext> validateReferences = null, IReadOnlyList<PNAreaTraitUse> traits = null) : base(id, implementation, data, validator, inputValidator, validateReferences)
    {
#if UNITY_5_3_OR_NEWER
        RequireImplementation(implementation, typeof(PNAreaInstance), "Area");
#endif
        Traits = traits ?? System.Array.Empty<PNAreaTraitUse>();
        var seen = new HashSet<EveId>();
        foreach (var use in Traits) if (use == null || !seen.Add(use.Id)) throw new ArgumentException("Area traits must be set and not repeated.", nameof(traits));
    }
    // A derived area keeps the parent's traits unless the content declares its own (an explicit empty list removes them).
    public PNAreaDefinition Derive(EveId id, JToken data, IReadOnlyList<PNAreaTraitUse> traits = null) => new PNAreaDefinition(id, ImplementationType, data, Validator, InputValidator, ReferenceValidator, traits ?? Traits);
    public override PNInstanceDefinition Derive(EveId id, JToken data, JObject entry)
    {
        var token = entry?["traits"];
        return Derive(id, data, token == null ? null : PNAreaTraitUse.ParseList(token));
    }
    // Each trait must be a registered area trait (inside the pack's dependencies) and its payload must pass that trait's own validator.
    public override void ValidateReferences(PNContentReferenceContext context)
    {
        base.ValidateReferences(context);
        foreach (var use in Traits)
        {
            context.Require<PNAreaTraitDefinition>(use.Id, "traits");
            context.Registry.AreaTraits().Get(use.Id).ValidatePayload(use.Payload);
        }
    }
#if UNITY_5_3_OR_NEWER
    // Construction happens only through the selected definition, before pool callback discovery.
    public override bool HasPoolTemplate => true;
    public override UnityEngine.GameObject CreatePoolTemplate()
    {
        var root = new UnityEngine.GameObject("Area template: " + Id);
        root.SetActive(false); root.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
        root.AddComponent(ImplementationType); root.AddComponent<PNAreaClock>(); return root;
    }
#endif
}
