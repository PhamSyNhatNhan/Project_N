#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using UnityEngine;

public static class PNGameBootstrap
{
    public static EveGameRuntime Runtime { get; private set; }
    public static PNPlayConfig Config { get; private set; } = new PNPlayConfig();
    private static IPNNetworkSessionDriver driver;
    private static bool networkRequested;
    private static bool sceneEntryConsumed, booted;
    private static string offlineModeId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Runtime = null; driver = null; networkRequested = false; sceneEntryConsumed=false; booted=false; offlineModeId=null; Config = new PNPlayConfig(); AutoStart = null; PendingTarget = null;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Startup()
    {
        PNSettingsService.EnsureLoaded();
        // The content is no longer loaded here: it loads once, behind the first loading screen, when the Title scene starts the boot (see PNGameFlow.StartBoot).
        Config = PNPlayConfig.FromArguments(Environment.GetCommandLineArgs());
        AutoStart = PNBootTarget.FromArguments(Environment.GetCommandLineArgs());
    }
    public static void RegisterNetworkDriver(IPNNetworkSessionDriver value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        if (driver != null && !ReferenceEquals(driver, value)) throw new InvalidOperationException("Network driver already registered.");
        driver = value;
    }
    public static EveGameRuntime EnsureContent()
    {
        if (Runtime != null)
        {
            Runtime.EnsureContentLoaded(); booted = true;
            Runtime.gameObject.SetActive(true);
            return Runtime;
        }
        var root = new GameObject("Project N Runtime");
        root.SetActive(false);
        Runtime = root.AddComponent<EveGameRuntime>();
        Runtime.IsBootstrapOwner = true;
        // Retain the runtime on failure, including any retryable cleanup it owns.
        Runtime.EnsureContentLoaded(); booted = true;
        root.SetActive(true);
        return Runtime;
    }
    // The staged form of EnsureContent: the runtime object is created inactive and the content loads step by step through the returned runner
    // (see PNBootRunner); the runtime is activated when the last step is done. Retained on failure, so a retry builds a new runner on it.
    public static PNBootRunner BeginStagedContent()
    {
        if (Runtime != null && Runtime.IsContentReady) { Runtime.gameObject.SetActive(true); return PNBootRunner.Finished(); }
        if (Runtime == null)
        {
            var root = new GameObject("Project N Runtime");
            root.SetActive(false);
            Runtime = root.AddComponent<EveGameRuntime>();
            Runtime.IsBootstrapOwner = true;
        }
        var runtime = Runtime;
        return new PNBootRunner(runtime.ContentLoadSteps(), () => runtime.gameObject.SetActive(true));
    }
    internal static void ResetForTests() => Reset();

    // ---- boot through the Title scene (Docs/LoadingHandoffPlan.md P6) ----
    // From --pn-auto-start=<hall | mode | scene>; the Title acts on it without a click.
    public static PNBootTarget AutoStart { get; private set; }
    // A scene that was opened directly (Editor Play on a scene, a test Player whose first scene is not the Title) is sent through the Title and comes
    // back to this target: one load path for everything.
    public static PNBootTarget PendingTarget { get; private set; }
    // True once the content is loaded (by the staged boot or, for older callers, by EnsureContent).
    public static bool Booted => booted;
    // The Title's boot finished loading content. A scene target lets that scene's own entry start its play once; every other target is started by PNGameFlow.
    internal static void MarkBooted(bool sceneEntryMayStart) { booted = true; sceneEntryConsumed = !sceneEntryMayStart; PendingTarget = null; }
    internal static void RedirectToTitle(string scenePath)
    {
        PendingTarget = PNBootTarget.Scene(scenePath); sceneEntryConsumed = true;
        UnityEngine.SceneManagement.SceneManager.LoadScene(PNScenes.Title);
    }
    public static void Begin(PNPlayConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if(networkRequested && driver!=null && !driver.IsRunning){driver.End();networkRequested=false;}
        var runtime = EnsureContent();
        if (runtime.IsReady || networkRequested)
        {
            if (Config.Role != config.Role || Config.ModeId != config.ModeId || Config.Address != config.Address)
                throw new InvalidOperationException("End the current play before changing configuration.");
            return;
        }
        if (config.Role == EveSessionRole.Offline)
        {
            PNModeDefinition.Select(runtime.Content.Registry,config.ModeId,config.Network);
            Config = config; offlineModeId=config.ModeId; runtime.BeginSession(config.Role); return;
        }
        if (driver == null) throw new InvalidOperationException("Host/Client requires a registered network session driver.");
        Config = config; networkRequested = true;
        try { driver.Begin(runtime, config); }
        catch { End(); throw; }
    }
    public static bool IsRoomOpen => (driver as IPNRoomSessionDriver)?.IsRoomOpen == true;
    public static IPNLobbyDriver LobbyDriver => driver as IPNLobbyDriver;
    // True when no room object, transport or teardown remains, so a new room or play may start.
    public static bool RoomIdle => !networkRequested && (LobbyDriver?.IsIdle ?? !IsRoomOpen);
    public static string ModeId => networkRequested ? RoomDriver.ModeId : offlineModeId??Config.ModeId;
    public static bool IsPlaying => Runtime != null && Runtime.IsReady;
    public static bool CanBeginPlay => networkRequested ? (driver as IPNRoomSessionDriver)?.CanBeginPlay == true :
        EveRegistrySession.State == EveSessionState.Stopped;
    internal static void SceneEntry(EveGameRuntime entry)
    {
        // Only the first entry in the startup scene preserves legacy auto-Begin.
        // An additive scene is content, never another play command.
        if(sceneEntryConsumed || networkRequested || entry.gameObject.scene != UnityEngine.SceneManagement.SceneManager.GetActiveScene())return;
        if(!booted)
        {
            // Not started through the Title yet: go there and come back to this scene (an unsaved scene has no path to come back to: the old direct start).
            string path=entry.gameObject.scene.path;
            if(path==PNScenes.Title)return;
            // A build without the Title scene (a disposable test Player) keeps the old direct start.
            if(!string.IsNullOrEmpty(path)&&Application.CanStreamedLevelBeLoaded(PNScenes.Title)){RedirectToTitle(path);return;}
        }
        sceneEntryConsumed=true;
        if(Config.Role==EveSessionRole.Offline&&!Config.ModeExplicit)
        {
            // A start scene can declare its own offline mode (the Hall); explicit --pn-mode still wins.
            var marker=PNEntrySceneMarker.Find(entry.gameObject.scene);
            if(marker!=null&&!string.IsNullOrWhiteSpace(marker.ModeId))
                Config=new PNPlayConfig(Config.Role,marker.ModeId,Config.Address,Config.Network,Config.Preparation);
        }
        if(!System.Array.Exists(Environment.GetCommandLineArgs(),x=>x=="--pn-lobby"))Begin(Config);
    }
    private static IPNRoomSessionDriver RoomDriver => driver as IPNRoomSessionDriver ??
        throw new NotSupportedException("The registered network driver does not support separate room/play lifecycle.");
    public static void OpenRoom(PNPlayConfig config)
    {
        if(config==null || config.Role==EveSessionRole.Offline)throw new System.ArgumentException("A room requires Host or Client configuration.");
        if(networkRequested || IsPlaying)throw new System.InvalidOperationException("Close the previous room/play first.");
        var runtime=EnsureContent(); var room=RoomDriver;
        Config=config; sceneEntryConsumed=true; networkRequested=true;
        try{room.OpenRoom(runtime,config);}catch{End();throw;}
    }
    public static void BeginPlay(string modeId=null)
    {
        if(networkRequested){RoomDriver.BeginPlay(modeId??Config.ModeId);return;}
        if(Config.Role!=EveSessionRole.Offline)throw new InvalidOperationException("Open a room before starting network play.");
        var runtime=EnsureContent();
        PNModeDefinition.Select(runtime.Content.Registry,modeId??Config.ModeId,Config.Network);
        offlineModeId=modeId??Config.ModeId;
        runtime.BeginSession(EveSessionRole.Offline);
    }
    public static void EndPlay()
    {
        if(networkRequested)RoomDriver.EndPlay();else Runtime?.EndSession();
    }
    public static void CloseRoom()=>End();
    public static void End()
    {
        if (networkRequested) { driver.End(); networkRequested = false; }
        if (Runtime != null) Runtime.EndSession();
    }
}
#endif
