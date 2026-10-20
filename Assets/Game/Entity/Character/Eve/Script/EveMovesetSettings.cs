using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: Required JSON tuning; Inspector never overrides selected content.
// VI: Thông số bắt buộc từ JSON; Inspector không ghi đè dữ liệu đã chọn.
public sealed class EveMovesetSettings
{
    public float TargetRange, ComboReset;
    public EveId SkillMotion, EmpoweredMotion, RepeatMotion, UltimateMotion, RetreatMotion;
    public EveId BlazingAssaultBuffId, RisingMomentumBuffId;
    public float EmpoweredStopDistance;
    public float TargetStopDistance, ChargeDuration;
    public int BurstCost, BurstInitialStacks, BurstEndRefund, AttackGauge, SkillGauge, UltimateGauge, AttackStacks, SkillStacks;
    public float BurstAttackBonus, BurstAttackSpeedBonus, AttackUltimateReduction, SkillUltimateReduction, RepeatDelay, RepeatInterval;
    public EveEnergySettings Energy;
    public EveWeaponSettings Weapon;
    // Compatibility views derived from the selected registry profiles, never duplicate tuning.
    public float SkillDistance { get; private set; }
    public float EmpoweredDistance { get; private set; }
    public float RepeatDistance { get; private set; }
    public float UltimateDistance { get; private set; }
    public float DashDistance { get; private set; }
    public float RushDuration { get; private set; }
    public float DashDuration { get; private set; }
    public float RetreatDistance { get; private set; }
    public float RetreatDuration { get; private set; }
    public bool DashIgnoresEntities { get; private set; }
    public void ResolveMotionProfiles(Func<EveId,PNSkillData> get,PNMotionSettings dash)
    {
        var skill=PNMotionSettings.Parse(get(SkillMotion).Action.Perform);
        SkillDistance=skill.Distance;RushDuration=skill.Duration;
        EmpoweredDistance=PNMotionSettings.Parse(get(EmpoweredMotion).Action.Perform).Distance;
        RepeatDistance=PNMotionSettings.Parse(get(RepeatMotion).Action.Perform).Distance;
        UltimateDistance=PNMotionSettings.Parse(get(UltimateMotion).Action.Perform).Distance;
        var retreat=PNMotionSettings.Parse(get(RetreatMotion).Action.Perform);
        RetreatDistance=retreat.Distance;RetreatDuration=retreat.Duration;
        DashDistance=dash.Distance;DashDuration=dash.Duration;DashIgnoresEntities=dash.IgnoreEntities;
    }
    public static EveMovesetSettings Parse(JToken token)
    {
        if (!(token is JObject obj)) throw new FormatException("Eve moveset tuning is required.");
        var settings = new EveMovesetSettings();
        foreach (var field in typeof(EveMovesetSettings).GetFields())
        {
            if (field.Name == nameof(Energy)) { settings.Energy = EveEnergySettings.Parse(obj["energy"]); continue; }
            if (field.Name == nameof(Weapon)) { settings.Weapon = EveWeaponSettings.Parse(obj["weapon"]); continue; }
            string key = char.ToLowerInvariant(field.Name[0]) + field.Name.Substring(1);
            var number = obj[key];
            if(field.FieldType==typeof(EveId))
            {
                if(number?.Type!=JTokenType.String)throw new FormatException("Missing registered Eve reference: "+key);
                field.SetValue(settings,EveId.Parse(number.Value<string>()));continue;
            }
            if (field.FieldType == typeof(bool))
            {
                if (number?.Type != JTokenType.Boolean) throw new FormatException("Missing boolean Eve tuning: " + key);
                field.SetValue(settings, number.Value<bool>()); continue;
            }
            if (number == null || (number.Type != JTokenType.Integer && number.Type != JTokenType.Float))
                throw new FormatException("Missing numeric Eve tuning: " + key);
            double value = number.Value<double>();
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 1000000 ||
                (field.FieldType == typeof(int) && value != Math.Truncate(value)))
                throw new FormatException("Invalid Eve tuning: " + key);
            field.SetValue(settings, field.FieldType == typeof(int) ? (object)(int)value : (float)value);
        }
        foreach (var p in obj.Properties())
            if (p.Name != "energy" && p.Name != "weapon" && (p.Name.Length == 0 || typeof(EveMovesetSettings).GetField(char.ToUpperInvariant(p.Name[0]) + p.Name.Substring(1)) == null))
                throw new FormatException("Unknown Eve tuning: " + p.Name);
        if (settings.TargetRange <= 0 || settings.ComboReset <= 0 || settings.ChargeDuration <= 0 || settings.RepeatInterval <= 0 || settings.BurstCost <= 0)
            throw new FormatException("Eve durations, range, repeat interval and Burst cost must be positive.");
        return settings;
    }
}
public sealed class EveEnergySettings
{
    public float MotionTrailTime, MotionTrailWidth, BurstParticleRate, ChargeParticleRate, ParticleLifetime, AuraRadius, LightIntensity;
    public float BurstParticleMinSize, BurstParticleMaxSize, BurstParticleMinLifetime, BurstParticleMaxLifetime, BurstRiseMinSpeed, BurstRiseMaxSpeed;
    public int ActivationParticles, EndParticles, ImpactParticles;
    public static EveEnergySettings Parse(JToken token)
    {
        if (!(token is JObject obj)) throw new FormatException("Eve energy settings are required.");
        foreach (var field in typeof(EveEnergySettings).GetFields())
        {
            string key = char.ToLowerInvariant(field.Name[0]) + field.Name.Substring(1);
            var v = obj[key];
            if (v == null || (v.Type != JTokenType.Integer && v.Type != JTokenType.Float) ||
                v.Value<double>() < 0 || double.IsNaN(v.Value<double>()) || double.IsInfinity(v.Value<double>()) || v.Value<double>() > 1000 ||
                (field.FieldType == typeof(int) && v.Value<double>() != Math.Truncate(v.Value<double>())))
                throw new FormatException("Invalid energy setting: " + key);
        }
        foreach (var p in obj.Properties())
            if (p.Name.Length == 0 || typeof(EveEnergySettings).GetField(char.ToUpperInvariant(p.Name[0]) + p.Name.Substring(1)) == null)
                throw new FormatException("Unknown energy setting: " + p.Name);
        var result = obj.ToObject<EveEnergySettings>();
        if (result.ParticleLifetime <= 0 || result.MotionTrailTime <= 0 || result.AuraRadius <= 0)
            throw new FormatException("Energy lifetimes/radius must be positive.");
        if(result.BurstParticleMinSize<=0 || result.BurstParticleMaxSize<result.BurstParticleMinSize ||
            result.BurstParticleMinLifetime<=0 || result.BurstParticleMaxLifetime<result.BurstParticleMinLifetime ||
            result.BurstRiseMinSpeed<=0 || result.BurstRiseMaxSpeed<result.BurstRiseMinSpeed)
            throw new FormatException("Invalid Burst particle size, lifetime or upward speed range.");
        return result;
    }
}
