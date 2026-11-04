using System;
using EveEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// EN: Kind "interactable" of the shared instances registry (Docs/InteractablePlan.md I1): something in the world a player stands near and activates. The definition holds the
//     tuning every interactable has; what activating it does is the handler the definition was registered with (IPNInteractionHandler, PNInteractionContracts.cs).
// VI: Kind "interactable" của registry instances: vật trong world mà người chơi đứng gần và kích hoạt. Định nghĩa giữ phần chỉnh chung; việc làm khi kích hoạt nằm ở handler.
public enum PNInteractionPermission { Host, Everyone }
public enum PNInteractionUses { Once, Repeat }

// EN: The `data` payload of an interactable. Required: range, permission, uses, label. Optional: icon, params.
//       range: metres in [0.3, 6], flat distance from the player to the object's anchor       permission: "host" (host and offline only; others see the entry dimmed) | "everyone"
//       uses: "once" (the object switches itself off after the first successful use) | "repeat"       label: localization key, namespaced, at most 128 characters
//       icon: Resources path, at most 128 characters (empty = the default icon)       params: an object only the handler understands, checked by the handler's own validator
// VI: Payload `data` của interactable. Bắt buộc: range, permission, uses, label. Tuỳ chọn: icon, params (chỉ handler hiểu).
public sealed class PNInteractionSettings
{
    public const double MinRange = 0.3, MaxRange = 6.0;
    public const int MaxText = 128;
    public double Range { get; }
    public PNInteractionPermission Permission { get; }
    public PNInteractionUses Uses { get; }
    public string Label { get; }
    public string Icon { get; }
    [JsonIgnore] public JToken Params { get; }

    public PNInteractionSettings(double range, PNInteractionPermission permission, PNInteractionUses uses, string label, string icon, JToken handlerParams)
    {
        if (double.IsNaN(range) || double.IsInfinity(range) || range < MinRange || range > MaxRange) throw new FormatException("interactable range must be a number in [" + MinRange + ", " + MaxRange + "].");
        if (!Enum.IsDefined(typeof(PNInteractionPermission), permission)) throw new FormatException("interactable permission must be host or everyone.");
        if (!Enum.IsDefined(typeof(PNInteractionUses), uses)) throw new FormatException("interactable uses must be once or repeat.");
        if (string.IsNullOrEmpty(label) || label.Length > MaxText || !EveId.TryParse(label, out _)) throw new FormatException("interactable label must be a namespaced localization key of at most " + MaxText + " characters.");
        icon = icon ?? "";
        if (icon.Length > MaxText) throw new FormatException("interactable icon is at most " + MaxText + " characters.");
        Range = range; Permission = permission; Uses = uses; Label = label; Icon = icon; Params = handlerParams?.DeepClone();
    }

    public static PNInteractionSettings Parse(JToken data)
    {
        var obj = PNBuffContentBatch.Object(data);
        PNBuffContentBatch.Fields(obj, "range", "permission", "uses", "label", "icon", "params");
        foreach (var required in new[] { "range", "permission", "uses", "label" })
            if (obj[required] == null) throw new FormatException("interactable " + required + " is required.");
        PNInteractionPermission permission;
        switch (PNBuffContentBatch.String(obj, "permission"))
        {
            case "host": permission = PNInteractionPermission.Host; break;
            case "everyone": permission = PNInteractionPermission.Everyone; break;
            default: throw new FormatException("interactable permission must be host or everyone.");
        }
        PNInteractionUses uses;
        switch (PNBuffContentBatch.String(obj, "uses"))
        {
            case "once": uses = PNInteractionUses.Once; break;
            case "repeat": uses = PNInteractionUses.Repeat; break;
            default: throw new FormatException("interactable uses must be once or repeat.");
        }
        if (obj["params"] != null && !(obj["params"] is JObject)) throw new FormatException("interactable params must be an object.");
        return new PNInteractionSettings(PNBuffContentBatch.Number(obj, "range"), permission, uses, PNBuffContentBatch.String(obj, "label"), PNBuffContentBatch.OptionalString(obj, "icon"), obj["params"]);
    }
}

public sealed class PNInteractableDefinition : PNInstanceDefinition
{
    public const string KindName = "interactable";
    public override string Kind => KindName;
    [JsonIgnore] public PNInteractionSettings Settings { get; }
    private readonly Func<IPNInteractionHandler> create;
    private readonly Action<JToken> validateParams;
    private IPNInteractionHandler handler;
    // The one shared instance of this interactable's handler, made by the factory the definition was registered with (no reflection: safe under IL2CPP stripping).
    [JsonIgnore] public IPNInteractionHandler Handler => handler ?? (handler = Make());

    // validateParams checks the handler-specific `params` object (null = the handler takes none, so `params` must be absent or empty).
    public PNInteractableDefinition(EveId id, Type handlerType, Func<IPNInteractionHandler> create, JToken data, Action<JToken> validateParams = null)
        : base(id, RequireHandler(handlerType), data, (payload, hub) => Check(payload, validateParams), input => { }, (payload, context) => { })
    {
        this.create = create ?? throw new ArgumentNullException(nameof(create));
        this.validateParams = validateParams;
        Settings = Check(data, validateParams);
    }
    private static PNInteractionSettings Check(JToken data, Action<JToken> validateParams)
    {
        var settings = PNInteractionSettings.Parse(data);
        if (validateParams != null) validateParams(settings.Params);
        else if (settings.Params != null && settings.Params.HasValues) throw new FormatException("this interactable takes no params.");
        return settings;
    }
    private IPNInteractionHandler Make()
    {
        var made = create();
        if (made == null || made.GetType() != ImplementationType) throw new InvalidOperationException("Interactable " + Id + " handler factory must return a " + ImplementationType.Name + ".");
        return made;
    }
    private static Type RequireHandler(Type type)
    {
        if (type == null || type.IsAbstract || type.IsInterface || type.ContainsGenericParameters || !typeof(IPNInteractionHandler).IsAssignableFrom(type))
            throw new ArgumentException("Interactable implementation must be a concrete IPNInteractionHandler class.");
        return type;
    }

    // Same handler with new data (JSON `instances` entry whose factoryId is this definition).
    public override PNInstanceDefinition Derive(EveId id, JToken data, JObject entry) => new PNInteractableDefinition(id, ImplementationType, create, data, validateParams);
}
