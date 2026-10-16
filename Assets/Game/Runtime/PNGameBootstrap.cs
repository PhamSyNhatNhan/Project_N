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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Runtime = null; driver = null; networkRequested = false; Config = new PNPlayConfig();
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
        if (config.Role == EveSessionRole.Offline) { Config = config; runtime.BeginSession(config.Role); return; }
        if (driver == null) throw new InvalidOperationException("Host/Client requires a registered network session driver.");
        Config = config; networkRequested = true;
        try { driver.Begin(runtime, config); }
        catch { End(); throw; }
    }
    public static void End()
    {
        if (networkRequested) { driver.End(); networkRequested = false; }
        if (Runtime != null) Runtime.EndSession();
    }
}
#endif
