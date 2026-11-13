using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: The `perform` payload of the registered Action `project_n:locomotion_action` (Docs/SandboxPlan.md, step G12): `{ "locomotion": "<registered locomotion id>", "duration": seconds }`. The Action pushes that
//     locomotion on the skill's body for `duration` seconds (or until the skill is cancelled) and pops it again. Both fields are required; unknown fields are refused.
// VI: Payload `perform` của Action `project_n:locomotion_action`: đẩy locomotion lên thân của skill trong `duration` giây rồi gỡ.
public sealed class PNLocomotionSettings
{
    public const float MaxDuration = 600f;
    public EveId Locomotion { get; }
    public float Duration { get; }

    public PNLocomotionSettings(EveId locomotion, float duration)
    {
        if (!locomotion.IsValid) throw new FormatException("Locomotion action needs a valid locomotion id.");
        if (float.IsNaN(duration) || float.IsInfinity(duration) || !(duration > 0f) || duration > MaxDuration) throw new FormatException("Locomotion action duration is greater than 0 and at most " + MaxDuration + " seconds.");
        Locomotion = locomotion; Duration = duration;
    }

    public static PNLocomotionSettings Parse(JToken token)
    {
        var obj = token as JObject ?? throw new FormatException("Locomotion action perform payload must be an object.");
        foreach (var property in obj.Properties()) if (property.Name != "locomotion" && property.Name != "duration") throw new FormatException("Unknown locomotion action field: " + property.Name);
        if (obj["locomotion"]?.Type != JTokenType.String || !EveId.TryParse(obj["locomotion"].Value<string>(), out var id)) throw new FormatException("Locomotion action needs a registered locomotion id.");
        if (obj["duration"] == null || (obj["duration"].Type != JTokenType.Integer && obj["duration"].Type != JTokenType.Float)) throw new FormatException("Locomotion action duration must be a number.");
        return new PNLocomotionSettings(id, obj["duration"].Value<float>());
    }

    // The locomotion has to be registered.
    public void ValidateReferences(EveRegistryHub hub)
    {
        if (!hub.Get<PNLocomotionDefinition>().TryGet(Locomotion, out _)) throw new FormatException("Locomotion action names " + Locomotion + ", which is not a registered locomotion.");
    }
}
