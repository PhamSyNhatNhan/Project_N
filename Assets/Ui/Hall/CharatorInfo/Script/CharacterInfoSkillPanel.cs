using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Tab Skills trong CharacterInfoUI.
/// Build skill grid từ SkillListData của nhân vật đang chọn.
/// Tap card → highlight + mở SkillDetailPopup.
/// </summary>
public class CharacterInfoSkillPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform        cardContainer;
    [SerializeField] private GameObject       skillCardPrefab;
    [SerializeField] private SkillDetailPopup popup;

    // ── Runtime ───────────────────────────────────────────────────
    private readonly List<SkillCardUI> _cards = new List<SkillCardUI>();
    private Talent? _currentTalent;

    // ── Public API (reset khi đổi nhân vật) ─────────────────────
    public void ResetTalent()
    {
        _currentTalent = null;
    }

    // ── Lifecycle ─────────────────────────────────────────────────
    private void OnDisable()
    {
        popup?.Hide();
        foreach (var c in _cards) c.SetSelected(false);
    }

    // ── Public API ────────────────────────────────────────────────
    public void Populate(Talent talent)
    {
        // Chỉ rebuild khi đổi nhân vật
        if (_currentTalent == talent) return;
        _currentTalent = talent;

        ClearCards();
        popup?.Hide();

        SkillListData skillListData = LoadSkillData(talent);
        if (skillListData?.skills == null || skillListData.skills.Count == 0) return;

        foreach (var skillData in skillListData.skills)
        {
            if (string.IsNullOrEmpty(skillData.skillId)) continue;

            GameObject  go   = Instantiate(skillCardPrefab, cardContainer);
            SkillCardUI card = go.GetComponent<SkillCardUI>();
            if (card == null) continue;

            card.Setup(skillData, OnCardSelected);
            _cards.Add(card);
        }

        // Highlight card đầu tiên nhưng KHÔNG hiện popup
        if (_cards.Count > 0)
            _cards[0].SetSelected(true);
    }

    // ── Card selected callback ────────────────────────────────────
    private void OnCardSelected(SkillCardUI selectedCard, SkillData data)
    {
        foreach (var c in _cards) c.SetSelected(false);
        selectedCard.SetSelected(true);
        popup?.Show(data);
    }

    // ── Helpers ───────────────────────────────────────────────────
    private void ClearCards()
    {
        foreach (var card in _cards)
            if (card != null) Destroy(card.gameObject);
        _cards.Clear();
    }

    // ── Load skill JSON ───────────────────────────────────────────
    private SkillListData LoadSkillData(Talent talent)
    {
        string    name         = talent.ToString();
        string    resourcePath = $"Entity/Player/{name}/Data/{name}";
        TextAsset textAsset    = Resources.Load<TextAsset>(resourcePath);

        if (textAsset == null)
        {
            Debug.LogWarning($"[CharacterInfoSkillPanel] Không tìm thấy: Resources/{resourcePath}.json");
            return null;
        }

        try
        {
            EntityData entityData = JsonConvert.DeserializeObject<EntityData>(textAsset.text);
            return entityData?.skill;  // EntityData.skill — đúng field name
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CharacterInfoSkillPanel] Lỗi parse '{resourcePath}': {ex.Message}");
            return null;
        }
    }
}