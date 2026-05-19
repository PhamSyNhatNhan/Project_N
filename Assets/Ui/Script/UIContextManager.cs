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

    [Header("Hall UI — HallUIManager và các thành phần Hall")]
    [SerializeField] private GameObject[] hallGroup;

    [Header("Overlay UI — DeathScreen, EndRun (hiện trong dungeon, ẩn ở Hall/Start)")]
    [SerializeField] private GameObject[] overlayGroup;

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
        EventManager.Gm.OnStartCombat.Get().AddListener(HandleStartCombat);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        EventManager.Ui.TriggerLoadingScene.Get().RemoveListener(HandleLoadingScene);
        EventManager.Gm.OnRoomCleared.Get().RemoveListener(HandleRoomCleared);
        EventManager.Gm.OnStartCombat.Get().RemoveListener(HandleStartCombat);
    }

    private void Start()
    {
        SetCombatGroup(false);
    }

    // ── Scene Loaded ──────────────────────────────────────────────
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _isLoading = false;

        SetCombatGroup(false);

        bool isHall     = scene.name == "Hall";
        bool isDungeon  = scene.name != "Hall" && scene.name != "Start";
        SetHallGroup(isHall);
        SetOverlayGroup(isDungeon);

        if (scene.name == "Start")
            EventManager.Ui.TriggerLoadingScene.Get().RemoveListener(HandleLoadingScene);
        else
            EventManager.Ui.TriggerLoadingScene.Get().AddListener(HandleLoadingScene);

        if (isHall && HallController.ShouldContinue)
        {
            HallController.ShouldContinue = false;
            DungeonFlowManager.Instance?.ContinueRun();
        }
    }

    private void HandleStartCombat(Component sender, object data)
    {
        SetCombatGroup(true);
    }

    // ── Room Cleared ──────────────────────────────────────────────
    private void HandleRoomCleared(Component sender, object data)
    {
        SetCombatGroup(false);
    }

    // ── Loading Scene ─────────────────────────────────────────────
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

    // ── Helpers ───────────────────────────────────────────────────
    private void SetCombatGroup(bool active)
    {
        if (combatGroup == null) return;
        foreach (var go in combatGroup)
            if (go != null) go.SetActive(active);
    }

    private void SetHallGroup(bool active)
    {
        if (hallGroup == null) return;
        foreach (var go in hallGroup)
            if (go != null) go.SetActive(active);
    }

    private void SetOverlayGroup(bool active)
    {
        if (overlayGroup == null) return;
        if (active) return; 
        foreach (var go in overlayGroup)
            if (go != null) go.SetActive(false);
    }
}