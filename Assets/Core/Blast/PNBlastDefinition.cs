using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// How long a blast lives relative to whoever made it (Docs/StrikeBlastPlan.md D5): Independent = until it has detonated; Owner = also ends when the owner dies, is disabled or leaves the world;
// Action = the creating Action ends it (the creator holds the handle and calls End on cancel/exit/dispose).
public enum PNBlastDependency { Independent, Owner, Action }
// Shared immutable registration. Each implementation owns its schema and runtime behavior.
public class PNBlastDefinition : PNInstanceDefinition
{
    public override string Kind => "blast";
    public PNBlastDefinition(EveId id, Type implementation, JToken data, Action<JToken, EveRegistryHub> validator, Action<JToken> inputValidator,
        Action<JToken, PNContentReferenceContext> validateReferences = null) : base(id, implementation, data, validator, inputValidator, validateReferences)
    {
#if UNITY_5_3_OR_NEWER
        RequireImplementation(implementation, typeof(PNBlastInstance), "Blast");
#endif
    }
    public PNBlastDefinition Derive(EveId id, JToken data) => new PNBlastDefinition(id, ImplementationType, data, Validator, InputValidator, ReferenceValidator);
    public override PNInstanceDefinition Derive(EveId id, JToken data, JObject entry) => Derive(id, data);
#if UNITY_5_3_OR_NEWER
    // Construction happens only through the selected definition, before pool callback discovery.
    public override bool HasPoolTemplate => true;
    public override UnityEngine.GameObject CreatePoolTemplate()
    {
        var root = new UnityEngine.GameObject("Blast template: " + Id);
        root.SetActive(false); root.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
        root.AddComponent(ImplementationType); root.AddComponent<PNBlastClock>(); return root;
    }
#endif
}
