using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Màn hình Character Info trong Hall.
/// Layout: char list scroll (top) | menu dọc (trái) | portrait (giữa) | stat+content (phải)
/// Mở từ HallUIManager khi nhận OnOpenCharacterInfo.
/// Đóng bằng nút X (gọi Hide()).
/// </summary>
public class CharacterInfoUI : MonoBehaviour
{
    // ── Header ────────────────────────────────────────────────────
    [Header("Close")]
    [SerializeField] private Button closeButton;

    // ── Char list ─────────────────────────────────────────────────
    [Header("Character List")]
    [SerializeField] private Transform       charListContainer;
    [SerializeField] private GameObject      charIconPrefab;    // prefab: Button + Image + Text(name)
    [SerializeField] private ScrollRect      charListScroll;

    // ── Portrait ──────────────────────────────────────────────────
    [Header("Portrait")]
    [SerializeField] private Image           portraitImage;

    // ── Right panel header ────────────────────────────────────────
    [Header("Right Header")]
    [SerializeField] private TextMeshProUGUI charNameText;

    // ── Menu items (3 nút trái) ───────────────────────────────────
    [Header("Menu")]
    [SerializeField] private Button          menuAttributes;
    [SerializeField] private Button          menuEquipment;
    [SerializeField] private Button          menuSkills;
    [SerializeField] private GameObject      menuAttributesHighlight;
    [SerializeField] private GameObject      menuEquipmentHighlight;
    [SerializeField] private GameObject      menuSkillsHighlight;

    // ── Panels ────────────────────────────────────────────────────
    [Header("Panels")]
    [SerializeField] private GameObject              attributesPanel;
    [SerializeField] private GameObject              equipmentPanel;
    [SerializeField] private GameObject              skillsPanel;
    [SerializeField] private CharacterInfoSkillPanel skillPanelController;

    // ── Attributes panel fields ───────────────────────────────────
    [Header("Stat Texts")]
    [SerializeField] private TextMeshProUGUI statHp;
    [SerializeField] private TextMeshProUGUI statAtk;
    [SerializeField] private TextMeshProUGUI statDef;
    [SerializeField] private TextMeshProUGUI statAtkSpd;
    [SerializeField] private TextMeshProUGUI statCritRate;
    [SerializeField] private TextMeshProUGUI statCritDmg;
    [SerializeField] private TextMeshProUGUI statBonusDmg;
    [SerializeField] private TextMeshProUGUI statBonusPhys;
    [SerializeField] private TextMeshProUGUI statBonusMagic;
    [SerializeField] private TextMeshProUGUI descriptionText;

    // ── Runtime ───────────────────────────────────────────────────
    private List<PlayerDisplayData> _characters   = new List<PlayerDisplayData>();
    private List<CharIconUI>         _charIcons    = new List<CharIconUI>();
    private int                     _currentIndex = 0;
    private enum Tab { Attributes, Equipment, Skills }
    private Tab _currentTab = Tab.Attributes;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        closeButton?.onClick.AddListener(Hide);

        menuAttributes?.onClick.AddListener(() => SwitchTab(Tab.Attributes));
        menuEquipment?.onClick.AddListener(()  => SwitchTab(Tab.Equipment));
        menuSkills?.onClick.AddListener(()     => SwitchTab(Tab.Skills));
    }

    private void Start()
    {
        gameObject.SetActive(false);
    }

    // ── Public API ────────────────────────────────────────────────
    public void Show()
    {
        gameObject.SetActive(true);
        LoadCharacters();
        SwitchTab(Tab.Attributes);

        if (_characters.Count > 0)
            SelectCharacter(0);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    // ── Scroll ────────────────────────────────────────────────────
    private System.Collections.IEnumerator ScrollToCenter()
    {
        yield return null; // chờ 1 frame ContentSizeFitter tính xong
        if (charListScroll == null) yield break;

        RectTransform content  = charListScroll.content;
        RectTransform viewport = charListScroll.viewport;
        if (content == null || viewport == null) yield break;

        float contentWidth  = content.rect.width;
        float viewportWidth = viewport.rect.width;
        float offset        = Mathf.Max(0f, (contentWidth - viewportWidth) / 2f);
        content.anchoredPosition = new Vector2(-offset, content.anchoredPosition.y);
    }

    // ── Load char list ────────────────────────────────────────────
    private void LoadCharacters()
    {
        _characters.Clear();

        // Xóa icon cũ
        foreach (Transform child in charListContainer)
            Destroy(child.gameObject);

        var registry = FindObjectOfType<PlayerRegistry>();
        if (registry == null)
        {
            Debug.LogError("[CharacterInfoUI] Không tìm thấy PlayerRegistry.");
            return;
        }

        foreach (var data in registry.GetAllDisplayData())
            _characters.Add(data);

        // Spawn icon cho từng nhân vật
        _charIcons.Clear();
        for (int i = 0; i < _characters.Count; i++)
        {
            GameObject go   = Instantiate(charIconPrefab, charListContainer);
            CharIconUI icon = go.GetComponent<CharIconUI>();
            if (icon == null) continue;

            bool unlocked = UserDataManager.Instance != null &&
                            UserDataManager.Instance.IsUnlocked(_characters[i].talent);

            icon.Setup(i, _characters[i].avatar, unlocked, SelectCharacter);
            _charIcons.Add(icon);
        }

        StartCoroutine(ScrollToCenter());
    }

    // ── Select character ──────────────────────────────────────────
    private void SelectCharacter(int index)
    {
        if (index < 0 || index >= _characters.Count) return;
        _currentIndex = index;

        PlayerDisplayData data = _characters[index];

        // Portrait
        if (portraitImage != null)
        {
            portraitImage.sprite  = data.portrait;
            portraitImage.enabled = data.portrait != null;
        }

        // Right header
        if (charNameText != null) charNameText.text = data.displayName;

        // Reset skill panel cache khi đổi nhân vật
        skillPanelController?.ResetTalent();

        // Refresh panel hiện tại
        RefreshCurrentPanel(data);

        // Highlight icon đang chọn
        RefreshCharIconHighlights();
    }

    private void RefreshCharIconHighlights()
    {
        for (int i = 0; i < _charIcons.Count; i++)
            _charIcons[i]?.SetSelected(i == _currentIndex);
    }

    // ── Tab switching ─────────────────────────────────────────────
    private void SwitchTab(Tab tab)
    {
        _currentTab = tab;

        if (attributesPanel != null) attributesPanel.SetActive(tab == Tab.Attributes);
        if (equipmentPanel  != null) equipmentPanel.SetActive(tab == Tab.Equipment);
        if (skillsPanel     != null) skillsPanel.SetActive(tab == Tab.Skills);

        if (menuAttributesHighlight != null) menuAttributesHighlight.SetActive(tab == Tab.Attributes);
        if (menuEquipmentHighlight  != null) menuEquipmentHighlight.SetActive(tab == Tab.Equipment);
        if (menuSkillsHighlight     != null) menuSkillsHighlight.SetActive(tab == Tab.Skills);

        if (_characters.Count > 0)
            RefreshCurrentPanel(_characters[_currentIndex]);
    }

    private void RefreshCurrentPanel(PlayerDisplayData data)
    {
        switch (_currentTab)
        {
            case Tab.Attributes:
                PopulateAttributes(data);
                break;
            case Tab.Skills:
                skillPanelController?.Populate(data.talent);
                break;
            // Equipment: placeholder, không cần populate
        }
    }

    // ── Populate Attributes ───────────────────────────────────────
    private void PopulateAttributes(PlayerDisplayData data)
    {
        // Đọc stat từ JSON
        StatData statData = LoadStatData(data.talent);
        if (statData == null) return;

        // Tính cur stat đơn giản (base, không có bonus vì đây là Hall)
        if (statHp       != null) statHp.text       = $"{statData.baseHealth:N0}";
        if (statAtk      != null) statAtk.text      = $"{statData.baseAttack:N0}";
        if (statDef      != null) statDef.text      = $"{statData.baseDefense:N0}";
        if (statAtkSpd   != null) statAtkSpd.text   = $"{statData.baseAttackSpeed:N0}%";
        if (statCritRate != null) statCritRate.text  = $"{statData.baseCritRate:N0}%";
        if (statCritDmg  != null) statCritDmg.text  = $"{statData.baseCritDamage:N0}%";

        // Bonus stats — lấy từ extraFields nếu có, mặc định 0
        float bonusDmg   = statData.Get<float>("baseBonusDamage",  0f);
        float bonusPhys  = statData.Get<float>("baseBonusPhysical", 0f);
        float bonusMagic = statData.Get<float>("baseBonusMagic",   0f);

        if (statBonusDmg   != null) statBonusDmg.text   = bonusDmg   > 0 ? $"{bonusDmg:N0}%"   : "0%";
        if (statBonusPhys  != null) statBonusPhys.text  = bonusPhys  > 0 ? $"{bonusPhys:N0}%"  : "0%";
        if (statBonusMagic != null) statBonusMagic.text = bonusMagic > 0 ? $"{bonusMagic:N0}%" : "0%";

        // Description
        string desc = statData.Get<string>("description", "");
        if (descriptionText != null)
            descriptionText.text = !string.IsNullOrEmpty(desc) ? desc : data.description;
    }

    // ── Load JSON helpers ─────────────────────────────────────────
    private StatData LoadStatData(Talent talent)
    {
        string name         = talent.ToString();
        string resourcePath = $"Entity/Player/{name}/Data/{name}";
        TextAsset textAsset  = Resources.Load<TextAsset>(resourcePath);
        if (textAsset == null)
        {
            Debug.LogWarning($"[CharacterInfoUI] Không tìm thấy: Resources/{resourcePath}.json");
            return null;
        }
        try
        {
            EntityData entityData = Newtonsoft.Json.JsonConvert.DeserializeObject<EntityData>(textAsset.text);
            return entityData?.stat;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CharacterInfoUI] Lỗi parse: {ex.Message}");
            return null;
        }
    }
}