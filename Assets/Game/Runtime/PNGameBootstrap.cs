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
    private static bool sceneEntryConsumed;
    private static string offlineModeId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Runtime = null; driver = null; networkRequested = false; sceneEntryConsumed=false; offlineModeId=null; Config = new PNPlayConfig();
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Startup()
    {
        Config = PNPlayConfig.FromArguments(Environment.GetCommandLineArgs());
        EnsureContent();
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
            Runtime.EnsureContentLoaded();
            Runtime.gameObject.SetActive(true);
            return Runtime;
        }
        var root = new GameObject("Project N Runtime");
        root.SetActive(false);
        Runtime = root.AddComponent<EveGameRuntime>();
        Runtime.IsBootstrapOwner = true;
        // Retain the runtime on failure, including any retryable cleanup it owns.
        Runtime.EnsureContentLoaded();
        root.SetActive(true);
        return Runtime;
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
    public static string ModeId => networkRequested ? RoomDriver.ModeId : offlineModeId??Config.ModeId;
    public static bool IsPlaying => Runtime != null && Runtime.IsReady;
    public static bool CanBeginPlay => networkRequested ? (driver as IPNRoomSessionDriver)?.CanBeginPlay == true :
        EveRegistrySession.State == EveSessionState.Stopped;
    internal static void SceneEntry(EveGameRuntime entry)
    {
        // Only the first entry in the startup scene preserves legacy auto-Begin.
        // An additive scene is content, never another play command.
        if(sceneEntryConsumed || networkRequested || entry.gameObject.scene != UnityEngine.SceneManagement.SceneManager.GetActiveScene())return;
        sceneEntryConsumed=true;
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
