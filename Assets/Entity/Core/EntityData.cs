using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

// ── StatData ──────────────────────────────────────────────────────────
public class StatData
{
    public float baseHealth              { get; set; } = 5000f;
    public float baseDefense             { get; set; } = 100f;
    public float baseAttack              { get; set; } = 0f;
    public float baseAttackSpeed         { get; set; } = 100f;
    public float baseCritRate            { get; set; } = 5f;
    public float baseCritDamage          { get; set; } = 50f;
    public float baseResistantPhysical   { get; set; } = 0f;
    public float baseResistantMagic      { get; set; } = 0f;

    [JsonExtensionData]
    public Dictionary<string, JToken> extraFields { get; set; } = new Dictionary<string, JToken>();

    public T Get<T>(string key, T defaultValue = default)
    {
        if (extraFields != null && extraFields.TryGetValue(key, out JToken token))
            return token.ToObject<T>();
        return defaultValue;
    }
}

// ── MoveData ──────────────────────────────────────────────────────────
public class MoveData
{
    public float baseMoveSpeed { get; set; } = 5f;

    [JsonExtensionData]
    public Dictionary<string, JToken> extraFields { get; set; } = new Dictionary<string, JToken>();

    public T Get<T>(string key, T defaultValue = default)
    {
        if (extraFields != null && extraFields.TryGetValue(key, out JToken token))
            return token.ToObject<T>();
        return defaultValue;
    }
}

// ── SkillData ─────────────────────────────────────────────────────────
public class SkillData
{
    public string       skillId    { get; set; } = "";
    public string       skillType  { get; set; } = "";
    public float        cooldown   { get; set; } = 0f;
    public string       damageType { get; set; } = "";
    public List<float>  damage     { get; set; } = new List<float>();

    // ── Icon ──────────────────────────────────────────────────────
    // Path trong Resources folder, ví dụ: "Skills/NormalKnife"
    public string  iconPath { get; set; } = "";

    // Được load khi ApplyData() — không serialize
    [JsonIgnore]
    public UnityEngine.Sprite icon { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JToken> extraFields { get; set; } = new Dictionary<string, JToken>();

    public T Get<T>(string key, T defaultValue = default)
    {
        if (extraFields != null && extraFields.TryGetValue(key, out JToken token))
            return token.ToObject<T>();
        return defaultValue;
    }
}

// ── SkillListData ─────────────────────────────────────────────────────
public class SkillListData
{
    public List<SkillData> skills { get; set; } = new List<SkillData>();
}

// ── EntityData ────────────────────────────────────────────────────────
public class EntityData
{
    public StatData      stat  { get; set; }
    public MoveData      move  { get; set; }
    public SkillListData skill { get; set; }
}