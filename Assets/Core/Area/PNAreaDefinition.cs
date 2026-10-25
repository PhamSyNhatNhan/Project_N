using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// Shared immutable registration. Each implementation owns its schema and runtime behavior.
public class PNAreaDefinition : PNInstanceDefinition
{
    public override string Kind => "area";
    public PNAreaDefinition(EveId id, Type implementation, JToken data, Action<JToken, EveRegistryHub> validator, Action<JToken> inputValidator,
        Action<JToken, PNContentReferenceContext> validateReferences = null) : base(id, implementation, data, validator, inputValidator, validateReferences)
    {
#if UNITY_5_3_OR_NEWER
        RequireImplementation(implementation, typeof(PNAreaInstance), "Area");
#endif
    }
    public PNAreaDefinition Derive(EveId id, JToken data) => new PNAreaDefinition(id, ImplementationType, data, Validator, InputValidator, ReferenceValidator);
    public override PNInstanceDefinition Derive(EveId id, JToken data, JObject entry) => Derive(id, data);
#if UNITY_5_3_OR_NEWER
    // Construction happens only through the selected definition, before pool callback discovery.
    public override bool HasPoolTemplate => true;
    public override UnityEngine.GameObject CreatePoolTemplate()
    {
        var root = new UnityEngine.GameObject("Area template: " + Id);
        root.SetActive(false); root.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
        root.AddComponent(ImplementationType); return root;
    }
#endif
}
