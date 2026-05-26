using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base class quản lý toàn bộ skill của entity.
/// Load data từ JSON qua ILoadable, phân loại thành 3 cơ chế:
///   - AbilityCooldown : skill có cooldown timer thực sự
///   - AbilityCounter  : skill dựa trên stack/counter
///   - AbilityInfinite : hiển thị trạng thái, không có CD
/// Cung cấp IsReady() và Use() chung tự động tìm đúng loại.
/// </summary>
public class Skill : MonoBehaviour, ILoadable<SkillListData>
{
    protected bool canSkill = true;
    protected bool isSkill  = false;

    // ── Components ────────────────────────────────────────────────
    protected TimeScale timeScale;
    protected Stat      stat;

    // ── Data & CD ─────────────────────────────────────────────────
    // key = skillId
    protected Dictionary<string, SkillData>       skillData     = new Dictionary<string, SkillData>();
    protected Dictionary<string, AbilityCooldown> skillCd       = new Dictionary<string, AbilityCooldown>();
    protected Dictionary<string, AbilityCounter>  skillCounter  = new Dictionary<string, AbilityCounter>();
    protected Dictionary<string, AbilityInfinite> skillInfinite = new Dictionary<string, AbilityInfinite>();

    // ── Lifecycle ─────────────────────────────────────────────────
    protected virtual void Awake()
    {
        timeScale = GetComponent<TimeScale>();
        stat      = GetComponent<Stat>();
    }

    protected virtual void Start() {}

    protected virtual void Update()
    {
        foreach (var cd in skillCd.Values)
            cd.Tick(Time.deltaTime);

        foreach (var inf in skillInfinite.Values)
            inf.Tick(Time.deltaTime);

        foreach (var ct in skillCounter.Values)
            ct.Tick(Time.deltaTime);
    }

    // ── ILoadable ─────────────────────────────────────────────────
    public void LoadRawData(SkillListData data)
    {
        skillData.Clear();
        foreach (var skill in data.skills)
        {
            if (string.IsNullOrEmpty(skill.skillId))
            {
                Debug.LogWarning($"[Skill] Skill thiếu skillId, bỏ qua.");
                continue;
            }
            skillData[skill.skillId] = skill;
        }
    }

    public virtual void ApplyData()
    {
        skillCd.Clear();
        skillCounter.Clear();
        skillInfinite.Clear();

        string entityKey = stat != null
            ? $"{stat.NameCharacter}_{stat.GetInstanceID()}"
            : "";

        foreach (var pair in skillData)
        {
            SkillData data = pair.Value;

            // Load icon
            if (!string.IsNullOrEmpty(data.iconPath))
            {
                data.icon = Resources.Load<Sprite>(data.iconPath);
                if (data.icon == null)
                    Debug.LogWarning($"[Skill] Không tìm thấy icon: '{data.iconPath}'");
            }

            bool   showOnUI = data.Get<bool>("showOnUI", false);
            string cdType   = data.Get<string>("cdType", "cooldown");

            switch (cdType)
            {
                case "infinite":
                {
                    bool initReady = data.Get<bool>("isReady", false);
                    var inf = new AbilityInfinite(
                        skillId:   data.skillId,
                        entityKey: entityKey,
                        isReady:   initReady,
                        showOnUI:  showOnUI,
                        icon:      data.icon
                    );
                    skillInfinite[data.skillId] = inf;
                    inf.FireInitialEvent();
                    break;
                }

                case "counter":
                {
                    int maxCounter = data.Get<int>("maxCounter", 1);
                    var ct = new AbilityCounter(
                        skillId:    data.skillId,
                        entityKey:  entityKey,
                        maxCounter: maxCounter,
                        showOnUI:   showOnUI,
                        icon:       data.icon
                    );
                    skillCounter[data.skillId] = ct;
                    ct.FireInitialEvent();
                    break;
                }

                default: // "cooldown" hoặc không có cdType → AbilityCooldown như cũ
                {
                    if (data.cooldown <= 0f) break;

                    int        maxCharge  = data.Get<int>("maxCharge", 1);
                    ChargeMode chargeMode = data.Get<string>("chargeMode", "PerStack") == "AllAtOnce"
                                           ? ChargeMode.AllAtOnce
                                           : ChargeMode.PerStack;

                    var cd = new AbilityCooldown(
                        skillId:    data.skillId,
                        entityKey:  entityKey,
                        baseCd:     data.cooldown,
                        maxCharge:  maxCharge,
                        chargeMode: chargeMode,
                        icon:       data.icon,
                        showOnUI:   showOnUI
                    );
                    skillCd[data.skillId] = cd;
                    cd.FireInitialEvent();
                    break;
                }
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────
    /// <summary>Lấy SkillData theo skillId</summary>
    protected SkillData GetSkillData(string skillId)
    {
        if (skillData.TryGetValue(skillId, out SkillData data))
            return data;
        Debug.LogWarning($"[Skill] Không tìm thấy skillId: '{skillId}'");
        return null;
    }

    /// <summary>Lấy AbilityCooldown theo skillId</summary>
    protected AbilityCooldown GetCd(string skillId)
    {
        if (skillCd.TryGetValue(skillId, out AbilityCooldown cd))
            return cd;
        return null;
    }

    /// <summary>Lấy AbilityCounter theo skillId</summary>
    protected AbilityCounter GetCounter(string skillId)
    {
        if (skillCounter.TryGetValue(skillId, out AbilityCounter ct))
            return ct;
        return null;
    }

    /// <summary>Lấy AbilityInfinite theo skillId</summary>
    protected AbilityInfinite GetInfinite(string skillId)
    {
        if (skillInfinite.TryGetValue(skillId, out AbilityInfinite inf))
            return inf;
        return null;
    }

    /// <summary>
    /// Kiểm tra skill có sẵn sàng không — tự tìm đúng loại.
    /// requiredCounter chỉ dùng cho AbilityCounter.
    /// </summary>
    protected bool IsReady(string skillId, int requiredCounter = 1)
    {
        if (skillCd.TryGetValue(skillId, out var cd))
        {
            //Debug.Log("Cooldown: " + cd.IsReady);
            return cd.IsReady;
        }

        if (skillCounter.TryGetValue(skillId, out var ct))
        {
            //Debug.Log("Counter");
            return ct.IsReadyFor(requiredCounter);
        }

        if (skillInfinite.TryGetValue(skillId, out var inf))
        {
            return inf.IsReady;
        }
        return true;
    }

    /// <summary>
    /// Dùng skill — tự tìm đúng loại.
    /// counterAmount chỉ dùng cho AbilityCounter.
    /// Vẫn hỗ trợ gọi trực tiếp skillCd[id].Use() như cũ.
    /// </summary>
    protected bool Use(string skillId, int counterAmount = 1)
    {
        if (skillCd.TryGetValue(skillId, out var cd))        return cd.Use();
        if (skillCounter.TryGetValue(skillId, out var ct))   return ct.Use(counterAmount);
        if (skillInfinite.TryGetValue(skillId, out var inf)) { inf.Use(); return true; }
        return false;
    }
}