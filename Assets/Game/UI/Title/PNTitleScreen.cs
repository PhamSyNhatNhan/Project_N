#if UNITY_5_3_OR_NEWER
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// The Title scene's UI (Docs/LoadingHandoffPlan.md P6): the game's first scene, no play session, only UI. "Bắt đầu" starts the boot (PNGameFlow.StartBoot:
// the first loading screen, the content, then the Hall); "Thoát" closes the application. With --pn-auto-start=<target>, or when another scene was sent
// through the Title (PNGameBootstrap.PendingTarget), it presses Start by itself. A boot failure comes back here as one line and "Thử lại". The scene is
// authored empty (a camera); this component is attached when the scene loads. Temporary look: the menu backdrop, pause-style buttons, no cards.
internal sealed class PNTitleScreen : MonoBehaviour
{
    private PanelSettings settings;
    private PNMenuBackdrop backdrop;
    private PNSparkleArtwork sparkles;
    private VisualElement column;
    private Button start, quit;
    private Label error;
    private bool started;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded;
    }
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.path != PNScenes.Title || FindAnyObjectByType<PNTitleScreen>() != null) return;
        new GameObject("Project N Title").AddComponent<PNTitleScreen>();
    }

    private void Awake()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var events = new GameObject("PN Title Event System"); events.transform.SetParent(transform, false);
            events.AddComponent<EventSystem>(); events.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        settings = ScriptableObject.CreateInstance<PanelSettings>(); settings.name = "Title panel settings";
        settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/Combat/RuntimeTheme");
        settings.scaleMode = PanelScaleMode.ScaleWithScreenSize; settings.referenceResolution = new Vector2Int(1280, 720); settings.match = .5f;
        settings.sortingOrder = 100;
        var document = gameObject.AddComponent<UIDocument>(); document.panelSettings = settings;
        var root = document.rootVisualElement;
        var styles = Resources.Load<StyleSheet>("UI/Combat/CombatUI"); // the pause-button, scroll and menu rules the game UI shares
        if (styles != null) root.styleSheets.Add(styles);
        root.style.position = Position.Absolute; root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;
        sparkles = new PNSparkleArtwork(); // the blurred star textures the backdrop (and every sparkle glyph) draws with need an owning lease
        backdrop = new PNMenuBackdrop { SurfaceAlpha = 1f, DecorationAlpha = 1f }; backdrop.SetOrbits(true);
        root.Add(backdrop.Root);
        column = PNMenuButtons.Column("pn-title-column");
        start = PNMenuButtons.Choice("pn-title-start", "Bắt đầu", Begin, true); column.Add(start);
        quit = PNMenuButtons.Choice("pn-title-quit", "Thoát", PNApplication.Quit); column.Add(quit);
        error = new Label { name = "pn-title-error", pickingMode = PickingMode.Ignore };
        error.style.color = new Color(.55f, .27f, .27f); error.style.fontSize = 15; error.style.whiteSpace = WhiteSpace.Normal;
        error.style.unityTextAlign = TextAnchor.MiddleCenter; error.style.marginTop = 14; error.style.display = DisplayStyle.None;
        column.Add(error);
        backdrop.Root.Add(column);
        start.schedule.Execute(() => start.Focus());
    }

    private void Begin()
    {
        if (started) return;
        started = true; error.style.display = DisplayStyle.None;
        // The buttons stay out of reach while the loading screen is up: it only draws, it does not take input.
        column.SetEnabled(false);
        PNGameFlow.StartBoot(PNGameBootstrap.PendingTarget ?? PNGameBootstrap.AutoStart ?? PNBootTarget.Hall);
    }

    private void Update()
    {
        backdrop?.Tick(Time.unscaledDeltaTime);
        // A target waiting from the command line or from a scene sent through here: no click needed.
        if (!started && PNGameFlow.BootFailure == null && (PNGameBootstrap.PendingTarget != null || PNGameBootstrap.AutoStart != null)) { Begin(); return; }
        if (started && !PNGameFlow.Booting && !PNGameFlow.Busy && PNGameFlow.BootFailure != null)
        {
            started = false; column.SetEnabled(true);
            error.text = PNGameFlow.BootFailure; error.style.display = DisplayStyle.Flex;
            start.text = "Thử lại"; start.Focus();
        }
    }

    private void OnDestroy()
    {
        backdrop?.Dispose(); backdrop = null; sparkles?.Dispose(); sparkles = null;
        if (settings != null) { if (Application.isPlaying) Destroy(settings); else DestroyImmediate(settings); settings = null; }
    }
}
#endif
