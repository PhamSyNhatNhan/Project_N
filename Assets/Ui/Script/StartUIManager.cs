using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gắn trên Canvas ở Start scene — không DontDestroyOnLoad.
/// Chỉ xử lý loading screen, bị destroy khi load scene mới.
/// </summary>
public class StartUIManager : MonoBehaviour
{
    [Header("Loading Screen")]
    [SerializeField] private GameObject loadingScreenUI;

    private bool _isLoading = false;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        EventManager.Ui.TriggerLoadingScene.Get().AddListener(HandleLoadingScene);
    }

    private void OnDestroy()
    {
        EventManager.Ui.TriggerLoadingScene.Get().RemoveListener(HandleLoadingScene);
    }

    // ── Loading ───────────────────────────────────────────────────
    private void HandleLoadingScene(Component sender, object data)
    {
        Debug.Log($"[{GetType().Name}] HandleLoadingScene called, scene: {data}, isLoading: {_isLoading}, sender: {sender?.name}");
        if (_isLoading) return;

        if (data is not string sceneName)
        {
            Debug.LogError("[StartUIManager] TriggerLoadingScene cần truyền string scene name.");
            return;
        }

        _isLoading = true;
        StartCoroutine(LoadSceneAsync(sceneName));
    }

    private IEnumerator LoadSceneAsync(string sceneName)
    {
        if (loadingScreenUI != null)
            loadingScreenUI.SetActive(true);

        var async = SceneManager.LoadSceneAsync(sceneName);
        async.allowSceneActivation = false;

        while (!async.isDone)
        {
            if (async.progress >= 0.9f)
                async.allowSceneActivation = true;

            yield return null;
        }
    }
}