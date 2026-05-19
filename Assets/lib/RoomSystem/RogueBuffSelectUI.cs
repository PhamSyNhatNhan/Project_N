using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI chọn Rogue Buff sau khi clear room — 2 bước:
///   Bước 1: Hiện tối đa 3 group ngẫu nhiên → player chọn group
///   Bước 2: Hiện các minor chưa active của group đó → player chọn 1
/// </summary>
public class RogueBuffSelectUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image      illustrationImage;
    [SerializeField] private Transform  cardContainer;
    [SerializeField] private GameObject buffCardPrefab;
    [SerializeField] private TextMeshProUGUI stepText;

    [Header("Default Illustration")]
    [SerializeField] private Sprite defaultIllustration;

    // ── Runtime ───────────────────────────────────────────────────
    private List<BuffCardUI>  _cards        = new List<BuffCardUI>();
    private BuffGroupId       _selectedGroup;
    private RogueBuffGroup    _selectedPrefab;
    private BuffSelectSource  _source;
    private enum Step { SelectGroup, SelectMinor }
    private Step _currentStep;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        EventManager.Gm.OnRogueBuffSelectOpen.Get().AddListener(HandleOpen);
    }

    private void OnDestroy()
    {
        EventManager.Gm.OnRogueBuffSelectOpen.Get().RemoveListener(HandleOpen);
    }

    private void Start()
    {
        gameObject.SetActive(false);
    }

    // ── Handler ───────────────────────────────────────────────────
    private void HandleOpen(Component sender, object data)
    {
        _source = data is BuffSelectSource s ? s : BuffSelectSource.ExitGate;

        if (illustrationImage != null)
            illustrationImage.sprite = data is Sprite sprite ? sprite : defaultIllustration;

        ShowGroupSelection();
        gameObject.SetActive(true);
    }

    // ── Bước 1: Chọn Group ────────────────────────────────────────
    private void ShowGroupSelection()
    {
        _currentStep = Step.SelectGroup;
        ClearCards();

        if (stepText != null) stepText.text = "Chọn nhóm buff";

        var groupPool = BuildGroupPool();
        if (groupPool.Count == 0)
        {
            Debug.LogWarning("[RogueBuffSelectUI] Không còn group nào.");
            return;
        }

        // Shuffle rồi lấy tối đa 3 group
        for (int i = groupPool.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (groupPool[i], groupPool[j]) = (groupPool[j], groupPool[i]);
        }

        int count = Mathf.Min(3, groupPool.Count);
        for (int i = 0; i < count; i++)
        {
            var (groupId, prefab) = groupPool[i];

            GameObject go   = Instantiate(buffCardPrefab, cardContainer);
            BuffCardUI card = go.GetComponent<BuffCardUI>();
            if (card == null) continue;

            card.SetupGroup(prefab, groupId, OnGroupSelected, this);
            _cards.Add(card);
        }
    }

    // ── Bước 2: Chọn Minor ────────────────────────────────────────
    private void ShowMinorSelection(BuffGroupId groupId, RogueBuffGroup prefab)
    {
        _currentStep    = Step.SelectMinor;
        _selectedGroup  = groupId;
        _selectedPrefab = prefab;
        ClearCards();

        if (stepText != null) stepText.text = "Chọn buff";

        var minors = GetAvailableMinors(groupId, prefab);
        if (minors.Count == 0)
        {
            Debug.LogWarning($"[RogueBuffSelectUI] Group {groupId} không còn minor nào.");
            return;
        }

        // Shuffle rồi lấy tối đa 3
        for (int i = minors.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (minors[i], minors[j]) = (minors[j], minors[i]);
        }

        int count = Mathf.Min(3, minors.Count);
        for (int i = 0; i < count; i++)
        {
            var (minorIndex, buff) = minors[i];

            GameObject go   = Instantiate(buffCardPrefab, cardContainer);
            BuffCardUI card = go.GetComponent<BuffCardUI>();
            if (card == null) continue;

            card.Setup(buff, groupId, minorIndex, OnCardSelected, this);
            _cards.Add(card);
        }
    }

    // ── Callbacks ─────────────────────────────────────────────────
    private void OnGroupSelected(BuffGroupId groupId, RogueBuffGroup prefab)
    {
        ShowMinorSelection(groupId, prefab);
    }

    private void OnCardSelected(BuffGroupId groupId, int minorIndex)
    {
        var buffManager = FindObjectOfType<RogueBuffManager>();
        if (buffManager == null)
        {
            Debug.LogError("[RogueBuffSelectUI] Không tìm thấy RogueBuffManager.");
            return;
        }

        buffManager.ActivateMinor(groupId, minorIndex);

        if (DungeonFlowManager.Instance?.CurrentRun != null)
            DungeonFlowManager.Instance.CurrentRun.isBuffSelected = true;

        Hide();

        if (_source == BuffSelectSource.ExitGate)
            DungeonFlowManager.Instance?.HandleRogueBuffSelected();
    }

    /// <summary>Deselect tất cả card trừ card đang được chọn.</summary>
    public void DeselectOthers(BuffCardUI selected)
    {
        foreach (var card in _cards)
            if (card != null && card != selected)
                card.Deselect();
    }

    // ── Build pools ───────────────────────────────────────────────
    private List<(BuffGroupId groupId, RogueBuffGroup prefab)> BuildGroupPool()
    {
        var pool        = new List<(BuffGroupId, RogueBuffGroup)>();
        var registry    = RogueBuffRegistry.Instance;
        var buffManager = FindObjectOfType<RogueBuffManager>();

        if (registry == null || buffManager == null) return pool;

        foreach (var entry in registry.GetAllEntries())
        {
            BuffGroupId    groupId = entry.Key;
            RogueBuffGroup prefab  = entry.Value;
            if (prefab == null) continue;

            RogueBuffGroup group = buffManager.GetGroup(groupId);

            bool hasInactive = false;
            for (int i = 0; i < prefab.MinorCount; i++)
            {
                if (group != null && group.IsMinorActive(i)) continue;
                hasInactive = true;
                break;
            }

            if (hasInactive) pool.Add((groupId, prefab));
        }

        return pool;
    }

    private List<(int minorIndex, MinorBuff buff)> GetAvailableMinors(BuffGroupId groupId, RogueBuffGroup prefab)
    {
        var buffManager = FindObjectOfType<RogueBuffManager>();
        RogueBuffGroup group = buffManager?.GetGroup(groupId);

        var available = new List<(int, MinorBuff)>();
        for (int i = 0; i < prefab.MinorCount; i++)
        {
            if (group != null && group.IsMinorActive(i)) continue;
            MinorBuff buff = prefab.GetMinor(i);
            if (buff != null) available.Add((i, buff));
        }

        return available;
    }

    // ── Helpers ───────────────────────────────────────────────────
    private void ClearCards()
    {
        foreach (var card in _cards)
            if (card != null) Destroy(card.gameObject);
        _cards.Clear();
    }

    private void Hide()
    {
        ClearCards();
        gameObject.SetActive(false);
    }
}