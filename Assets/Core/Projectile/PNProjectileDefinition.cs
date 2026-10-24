using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;

// Shared immutable registration (same shape as PNAreaDefinition): an id, one concrete implementation and its validated payload.
// The implementation owns its schema, movement and reaction to contacts; mutable state lives on each instance.
public class PNProjectileDefinition : PNInstanceDefinition
{
    private static readonly string[] Fields = { "destroyable" };
    public override string Kind => "projectile";
    public override IReadOnlyList<string> EntryFields => Fields;
    // EN: Whether gameplay clearing (PNProjectileManager.Destroy without bypass) may end projectiles of this definition. Lifecycle End is never blocked by it.
    // VI: Xoá do gameplay (PNProjectileManager.Destroy không bypass) có kết thúc được đạn của định nghĩa này không. End vòng đời không bao giờ bị chặn.
    public bool Destroyable { get; }
    public PNProjectileDefinition(EveId id, Type implementation, JToken data, Action<JToken, EveRegistryHub> validator, Action<JToken> inputValidator,
        Action<JToken, PNContentReferenceContext> validateReferences = null, bool destroyable = true) : base(id, implementation, data, validator, inputValidator, validateReferences)
    {
#if UNITY_5_3_OR_NEWER
        RequireImplementation(implementation, typeof(PNProjectileInstance), "Projectile");
#endif
        Destroyable = destroyable;
    }
    // Parses the payload once per definition (the payload is immutable) and caches the result for every projectile of it.
    private object cachedSettings;
    public T Settings<T>(Func<JToken, T> parse) where T : class
    {
        if (cachedSettings is T known) return known;
        var value = parse(Payload); cachedSettings = value; return value;
    }
    // A derived projectile keeps the parent's destroyable flag unless the content declares its own.
    public PNProjectileDefinition Derive(EveId id, JToken data, bool? destroyable = null) => new PNProjectileDefinition(id, ImplementationType, data, Validator, InputValidator, ReferenceValidator, destroyable ?? Destroyable);
    public override PNInstanceDefinition Derive(EveId id, JToken data, JObject entry)
    {
        var token = entry?["destroyable"];
        if (token != null && token.Type != JTokenType.Boolean) throw new FormatException("Projectile destroyable must be a boolean.");
        return Derive(id, data, token == null ? (bool?)null : token.Value<bool>());
    }
#if UNITY_5_3_OR_NEWER
    // Construction happens only through the selected definition, before pool callback discovery.
    public override UnityEngine.GameObject CreatePoolTemplate()
    {
        var root = new UnityEngine.GameObject("Projectile template: " + Id);
        root.SetActive(false); root.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
        root.AddComponent(ImplementationType); root.AddComponent<PNProjectileClock>(); return root;
    }
#endif
}
