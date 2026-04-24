using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gắn trên Canvas — quản lý enable/disable UI group theo context
/// và xử lý loading screen khi chuyển scene.
/// </summary>
public class UIContextManager : MonoBehaviour
{
    public static UIContextManager Instance { get; private set; }

    [Header("Combat UI — Pc, Mobile, HealthBar group")]
    [SerializeField] private GameObject[] combatGroup;

    [Header("Loading Screen")]
    [SerializeField] private GameObject loadingScreenUI;

    private AsyncOperation _async;
    private bool           _isLoading = false;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded += OnSceneLoaded;
        EventManager.Gm.OnRoomCleared.Get().AddListener(HandleRoomCleared);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        EventManager.Ui.TriggerLoadingScene.Get().RemoveListener(HandleLoadingScene);
        EventManager.Gm.OnRoomCleared.Get().RemoveListener(HandleRoomCleared);
    }

    private void Start()
    {
        UpdateContext();
    }

    // ── Scene Loaded ──────────────────────────────────────────────
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _isLoading = false;
        UpdateContext();

        // Ở Start scene — nhường việc load cho StartUIManager
        if (scene.name == "Start")
            EventManager.Ui.TriggerLoadingScene.Get().RemoveListener(HandleLoadingScene);
        else
            EventManager.Ui.TriggerLoadingScene.Get().AddListener(HandleLoadingScene);

        // Continue run nếu có flag
        if (scene.name == "Hall" && HallController.ShouldContinue)
        {
            HallController.ShouldContinue = false;
            DungeonFlowManager.Instance?.ContinueRun();
        }
    }

    // ── Context ───────────────────────────────────────────────────
    private void UpdateContext()
    {
        bool isCombat = IsCombatScene();
        SetCombatGroup(isCombat);
    }

    private bool IsCombatScene()
    {
        if (DungeonFlowManager.Instance == null) return false;

        FloorConfig floor = DungeonFlowManager.Instance.CurrentFloor;
        if (floor == null) return false;

        return floor.roomType == RoomType.NormalCombat
            || floor.roomType == RoomType.Boss;
    }

    private void SetCombatGroup(bool active)
    {
        if (combatGroup == null) return;
        foreach (var go in combatGroup)
            if (go != null) go.SetActive(active);
    }

    // ── Room Cleared ──────────────────────────────────────────────
    private void HandleRoomCleared(Component sender, object data)
    {
        SetCombatGroup(false);
    }
    private void HandleLoadingScene(Component sender, object data)
    {
        if (_isLoading) return;

        if (data is not string sceneName)
        {
            Debug.LogError("[UIContextManager] TriggerLoadingScene cần truyền string scene name.");
            return;
        }

        _isLoading = true;
        StopAllCoroutines();
        StartCoroutine(LoadSceneAsync(sceneName));
    }

    private IEnumerator LoadSceneAsync(string sceneName)
    {
        if (loadingScreenUI != null)
            loadingScreenUI.SetActive(true);

        _async = SceneManager.LoadSceneAsync(sceneName);
        _async.allowSceneActivation = false;

        while (!_async.isDone)
        {
            if (_async.progress >= 0.9f)
                _async.allowSceneActivation = true;

            yield return null;
        }

        if (loadingScreenUI != null)
            loadingScreenUI.SetActive(false);
    }
}