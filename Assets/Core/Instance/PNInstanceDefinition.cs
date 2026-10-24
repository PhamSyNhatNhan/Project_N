using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// EN: Shared immutable definition of something spawned into a world (area, projectile, later summon/item/mod kinds).
//     One registry holds every kind and the kind is the subclass. Spawn rules (tables, waves, drops) are not part of it:
//     whatever uses a definition owns its rules. Each kind's manager keeps its own request type and lifetime.
// VI: Định nghĩa bất biến dùng chung của thứ sinh vào world (area, projectile, sau này summon/item/kind của mod).
//     Một registry chứa mọi kind và kind là lớp con. Luật sinh (bảng, đợt, rơi đồ) không thuộc lớp này: bên dùng tự quyết.
public abstract class PNInstanceDefinition : EveIIdentified
{
    private readonly JToken payload;
    private readonly Action<JToken, EveRegistryHub> validate;
    private readonly Action<JToken> validateInput;
    private readonly Action<JToken, PNContentReferenceContext> validateReferences;
    public EveId Id { get; }
    // Stable lowercase kind name ("area", "projectile"): used in messages and the coop content hash. Subclasses return a constant.
    public abstract string Kind { get; }
    [JsonIgnore] public Type ImplementationType { get; }
    public string Implementation => ImplementationType.FullName;
    public JToken Payload => payload.DeepClone();
    // Extra JSON entry fields this kind accepts beside id / factoryId / data.
    [JsonIgnore] public virtual IReadOnlyList<string> EntryFields => System.Array.Empty<string>();
    protected PNInstanceDefinition(EveId id, Type implementation, JToken data, Action<JToken, EveRegistryHub> validator, Action<JToken> inputValidator,
        Action<JToken, PNContentReferenceContext> validateReferences)
    {
        if (!id.IsValid || id.Namespace == "base") throw new ArgumentException("Original " + Kind + " ID required.");
        Id = id; ImplementationType = implementation ?? throw new ArgumentNullException(nameof(implementation));
        payload = data?.DeepClone() ?? throw new ArgumentNullException(nameof(data));
        validate = validator ?? throw new ArgumentNullException(nameof(validator)); validateInput = inputValidator ?? throw new ArgumentNullException(nameof(inputValidator));
        this.validateReferences = validateReferences;
    }
    internal void ValidatePayload(EveRegistryHub registry) => validate(Payload, registry);
    public void Validate(EveRegistryHub registry) { ValidatePayload(registry); ValidateReferences(new PNContentReferenceContext(registry)); }
    public void ValidateReferences(PNContentReferenceContext context) => validateReferences?.Invoke(Payload, context);
    public void ValidateInput(JToken input) => validateInput(input?.DeepClone());
    // Subclasses reuse the validators for a derived definition of the same kind and implementation.
    protected Action<JToken, EveRegistryHub> Validator => validate;
    protected Action<JToken> InputValidator => validateInput;
    protected Action<JToken, PNContentReferenceContext> ReferenceValidator => validateReferences;
    // JSON derivation: same kind and implementation with a new payload. The subclass reads its own entry fields (already checked against EntryFields).
    public abstract PNInstanceDefinition Derive(EveId id, JToken data, JObject entry);
#if UNITY_5_3_OR_NEWER
    // Construction happens only through the selected definition, before pool callback discovery.
    public abstract UnityEngine.GameObject CreatePoolTemplate();
    // The implementation of a kind must be a concrete component of that kind's base.
    protected static void RequireImplementation(Type implementation, Type required, string kind)
    {
        if (implementation == null || !required.IsAssignableFrom(implementation) || implementation.IsAbstract || implementation.ContainsGenericParameters)
            throw new ArgumentException(kind + " implementation must be a concrete " + required.Name + " component.");
    }
#endif
}
