using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: Moving blade pose shared by ribbon, trail and damage.
// VI: Dải năng lượng, trail và sát thương dùng chung tư thế lưỡi chém.
public sealed class EveSweepSettings
{
    public float Duration, SweepStart, SweepEnd, FadeEnd, StartAngle, EndAngle, Tilt;
    public float WindupAngle, RecoveryAngle, TrailDepth;
    public float Radius, InnerRadius, Height, BladeWidth, TrailFraction, Intensity, ParticleRate, TrailWidth;
    public float HitThickness, SampleInterval, ParticleSize, ParticleLifetime, ParticleSpeed, Afterglow;
    public PNSkillValue Value;
    private PNSkillValues compositeValue;
    // Keep the public legacy field authoritative for single-value code mods.
    public PNSkillValues Values => Value != null ? new PNSkillValues(new[] { Value }) : compositeValue;
    public double? IFrameDuration { get; private set; }
    public static EveSweepSettings Parse(JToken token)
    {
        if (!(token is JObject obj)) throw new FormatException("Eve sweep payload is required.");
        var result = new EveSweepSettings();
        result.IFrameDuration = PNDamage.ParseIFrameDuration(obj);
        foreach (var field in typeof(EveSweepSettings).GetFields())
        {
            if (field.Name == nameof(Value))
            {
                var values = PNSkillValues.Parse(obj["value"]);
                if (values.Count == 1) result.Value = values[0];
                else result.compositeValue = values;
                continue;
            }
            string key = char.ToLowerInvariant(field.Name[0]) + field.Name.Substring(1);
            var number = obj[key];
            if (number == null || (number.Type != JTokenType.Float && number.Type != JTokenType.Integer)) throw new FormatException("Missing sweep setting: " + key);
            float value = number.Value<float>();
            if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 1000) throw new FormatException("Invalid sweep setting: " + key);
            field.SetValue(result, value);
        }
        foreach (var p in obj.Properties())
            if (p.Name != "tags" && p.Name != "iframeDuration" && (p.Name.Length == 0 || typeof(EveSweepSettings).GetField(char.ToUpperInvariant(p.Name[0]) + p.Name.Substring(1)) == null))
                throw new FormatException("Unknown Eve sweep field: " + p.Name);
        result.Values.ValidateNonnegativeAmounts();
        if (result.Duration <= 0 || result.Radius <= 0 || result.InnerRadius < 0 || result.InnerRadius >= result.Radius ||
            result.SweepStart < 0 || result.SweepEnd <= result.SweepStart || result.FadeEnd <= result.SweepEnd || result.FadeEnd > 1 ||
            result.TrailDepth <= 0 || result.BladeWidth <= 0 || result.TrailFraction <= 0 || result.TrailFraction > 1 || result.HitThickness <= 0 ||
            result.ParticleSize<=0 || result.ParticleLifetime<=0 || result.ParticleSpeed<0 || result.Afterglow<0 || result.Afterglow>1 ||
            result.SampleInterval < .001f || result.SampleInterval > .02f || result.Intensity <= 0 || result.ParticleRate < 0 || result.TrailWidth <= 0 ||
            Math.Abs(result.EndAngle - result.StartAngle) > 180 || result.EndAngle == result.StartAngle)
            throw new FormatException("Invalid Eve sweep geometry/timeline.");
        return result;
    }
    public float SweepProgress(float normalized)
    {
        float t = Math.Max(0, Math.Min(1, (normalized - SweepStart) / (SweepEnd - SweepStart)));
        return t * t * (3 - 2 * t);
    }
#if UNITY_5_3_OR_NEWER
    public float SwordAngle(float normalized)
    {
        if (normalized < SweepStart) return Mathf.Lerp(WindupAngle, StartAngle, Mathf.SmoothStep(0, 1, normalized / SweepStart));
        if (normalized > SweepEnd) return Mathf.Lerp(EndAngle, RecoveryAngle, Mathf.SmoothStep(0, 1, (normalized - SweepEnd) / (1 - SweepEnd)));
        return Mathf.Lerp(StartAngle, EndAngle, SweepProgress(normalized));
    }
    public Vector3 SwordDirection(float normalized)
    {
        float angle = SwordAngle(normalized) * Mathf.Deg2Rad;
        return Quaternion.AngleAxis(Tilt, Vector3.right) * new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
    }
    public Vector3 Point(float progress, float radius)
    {
        float angle = Mathf.Lerp(StartAngle, EndAngle, progress) * Mathf.Deg2Rad;
        return Quaternion.AngleAxis(Tilt, Vector3.right) * new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius)
            + Vector3.up * Height;
    }
#endif
}
