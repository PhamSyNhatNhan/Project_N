using System.Collections.Generic;
using UnityEngine;

public class Skill : MonoBehaviour, ILoadable<SkillListData>
{
    protected bool canSkill = true;
    protected bool isSkill  = false;

    // ── Components ────────────────────────────────────────────────
    protected TimeScale timeScale;
    protected Stat      stat;

    // ── Data & CD ─────────────────────────────────────────────────
    // key = skillId
    protected Dictionary<string, SkillData>       skillData = new Dictionary<string, SkillData>();
    protected Dictionary<string, AbilityCooldown> skillCd   = new Dictionary<string, AbilityCooldown>();

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

            if (data.cooldown <= 0f) continue;

            int        maxCharge  = data.Get<int>("maxCharge", 1);
            ChargeMode chargeMode = data.Get<string>("chargeMode", "PerStack") == "AllAtOnce"
                                    ? ChargeMode.AllAtOnce
                                    : ChargeMode.PerStack;

            skillCd[data.skillId] = new AbilityCooldown(
                skillId:    data.skillId,
                entityKey:  stat != null ? $"{stat.NameCharacter}_{stat.GetInstanceID()}" : data.skillId,
                baseCd:     data.cooldown,
                maxCharge:  maxCharge,
                chargeMode: chargeMode,
                icon:       data.icon
            );

            // Fire lần đầu cho skill có charge > 1 để SkillPanel hiển thị ngay
            if (maxCharge > 1)
                skillCd[data.skillId].FireInitialEvent();
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
        Debug.LogWarning($"[Skill] Không tìm thấy CD cho skillId: '{skillId}'");
        return null;
    }

    /// <summary>Kiểm tra skill có sẵn sàng không</summary>
    protected bool IsReady(string skillId)
    {
        var cd = GetCd(skillId);
        return cd == null || cd.IsReady;
    }
}