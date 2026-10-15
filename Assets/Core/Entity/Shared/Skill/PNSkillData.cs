using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: Core's skill envelope; payload schemas belong to the concrete skill and action.
// VI: Khung dữ liệu skill của Core; schema payload thuộc skill và action cụ thể.
public class PNSkillData
{
    private readonly JToken skill;
    public virtual JToken Skill => skill?.DeepClone();
    public virtual PNSkillActionData Action { get; }

    public PNSkillData(JToken skill, PNSkillActionData action)
    {
        this.skill = Copy(skill);
        Action = action ?? throw new ArgumentNullException(nameof(action));
    }

    public static PNSkillData Parse(object data)
    {
        if (data == null || data is JToken nullToken && nullToken.Type == JTokenType.Null) return null;
        if (data is PNSkillData typed)
            return new PNSkillData(typed.Skill, typed.Action.Copy());
        if (!(data is JObject root)) throw new FormatException("Core skill data must be an object or PNSkillData.");
        CheckFields(root, "skill", "action", "categoryIds", "affectedByAttackSpeed", "effects", "triggerIds");
        if (root["action"] == null && root["categoryIds"] != null) return null;
        if (!(root["action"] is JObject action)) throw new FormatException("Core skill data requires an action object.");
        CheckFields(action, "id", "start", "perform", "exit", "cancel");
        if (action["id"]?.Type != JTokenType.String) throw new FormatException("Action id must be a string.");
        return new PNSkillData(root["skill"], new PNSkillActionData(EveId.Parse(action["id"].Value<string>()),
            action["start"], action["perform"], action["exit"], action["cancel"]));
    }

    internal static JToken Copy(JToken value) => value == null || value.Type == JTokenType.Null ? null : value.DeepClone();
    private static void CheckFields(JObject value, params string[] allowed)
    {
        foreach (var property in value.Properties())
            if (!allowed.Contains(property.Name)) throw new FormatException($"Unknown Core skill field: {property.Name}.");
    }
}

// EN: Selects a reusable action and supplies isolated payloads for its four hooks.
// VI: Chọn action dùng lại và cung cấp payload riêng cho bốn hook.
public class PNSkillActionData
{
    private readonly JToken start, perform, exit, cancel;
    public virtual EveId Id { get; }
    public virtual JToken Start => start?.DeepClone();
    public virtual JToken Perform => perform?.DeepClone();
    public virtual JToken Exit => exit?.DeepClone();
    public virtual JToken Cancel => cancel?.DeepClone();
    public virtual IReadOnlyList<EveId> StartTags { get; }
    public virtual IReadOnlyList<EveId> PerformTags { get; }
    public virtual IReadOnlyList<EveId> ExitTags { get; }
    public virtual IReadOnlyList<EveId> CancelTags { get; }

    public PNSkillActionData(EveId id, JToken start = null, JToken perform = null, JToken exit = null, JToken cancel = null)
    {
        if (!id.IsValid) throw new ArgumentException("Action ID is required.", nameof(id));
        Id = id;
        this.start = PNSkillData.Copy(start);
        this.perform = PNSkillData.Copy(perform);
        this.exit = PNSkillData.Copy(exit);
        this.cancel = PNSkillData.Copy(cancel);
        StartTags = ReadTags(this.start);
        PerformTags = ReadTags(this.perform);
        ExitTags = ReadTags(this.exit);
        CancelTags = ReadTags(this.cancel);
    }

    // EN: Only the optional tags field is shared; every other payload field belongs to the action.
    // VI: Chỉ trường tags tùy chọn là dùng chung; mọi trường payload khác thuộc action.
    private static IReadOnlyList<EveId> ReadTags(JToken payload)
    {
        var token = (payload as JObject)?["tags"];
        if (token == null) return Array.AsReadOnly(Array.Empty<EveId>());
        if (!(token is JArray array)) throw new FormatException("Action tags must be an array of ID strings.");
        var ids = new HashSet<EveId>();
        foreach (var entry in array)
        {
            if (entry.Type != JTokenType.String) throw new FormatException("Action tags must contain ID strings only.");
            ids.Add(EveId.Parse(entry.Value<string>()));
        }
        return Array.AsReadOnly(ids.OrderBy(id => id).ToArray());
    }

    public virtual void ValidateTags(EveRegistryHub registry)
    {
        if (registry == null || !registry.IsSealed) throw new ArgumentException("Sealed registry required.", nameof(registry));
        foreach (var id in StartTags.Concat(PerformTags).Concat(ExitTags).Concat(CancelTags).Distinct())
            registry.Tags().Get(id);
    }

    public virtual PNSkillActionData Copy() => new PNSkillActionData(Id, start, perform, exit, cancel);
}
