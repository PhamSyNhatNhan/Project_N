using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

// Shared immutable registration (same shape as PNAreaDefinition): an id, one concrete implementation and its validated payload.
// The implementation owns its schema, movement and reaction to contacts; mutable state lives on each instance.
public class PNProjectileDefinition : EveIIdentified
{
    private readonly JToken payload;
    private readonly Action<JToken, EveRegistryHub> validate;
    private readonly Action<JToken> validateInput;
    private readonly Action<JToken, PNContentReferenceContext> validateReferences;
    public EveId Id { get; }
    [JsonIgnore] public Type ImplementationType { get; }
    public string Implementation => ImplementationType.FullName;
    // EN: Whether gameplay clearing (PNProjectileManager.Destroy without bypass) may end projectiles of this definition. Lifecycle End is never blocked by it.
    // VI: Xoá do gameplay (PNProjectileManager.Destroy không bypass) có kết thúc được đạn của định nghĩa này không. End vòng đời không bao giờ bị chặn.
    public bool Destroyable { get; }
    public JToken Payload => payload.DeepClone();
    public PNProjectileDefinition(EveId id, Type implementation, JToken data, Action<JToken, EveRegistryHub> validator, Action<JToken> inputValidator,
        Action<JToken, PNContentReferenceContext> validateReferences = null, bool destroyable = true)
    {
        if (!id.IsValid || id.Namespace == "base") throw new ArgumentException("Original projectile ID required.");
#if UNITY_5_3_OR_NEWER
        if (implementation == null || !typeof(PNProjectileInstance).IsAssignableFrom(implementation) || implementation.IsAbstract || implementation.ContainsGenericParameters)
            throw new ArgumentException("Projectile implementation must be a concrete PNProjectileInstance component.");
#else
        if (implementation == null) throw new ArgumentNullException(nameof(implementation));
#endif
        Id = id; ImplementationType = implementation; payload = data?.DeepClone() ?? throw new ArgumentNullException(nameof(data));
        validate = validator ?? throw new ArgumentNullException(nameof(validator)); validateInput = inputValidator ?? throw new ArgumentNullException(nameof(inputValidator));
        this.validateReferences = validateReferences; Destroyable = destroyable;
    }
    internal void ValidatePayload(EveRegistryHub registry) => validate(Payload, registry);
    public void Validate(EveRegistryHub registry) { ValidatePayload(registry); ValidateReferences(new PNContentReferenceContext(registry)); }
    public void ValidateReferences(PNContentReferenceContext context) => validateReferences?.Invoke(Payload, context);
    public void ValidateInput(JToken input) => validateInput(input?.DeepClone());
    // Parses the payload once per definition (the payload is immutable) and caches the result for every projectile of it.
    private object cachedSettings;
    public T Settings<T>(Func<JToken, T> parse) where T : class
    {
        if (cachedSettings is T known) return known;
        var value = parse(payload); cachedSettings = value; return value;
    }
    // A derived projectile keeps the parent's destroyable flag unless the content declares its own.
    public PNProjectileDefinition Derive(EveId id, JToken data, bool? destroyable = null) => new PNProjectileDefinition(id, ImplementationType, data, validate, validateInput, validateReferences, destroyable ?? Destroyable);
#if UNITY_5_3_OR_NEWER
    // Construction happens only through the selected definition, before pool callback discovery.
    public virtual UnityEngine.GameObject CreatePoolTemplate()
    {
        var root = new UnityEngine.GameObject("Projectile template: " + Id);
        root.SetActive(false); root.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
        root.AddComponent(ImplementationType); root.AddComponent<PNProjectileClock>(); return root;
    }
#endif
}
