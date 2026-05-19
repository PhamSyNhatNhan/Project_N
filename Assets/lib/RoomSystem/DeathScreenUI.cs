using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Màn hình chết — hiện khi player chết.
/// Hiện floor đạt được, nút Retry và Summary.
/// </summary>
public class DeathScreenUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI floorsText;
    [SerializeField] private Button          retryButton;
    [SerializeField] private Button          summaryButton;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        EventManager.Gm.OnPlayerDead.Get().AddListener(HandlePlayerDead);
        retryButton?.onClick.AddListener(OnRetry);
        summaryButton?.onClick.AddListener(OnSummary);
    }

    private void OnDestroy()
    {
        EventManager.Gm.OnPlayerDead.Get().RemoveListener(HandlePlayerDead);
        retryButton?.onClick.RemoveListener(OnRetry);
        summaryButton?.onClick.RemoveListener(OnSummary);
    }

    private void Start()
    {
        gameObject.SetActive(false);
    }


    // ── Handler ───────────────────────────────────────────────────
    private void HandlePlayerDead(Component sender, object data)
    {
        gameObject.SetActive(true);
        var run = DungeonFlowManager.Instance?.CurrentRun;
        if (floorsText != null)
            floorsText.text = (run?.currentFloor ?? 0).ToString();
        StartCoroutine(PauseNextFrame());
    }

    private System.Collections.IEnumerator PauseNextFrame()
    {
        yield return null;
        Time.timeScale = 0f;
    }


    // ── Retry ─────────────────────────────────────────────────────
    private void OnRetry()
    {
        if (DungeonFlowManager.Instance == null) return;

        var run = DungeonFlowManager.Instance.CurrentRun;
        if (run == null) return;

        run.isRoomCleared  = false;
        run.isBuffSelected = false;

        FloorConfig config = DungeonFlowManager.Instance.CurrentFloor;
        if (config == null || string.IsNullOrEmpty(config.sceneName)) return;

        Hide();
        EventManager.Ui.TriggerLoadingScene.Get().Invoke(this, config.sceneName);
    }

    // ── Summary ───────────────────────────────────────────────────
    private void OnSummary()
    {
        var run = DungeonFlowManager.Instance?.CurrentRun;
        if (run == null) return;

        Hide();
        DungeonFlowManager.Instance.HandleFinalFloorCleared();
    }

    // ── Helpers ───────────────────────────────────────────────────
    private void Hide()
    {
        Time.timeScale = 1f;
        gameObject.SetActive(false);
    }
}