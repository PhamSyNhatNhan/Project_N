using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Popup hiển thị chi tiết 1 skill.
/// Chỉ hiện field có giá trị thực — bỏ qua 0, rỗng, và các field kỹ thuật.
/// </summary>
public class SkillDetailPopup : MonoBehaviour
{
    [Header("Header")]
    [SerializeField] private Image           iconImage;
    [SerializeField] private TextMeshProUGUI skillNameText;
    [SerializeField] private TextMeshProUGUI skillTypeText;
    [SerializeField] private Image           typeChipBg;

    [Header("Body")]
    [SerializeField] private TextMeshProUGUI descriptionText;

    [Header("Fields")]
    [SerializeField] private Transform       fieldContainer;
    [SerializeField] private GameObject      fieldRowPrefab;  // 2 TextMeshProUGUI: key + value

    [Header("Close")]
    [SerializeField] private Button          closeButton;
    [SerializeField] private Button          backdropButton;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        closeButton?.onClick.AddListener(Hide);
        backdropButton?.onClick.AddListener(Hide);
    }

    private void Start()
    {
        gameObject.SetActive(false);
    }

    // ── Public API ────────────────────────────────────────────────
    public void Show(SkillData data)
    {
        gameObject.SetActive(true);
        Populate(data);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    // ── Populate ──────────────────────────────────────────────────
    private void Populate(SkillData data)
    {
        // Icon
        if (iconImage != null)
        {
            if (!string.IsNullOrEmpty(data.iconPath))
            {
                var sprite = Resources.Load<Sprite>(data.iconPath);
                iconImage.sprite  = sprite;
                iconImage.enabled = sprite != null;
            }
            else
            {
                iconImage.enabled = false;
            }
        }

        // skillId làm tên hiển thị
        if (skillNameText != null)
            skillNameText.text = data.skillId;

        // skillType là string trong JSON
        string skillTypeStr = data.skillType.ToString(); // SkillType enum → string
        if (skillTypeText != null)
            skillTypeText.text = skillTypeStr;

        Color typeColor = GetTypeColor(skillTypeStr);
        if (typeChipBg    != null) typeChipBg.color    = new Color(typeColor.r, typeColor.g, typeColor.b, 0.15f);
        if (skillTypeText != null) skillTypeText.color = typeColor;

        // description nằm trong extraFields
        string desc = data.Get<string>("description", "");
        if (descriptionText != null)
            descriptionText.text = !string.IsNullOrEmpty(desc) ? desc : "—";

        BuildFields(data);
    }

    // ── Build dynamic fields ──────────────────────────────────────
    private void BuildFields(SkillData data)
    {
        if (fieldContainer == null || fieldRowPrefab == null) return;

        foreach (Transform child in fieldContainer)
            Destroy(child.gameObject);

        var fields = new List<(string key, string val, Color col)>();

        // Cooldown
        if (data.cooldown > 0f)
            fields.Add(("Cooldown", $"{data.cooldown}s", Color.white));

        // Damage
        var dmgList = data.Get<List<float>>("damage", null);
        if (dmgList != null)
        {
            var filtered = dmgList.FindAll(d => d > 0f);
            if (filtered.Count > 0)
            {
                string dmgStr = filtered.Count > 1
                    ? $"{filtered[0]:0} – {filtered[filtered.Count - 1]:0}"
                    : $"{filtered[0]:0}";
                string dmgTypeStr = data.Get<string>("damageType", "");
                Color  dmgColor   = GetDamageTypeColor(dmgTypeStr);
                fields.Add(("Damage", dmgStr, dmgColor));
            }
        }

        // Damage Type
        string damageType = data.Get<string>("damageType", "");
        if (!string.IsNullOrEmpty(damageType))
            fields.Add(("Damage Type", damageType, GetDamageTypeColor(damageType)));

        // Charges
        int maxCharge = data.Get<int>("maxCharge", 1);
        if (maxCharge > 1)
        {
            fields.Add(("Charges", maxCharge.ToString(), Color.white));
            string chargeMode = data.Get<string>("chargeMode", "PerStack");
            fields.Add(("Charge Mode", chargeMode, Color.white));
        }

        // Counter
        int maxCounter = data.Get<int>("maxCounter", 0);
        if (maxCounter > 0)
            fields.Add(("Max Counter", maxCounter.ToString(), Color.white));

        foreach (var (key, val, col) in fields)
            SpawnFieldRow(key, val, col);
    }

    private void SpawnFieldRow(string key, string val, Color valColor)
    {
        GameObject go  = Instantiate(fieldRowPrefab, fieldContainer);
        FieldRowUI row = go.GetComponent<FieldRowUI>();
        row?.Setup(key, val, valColor);
    }

    // ── Color helpers ─────────────────────────────────────────────
    private Color GetTypeColor(string skillType) => skillType switch
    {
        "Attack"  => new Color(0.88f, 0.48f, 0.37f),
        "Skill"   => new Color(0.49f, 0.72f, 0.83f),
        "Ulti"    => new Color(0.88f, 0.25f, 0.98f),
        "Dash"    => new Color(0.65f, 0.55f, 0.98f),
        "Passive" => new Color(0.60f, 0.63f, 0.72f),
        "Burst"   => new Color(0.96f, 0.77f, 0.19f),
        _         => Color.white
    };

    private Color GetDamageTypeColor(string damageType) => damageType switch
    {
        "Physical" => new Color(0.88f, 0.48f, 0.37f),
        "Magic"    => new Color(0.66f, 0.33f, 0.97f),
        "True"     => Color.white,
        _          => Color.white
    };
}