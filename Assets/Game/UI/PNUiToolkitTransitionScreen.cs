#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.UIElements;

// Full-screen transition screen built in code with UI Toolkit. Only the built-in Default payload
// uses the game's stellar presentation; custom payloads retain their own colour/text and layout.
// It draws only: picking is ignored so it never takes input from anything beneath it.
public sealed class PNUiToolkitTransitionScreen : IPNTransitionScreen
{
    // Above the combat HUD document (sorting 100).
    public const float SortingOrder = 150f;
    private enum Fade { In, Shown, Out, Done }
    private readonly PNTransitionPayload payload;
    private GameObject host;
    private PanelSettings settings;
    private VisualElement root;
    private Label label;
    private Fade fade = Fade.In;
    private float alpha;
    private bool disposed;
    private PNMenuBackdrop backdrop;
    private PNSparkleArtwork sparkles;
    private PNTitleLoadingVisual artwork;

    public PNUiToolkitTransitionScreen(PNTransitionPayload payload) { this.payload = payload ?? throw new ArgumentNullException(nameof(payload)); }

    public bool ReadyToHide => fade == Fade.Shown || fade == Fade.Out || fade == Fade.Done;
    public bool Finished => fade == Fade.Done;
    public float Alpha => alpha;
    public VisualElement Root => root;
    public string Text => label?.text;

    public void Show(PNTransitionContext context)
    {
        if (disposed) throw new ObjectDisposedException(nameof(PNUiToolkitTransitionScreen));
        host = new GameObject("PN Transition Screen (UI Toolkit)");
        if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(host);
        settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.name = "Transition panel settings";
        settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/Combat/RuntimeTheme");
        settings.scaleMode = PanelScaleMode.ScaleWithScreenSize; settings.referenceResolution = new Vector2Int(1280, 720); settings.match = .5f;
        settings.sortingOrder = SortingOrder;
        var document = host.AddComponent<UIDocument>(); document.panelSettings = settings;
        root = document.rootVisualElement;
        root.style.position = Position.Absolute; root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;
        root.style.backgroundColor = new Color(payload.R, payload.G, payload.B, payload.A);
        root.style.alignItems = Align.Center; root.style.justifyContent = Justify.Center;
        root.pickingMode = PickingMode.Ignore;
        label = new Label(payload.Text) { pickingMode = PickingMode.Ignore };
        label.style.color = Color.white; label.style.fontSize = 36; label.style.unityTextAlign = TextAnchor.MiddleCenter;
        root.Add(label);
        // Identity, not a colour/text comparison: an independently registered mod payload
        // remains a custom screen even if its values happen to equal the default's values.
        if (ReferenceEquals(payload, PNTransitionPayload.Default))
        {
            var shared = Resources.Load<StyleSheet>("UI/Combat/CombatUI"); if (shared != null) root.styleSheets.Add(shared);
            var local = Resources.Load<StyleSheet>("UI/Combat/TitleLoading"); if (local != null) root.styleSheets.Add(local);
            sparkles = new PNSparkleArtwork();
            backdrop = new PNMenuBackdrop { SurfaceAlpha = 1, DecorationAlpha = 1 };
            backdrop.Root.pickingMode = PickingMode.Ignore;
            backdrop.Root.style.backgroundColor = new Color(184f/255,192f/255,196f/255,1);
            root.Add(backdrop.Root);
            artwork = new PNTitleLoadingVisual(false); backdrop.Root.Add(artwork);
            label.RemoveFromHierarchy(); PNLocalizationUI.Bind(label,"project_n:ui.loading");
            label.style.fontSize = 21; label.AddToClassList("pn-default-loading-caption"); PNTitleLoadingFonts.Apply(label);
            backdrop.Root.Add(label);
        }
        alpha = payload.FadeIn <= 0 ? 1 : 0;
        fade = payload.FadeIn <= 0 ? Fade.Shown : Fade.In;
        Apply();
    }

    public void Tick(PNTransitionState state, float unscaledDeltaTime)
    {
        if (disposed || root == null) return;
        backdrop?.Tick(unscaledDeltaTime); artwork?.Tick(unscaledDeltaTime);
        if (fade == Fade.In) { alpha = payload.FadeIn <= 0 ? 1 : Mathf.Min(1, alpha + unscaledDeltaTime / payload.FadeIn); if (alpha >= 1) fade = Fade.Shown; }
        else if (fade == Fade.Out) { alpha = payload.FadeOut <= 0 ? 0 : Mathf.Max(0, alpha - unscaledDeltaTime / payload.FadeOut); if (alpha <= 0) fade = Fade.Done; }
        Apply();
    }

    public void Hide(string reason)
    {
        if (fade == Fade.Out || fade == Fade.Done) return;
        fade = payload.FadeOut <= 0 ? Fade.Done : Fade.Out;
        if (fade == Fade.Done) alpha = 0;
        Apply();
    }

    private void Apply() { if (root != null) root.style.opacity = alpha; }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; fade = Fade.Done;
        artwork?.Dispose(); artwork = null;
        backdrop?.Dispose(); backdrop = null; sparkles?.Dispose(); sparkles = null;
        if (host != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(host); else UnityEngine.Object.DestroyImmediate(host); }
        if (settings != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(settings); else UnityEngine.Object.DestroyImmediate(settings); }
        host = null; settings = null; root = null; label = null;
    }
}
#endif
