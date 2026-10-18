#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;

// Prefab-based transition screen. The prefab comes from a source delegate (a built-in game screen loads it from Resources, a mod builds or
// loads its own), is instantiated under the runtime when the screen shows and destroyed on dispose. Constraints (so it behaves like the UI
// Toolkit screen): no Camera or AudioListener, its Canvas sorts above the combat HUD and takes no input, and it is marked as a room resource so
// it is never treated as world content. Fade uses a CanvasGroup added by the screen when the prefab has none.
public sealed class PNPrefabTransitionScreen : IPNTransitionScreen
{
    // Above the combat HUD document (sorting 100); the same order as the UI Toolkit screen.
    public const int SortingOrder = 150;
    private enum Fade { In, Shown, Out, Done }
    private readonly PNTransitionPayload payload;
    private readonly Func<GameObject> source;
    private GameObject instance;
    private CanvasGroup group;
    private Fade fade = Fade.In;
    private float alpha;
    private bool disposed;

    public PNPrefabTransitionScreen(PNTransitionPayload payload, Func<GameObject> source)
    {
        this.payload = payload ?? throw new ArgumentNullException(nameof(payload));
        this.source = source ?? throw new ArgumentNullException(nameof(source));
    }
    // Resources path (without extension) from the registered payload, for built-in prefab screens.
    public static Func<GameObject> FromResources(string path) => () => Resources.Load<GameObject>(path) ?? throw new InvalidOperationException("Transition prefab not found in Resources: " + path);

    public bool ReadyToHide => fade == Fade.Shown || fade == Fade.Out || fade == Fade.Done;
    public bool Finished => fade == Fade.Done;
    public float Alpha => alpha;
    public GameObject Instance => instance;

    public void Show(PNTransitionContext context)
    {
        if (disposed) throw new ObjectDisposedException(nameof(PNPrefabTransitionScreen));
        var prefab = source();
        if (prefab == null) throw new InvalidOperationException("Transition prefab source returned nothing.");
        if (prefab.GetComponentInChildren<Camera>(true) != null || prefab.GetComponentInChildren<AudioListener>(true) != null)
            throw new InvalidOperationException("A transition prefab must not contain a Camera or an AudioListener.");
        instance = UnityEngine.Object.Instantiate(prefab);
        instance.name = "PN Transition Screen (prefab)";
        if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(instance);
        instance.AddComponent<PNRoomResource>();
        foreach (var canvas in instance.GetComponentsInChildren<Canvas>(true))
        {
            if (canvas.rootCanvas == canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true; canvas.sortingOrder = SortingOrder;
        }
        // The screen never takes input from the HUD beneath it.
        foreach (var raycaster in instance.GetComponentsInChildren<UnityEngine.UI.GraphicRaycaster>(true)) raycaster.enabled = false;
        foreach (var graphic in instance.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) graphic.raycastTarget = false;
        group = instance.GetComponent<CanvasGroup>() ?? instance.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;
        instance.SetActive(true);
        alpha = payload.FadeIn <= 0 ? 1 : 0; fade = payload.FadeIn <= 0 ? Fade.Shown : Fade.In; Apply();
    }

    public void Tick(PNTransitionState state, float unscaledDeltaTime)
    {
        if (disposed || instance == null) return;
        if (fade == Fade.In) { alpha = payload.FadeIn <= 0 ? 1 : Mathf.Min(1, alpha + unscaledDeltaTime / payload.FadeIn); if (alpha >= 1) fade = Fade.Shown; }
        else if (fade == Fade.Out) { alpha = payload.FadeOut <= 0 ? 0 : Mathf.Max(0, alpha - unscaledDeltaTime / payload.FadeOut); if (alpha <= 0) fade = Fade.Done; }
        Apply();
    }

    public void Hide(string reason)
    {
        if (fade == Fade.Out || fade == Fade.Done) return;
        fade = payload.FadeOut <= 0 ? Fade.Done : Fade.Out; if (fade == Fade.Done) alpha = 0; Apply();
    }

    private void Apply() { if (group != null) group.alpha = alpha; }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; fade = Fade.Done;
        if (instance != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(instance); else UnityEngine.Object.DestroyImmediate(instance); }
        instance = null; group = null;
    }
}
#endif
