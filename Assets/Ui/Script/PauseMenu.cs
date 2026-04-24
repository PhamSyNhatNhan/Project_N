using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gắn trên PauseMenu Panel trong Canvas.
/// Subscribe OnPause để hiện, OnResume để ẩn.
/// 3 button: Resume, Restart Floor, Quit to Start.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button restartFloorButton;
    [SerializeField] private Button quitToStartButton;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        EventManager.Gm.OnPause.Get().AddListener(HandlePause);
        EventManager.Gm.OnResume.Get().AddListener(HandleResume);

        resumeButton?.onClick.AddListener(OnResumeClicked);
        restartFloorButton?.onClick.AddListener(OnRestartFloorClicked);
        quitToStartButton?.onClick.AddListener(OnQuitToStartClicked);
    }

    private void OnDestroy()
    {
        EventManager.Gm.OnPause.Get().RemoveListener(HandlePause);
        EventManager.Gm.OnResume.Get().RemoveListener(HandleResume);

        resumeButton?.onClick.RemoveListener(OnResumeClicked);
        restartFloorButton?.onClick.RemoveListener(OnRestartFloorClicked);
        quitToStartButton?.onClick.RemoveListener(OnQuitToStartClicked);
    }

    private void Start()
    {
        gameObject.SetActive(false);
    }

    // ── Handlers ──────────────────────────────────────────────────
    private void HandlePause(Component sender, object data)
    {
        gameObject.SetActive(true);
        Time.timeScale = 0f;

        bool isCombat = UnityEngine.SceneManagement.SceneManager
            .GetActiveScene().name != "Hall";

        if (restartFloorButton != null)
        {
            restartFloorButton.interactable = isCombat;
            var colors = restartFloorButton.colors;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.3f);
            restartFloorButton.colors = colors;
        }
    }

    private void HandleResume(Component sender, object data)
    {
        gameObject.SetActive(false);
        Time.timeScale = 1f;
    }

    // ── Buttons ───────────────────────────────────────────────────
    private void OnResumeClicked()
    {
        EventManager.Gm.OnResume.Get().Invoke(this, null);
    }

    private void OnRestartFloorClicked()
    {
        Time.timeScale = 1f;
        gameObject.SetActive(false);

        if (DungeonFlowManager.Instance == null)
        {
            Debug.LogError("[PauseMenu] DungeonFlowManager chưa tồn tại.");
            return;
        }

        FloorConfig config = DungeonFlowManager.Instance.CurrentFloor;
        if (config == null || string.IsNullOrEmpty(config.sceneName))
        {
            Debug.LogError("[PauseMenu] Không tìm thấy sceneName cho floor hiện tại.");
            return;
        }

        // Reset flag để RoomSpawner spawn wave bình thường
        var run = DungeonFlowManager.Instance.CurrentRun;
        if (run != null)
        {
            run.isRoomCleared  = false;
            run.isBuffSelected = false;
        }

        EventManager.Ui.TriggerLoadingScene.Get().Invoke(this, config.sceneName);
    }

    private void OnQuitToStartClicked()
    {
        Time.timeScale = 1f;
        gameObject.SetActive(false);
        EventManager.Ui.TriggerLoadingScene.Get().Invoke(this, "Start");
    }
}