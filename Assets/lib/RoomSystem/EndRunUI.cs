using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Màn hình kết thúc run — hiện sau khi clear dungeon.
/// Layout:
///   - Character portrait + tên (trên ảnh)
///   - Dungeon preview + tên (trên ảnh)
///   - Floors cleared + Time
///   - Shard breakdown + animation đếm
///   - Nút về Hall
/// </summary>
public class EndRunUI : MonoBehaviour
{
    private const int ShardPerNormal = 100;
    private const int ShardPerShop   = 30;
    private const int ShardPerBoss   = 200;

    [Header("Character & Map")]
    [SerializeField] private Image           characterPortrait;
    [SerializeField] private TextMeshProUGUI characterNameText;
    [SerializeField] private Image           dungeonPreview;
    [SerializeField] private TextMeshProUGUI dungeonNameText;

    [Header("Stats")]
    [SerializeField] private TextMeshProUGUI floorsText;
    [SerializeField] private TextMeshProUGUI timeText;

    [Header("Shard Breakdown")]
    [SerializeField] private TextMeshProUGUI normalRoomText;
    [SerializeField] private TextMeshProUGUI shopRoomText;
    [SerializeField] private TextMeshProUGUI bossRoomText;
    [SerializeField] private TextMeshProUGUI timeBonusText;
    [SerializeField] private TextMeshProUGUI totalShardText;

    [Header("Animation")]
    [SerializeField] private float countDuration = 1.5f;

    [Header("Button")]
    [SerializeField] private Button backButton;


    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        EventManager.Gm.OnRunCompleted.Get().AddListener(HandleRunCompleted);
        backButton?.onClick.AddListener(OnBack);
    }

    private void OnDestroy()
    {
        EventManager.Gm.OnRunCompleted.Get().RemoveListener(HandleRunCompleted);
        backButton?.onClick.RemoveListener(OnBack);
    }

    private void Start()
    {
        gameObject.SetActive(false);
    }

    // ── Handler ───────────────────────────────────────────────────
    private void HandleRunCompleted(Component sender, object data)
    {
        if (data is not RunSaveData run) return;
        gameObject.SetActive(true);
        Show(run);
        StartCoroutine(PauseNextFrame());
    }

    private System.Collections.IEnumerator PauseNextFrame()
    {
        yield return null;
        Time.timeScale = 0f;
    }

    // ── Show ──────────────────────────────────────────────────────
    private void Show(RunSaveData run)
    {
        var dungeonConfig = DungeonFlowManager.Instance?.DungeonConfig;

        // ── Character ─────────────────────────────────────────────
        if (characterNameText != null)
            characterNameText.text = run.talent.ToString();

        var playerRegistry = FindObjectOfType<PlayerRegistry>();
        if (playerRegistry != null)
        {
            var displayData = playerRegistry.GetDisplayData(run.talent);
            if (characterPortrait != null && displayData?.portrait != null)
            {
                characterPortrait.sprite  = displayData.portrait;
                characterPortrait.enabled = true;
            }
        }

        // ── Dungeon ───────────────────────────────────────────────
        if (dungeonNameText != null)
            dungeonNameText.text = dungeonConfig?.displayName ?? run.dungeonGroupId;

        var mapRegistry = FindObjectOfType<MapRegistry>();
        if (mapRegistry != null)
        {
            var mapData = mapRegistry.GetDisplayData(run.dungeonGroupId);
            if (dungeonPreview != null && mapData?.preview != null)
            {
                dungeonPreview.sprite  = mapData.preview;
                dungeonPreview.enabled = true;
            }
        }

        // ── Stats ─────────────────────────────────────────────────
        int totalFloors = dungeonConfig?.floors?.Length ?? run.currentFloor;
        if (floorsText != null) floorsText.text = totalFloors.ToString();
        if (timeText   != null) timeText.text   = FormatTime(run.runTime);

        // ── Shard ─────────────────────────────────────────────────
        int normalShard = run.normalRoomsCleared * ShardPerNormal;
        int shopShard   = run.shopRoomsCleared   * ShardPerShop;
        int bossShard   = run.bossRoomsCleared   * ShardPerBoss;
        int timeBonus   = CalcTimeBonus(run.runTime, run.bossRoomsCleared);
        int total       = normalShard + shopShard + bossShard + timeBonus;

        if (normalRoomText != null)
            normalRoomText.text = $"{run.normalRoomsCleared} × {ShardPerNormal} = {normalShard}";
        if (shopRoomText != null)
            shopRoomText.text   = $"{run.shopRoomsCleared} × {ShardPerShop} = {shopShard}";
        if (bossRoomText != null)
            bossRoomText.text   = $"{run.bossRoomsCleared} × {ShardPerBoss} = {bossShard}";
        if (timeBonusText != null)
            timeBonusText.text  = timeBonus.ToString();

        UserDataManager.Instance?.AddShards(total);
        StartCoroutine(AnimateCount(total));
    }

    // ── Shard Calc ────────────────────────────────────────────────
    private int CalcTimeBonus(float runTime, int bossCount)
    {
        if (bossCount <= 0) return 0;

        // Max bonus theo số boss
        int maxBonus = bossCount * 150 + (bossCount >= 3 ? 50 : 0);

        // Scale factor theo số boss — 1: khắt khe, 2: vừa, 3: thoải mái
        float scaleFactor = bossCount switch
        {
            1 => 120f,
            2 => 360f,
            _ => 720f
        };

        return Mathf.RoundToInt(maxBonus * Mathf.Exp(-runTime / scaleFactor));
    }

    // ── Animate ───────────────────────────────────────────────────
    private IEnumerator AnimateCount(int target)
    {
        if (totalShardText == null) yield break;

        float elapsed = 0f;
        while (elapsed < countDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            int cur  = Mathf.RoundToInt(Mathf.Lerp(0, target, elapsed / countDuration));
            totalShardText.text = cur.ToString("N0");
            yield return null;
        }
        totalShardText.text = target.ToString("N0");
    }

    // ── Format Time ───────────────────────────────────────────────
    private string FormatTime(float seconds)
    {
        int m = Mathf.FloorToInt(seconds / 60f);
        int s = Mathf.FloorToInt(seconds % 60f);
        return $"{m:00}:{s:00}";
    }

    // ── Back ──────────────────────────────────────────────────────
    private void OnBack()
    {
        Time.timeScale = 1f;
        gameObject.SetActive(false);
        EventManager.Ui.TriggerLoadingScene.Get().Invoke(this, "Hall");
    }
}