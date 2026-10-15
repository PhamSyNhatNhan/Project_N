using System;
using Newtonsoft.Json.Linq;

// EN: Optional combat payload; old visual-only slash data remains supported.
// VI: Payload combat tùy chọn; dữ liệu slash chỉ VFX cũ vẫn dùng được.
public sealed class PNSlashHitSettings
{
    public PNSkillValue Value => Values.RequireSingle();
    public PNSkillValues Values { get; }
    public bool CanCrit { get; }
    public double? IFrameDuration { get; }
    public float Radius { get; }
    public float InnerRadius { get; }
    public float Angle { get; }
    public float Thickness { get; }
    public float ForwardOffset { get; }
    public float VerticalOffset { get; }
    public float WindowStart { get; }
    public float WindowEnd { get; }
    public int LayerMask { get; }
    public bool FollowCaster { get; }
    public bool HitTriggers { get; }
    public float SampleInterval { get; }
    public float Duration { get; }
    public bool PrefabDriven { get; }
    private PNSlashHitSettings(JObject perform, JObject hitbox, PNSlashVfxSettings visual)
    {
        foreach (var field in hitbox.Properties())
            if (field.Name != "radius" && field.Name != "innerRadius" && field.Name != "angle" && field.Name != "thickness" &&
                field.Name != "forwardOffset" && field.Name != "verticalOffset" && field.Name != "windowStart" && field.Name != "windowEnd" &&
                field.Name != "layerMask" && field.Name != "followCaster" && field.Name != "hitTriggers" && field.Name != "sampleInterval")
                throw new FormatException($"Unknown slash hitbox field: {field.Name}.");
        Values = PNSkillValues.Parse(perform["value"]);
        CanCrit = Boolean(perform, "canCrit", true);
        IFrameDuration = PNDamage.ParseIFrameDuration(perform);
        Duration = visual.Duration; PrefabDriven = perform["prefab"] != null;
        if (PrefabDriven)
            foreach (var field in new[] { "radius", "innerRadius", "angle", "thickness", "forwardOffset", "verticalOffset", "layerMask" })
                if (hitbox[field] != null) throw new FormatException("Prefab-driven hitbox field belongs on the hitbox component: " + field);
        Values.ValidateNonnegativeAmounts();
        Radius = Number(hitbox, "radius", visual.Radius); InnerRadius = Number(hitbox, "innerRadius", 0f);
        Angle = Number(hitbox, "angle", 100f); Thickness = Number(hitbox, "thickness", .4f);
        ForwardOffset = Number(hitbox, "forwardOffset", visual.ForwardOffset);
        VerticalOffset = Number(hitbox, "verticalOffset", visual.Height);
        WindowStart = Number(hitbox, "windowStart", 0f); WindowEnd = Number(hitbox, "windowEnd", visual.Duration);
        if (Radius <= 0f || float.IsInfinity(Radius * 2f) || InnerRadius < 0f || InnerRadius >= Radius ||
            Angle <= 0f || Angle > 360f || Thickness <= 0f || WindowStart < 0f || WindowEnd <= WindowStart || WindowEnd > visual.Duration)
            throw new FormatException("Invalid slash hitbox dimensions or window; window must lie within VFX duration.");
        var mask = hitbox["layerMask"];
        if (mask != null && mask.Type != JTokenType.Integer) throw new FormatException("Slash layerMask must be an integer.");
        try { LayerMask = mask == null ? ~0 : mask.Value<int>(); }
        catch (Exception error) { throw new FormatException("Slash layerMask must fit Int32.", error); }
        FollowCaster = Boolean(hitbox, "followCaster", false); HitTriggers = Boolean(hitbox, "hitTriggers", false);
        SampleInterval = Number(hitbox, "sampleInterval", .004f);
        if (SampleInterval <= 0f || (WindowEnd - WindowStart) / SampleInterval > 4096f)
            throw new FormatException("Slash sampling requires a positive interval and at most 4096 steps per window.");
    }
    public static PNSlashHitSettings Parse(JToken payload)
    {
        var visual = new PNSlashVfxSettings(payload);
        var perform = (JObject)payload;
        if (perform["hitbox"] == null && perform["value"] == null) return null;
        if (!(perform["hitbox"] is JObject hitbox)) throw new FormatException("Combat slash requires a hitbox object and value.");
        return new PNSlashHitSettings(perform, hitbox, visual);
    }
    private static float Number(JObject data, string field, float fallback)
    {
        var value = data[field];
        if (value != null && value.Type != JTokenType.Float && value.Type != JTokenType.Integer)
            throw new FormatException($"Slash hitbox {field} must be numeric.");
        float result = value == null ? fallback : value.Value<float>();
        if (float.IsNaN(result) || float.IsInfinity(result)) throw new FormatException($"Slash hitbox {field} must be finite.");
        return result;
    }
    private static bool Boolean(JObject data, string field, bool fallback)
    {
        var value = data[field];
        if (value != null && value.Type != JTokenType.Boolean) throw new FormatException($"Slash hitbox {field} must be Boolean.");
        return value == null ? fallback : value.Value<bool>();
    }
}
