using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Data của 1 banner — deserialize từ {bannerId}.json trong Resources/Data/Gacha/
/// </summary>
[Serializable]
public class GachaBannerData
{
    public string bannerId       { get; set; } = "";
    public string talent         { get; set; } = "";   // map sang Talent enum
    public string displayName    { get; set; } = "";
    public string description    { get; set; } = "";
    public string portraitPath   { get; set; } = "";   // Resources path
    public float  talentRate     { get; set; } = 2f;   // % base rate
    public int    pityHardLimit  { get; set; } = 90;
    public int    softPityStart  { get; set; } = 68;
    public int    costPerPull    { get; set; } = 150;

    // ── Runtime (không serialize) ─────────────────────────────────
    [JsonIgnore] public Sprite portrait { get; set; }
    [JsonIgnore] public Talent talentEnum { get; set; }
}

/// <summary>
/// Kết quả 1 lần pull.
/// </summary>
public class GachaPullResult
{
    public GachaResultType type    { get; set; }
    public Talent          talent  { get; set; }   // chỉ dùng khi type == Talent
    public int             shards  { get; set; }   // chỉ dùng khi type == CharacterShard
}

public enum GachaResultType
{
    Talent,
    CharacterShard
}

/// <summary>
/// Lưu pity của từng banner trong UserData.
/// key = bannerId, value = pity hiện tại
/// </summary>
[Serializable]
public class GachaSaveData
{
    public Dictionary<string, int> pityMap { get; set; } = new Dictionary<string, int>();

    public int GetPity(string bannerId)
    {
        if (pityMap.TryGetValue(bannerId, out int p)) return p;
        return 0;
    }

    public void SetPity(string bannerId, int pity)
    {
        pityMap[bannerId] = pity;
    }
}