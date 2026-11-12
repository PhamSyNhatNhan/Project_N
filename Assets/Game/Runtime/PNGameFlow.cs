#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public enum PNFlowPhase { Hall, Loading, Lobby, Run }
public enum PNFlowMode { Offline, Host, Join }

// Placeholder coordinator for Hall -> (lobby) -> run -> Hall. It owns the scene changes and the play lifecycle;
// UI and later interactive Hall objects call the same entry points.
public sealed class PNGameFlow : MonoBehaviour
{
    private const double SessionEndTimeout = 20, ConnectTimeout = 30, RunTimeout = 60;
    private static PNGameFlow instance;
    private static bool loading, expectSession;
    private static string message;
    private static bool starting;
    public static event Action Changed;
    private static string messageKey,startErrorKey;
    private static int messageLocaleRevision;
    public static long NoticeSequence { get; private set; }
    public static string Message { get { if(messageKey!=null&&messageLocaleRevision!=PNLocalization.Revision){message=PNLocalization.Text(messageKey);messageLocaleRevision=PNLocalization.Revision;}return message; } }
    public static bool Busy => loading || starting;
    public static PNFlowMode CurrentMode { get; private set; }
    public static PNFlowPhase Phase
    {
        get
        {
            if (loading) return PNFlowPhase.Loading;
            var id = EveRegistrySession.IsReady ? PNLocalWorldContext.Current?.World?.DefinitionId.ToString() : null;
            if (id == "project_n:roguelite_lobby") return PNFlowPhase.Lobby;
            if (id == "project_n:scene_world" || id != null && id.StartsWith("project_n:map_", StringComparison.Ordinal)) return PNFlowPhase.Run;
            if (id == "project_n:hall_world") return PNFlowPhase.Hall;
            // Before the local view exists, fall back to the selected mode.
            if (!EveRegistrySession.IsReady) return PNFlowPhase.Hall;
            return PNGameBootstrap.ModeId == "hall" ? PNFlowPhase.Hall : (PNGameBootstrap.IsRoomOpen ? PNFlowPhase.Lobby : PNFlowPhase.Run);
        }
    }
    // The run the next start plays (Docs/RunSavePlan.md 5.8): set by the menu before StartMode, taken by BeginRun. Null = the standard run.
    public static readonly EveId DefaultRun = EveId.Parse("project_n:standard_run");
    public static PNRunRequest NextRun;
    public static bool IsHost => EveRegistrySession.IsReady && EveRegistrySession.Context.Role == EveSessionRole.Host;
    public static bool InRoom => PNGameBootstrap.IsRoomOpen;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { instance = null; loading = expectSession = starting = usingHandoff = false; coverHost = null; coverSettings = null; coverUntil = 0; message = null; messageKey=null; startErrorKey=null; NoticeSequence=0; messageLocaleRevision=-1; Changed = null; CurrentMode = PNFlowMode.Offline; NextRun = null; }
    private static PNGameFlow Ensure()
    {
        if (instance != null) return instance;
        // Its own persistent object: the flow must exist before the content (and so the runtime object) does, for the Title boot.
        var host = new GameObject("Project N Flow");
        if (Application.isPlaying) DontDestroyOnLoad(host);
        instance = host.AddComponent<PNGameFlow>();
        return instance;
    }
    private static void Raise() { try { Changed?.Invoke(); } catch (Exception error) { Debug.LogException(error); } }
    private static void Say(string value) { messageKey=null;message=value;NoticeSequence++;Raise(); }
    private static void SayKey(string key){messageKey=key;messageLocaleRevision=-1;NoticeSequence++;Raise();}
    private static bool Refuse(string key,out string reason){startErrorKey=key;reason=PNLocalization.Text(key);return false;}

    // ErMode is the developers' test arena (the mob Player reviews use its authored spawn points). A solo start goes through the lobby into the first room of a run like every
    // other start; this switch keeps the old straight-to-arena solo start for those reviews.
    public static bool LegacyArena { get { return Array.IndexOf(Environment.GetCommandLineArgs(), "-pn-legacy-run") >= 0; } }
    // A test sets this to replay a known run (the same seed offers the same rooms); -pn-run-seed=<n> does the same for a Player.
    public static ulong? DebugSeed;
    private static ulong RunSeed()
    {
        if (DebugSeed.HasValue) return DebugSeed.Value;
        foreach (var argument in Environment.GetCommandLineArgs())
            if (argument.StartsWith("-pn-run-seed=", StringComparison.Ordinal) && ulong.TryParse(argument.Substring("-pn-run-seed=".Length), out var fixedSeed)) return fixedSeed;
        return ((ulong)(uint)Guid.NewGuid().GetHashCode() << 32) ^ (ulong)DateTime.UtcNow.Ticks;
    }
    // Start the roguelite from the Hall (or from anywhere): every start goes to the lobby and then, with the team there, into the first room of a run (a solo start goes on by itself).
    public static void StartRoguelite(PNFlowMode mode, string address = null) => StartMode("roguelite", mode, address);
    // Start any registered mode by its id (the menu passes the mode it listed). Scenes are still the roguelite ones: a mode with its own
    // scenes needs a scene mapping first (Docs/ModeMenuPlan.md).
    public static void StartMode(string modeId, PNFlowMode mode, string address = null)
    {
        if (Busy) return;
        PNRunSummary.Clear();
        // Shipped modes keep their short selection name ("roguelite"), which rooms and the admission already compare; others use the full id.
        if (modeId.StartsWith("project_n:", StringComparison.Ordinal)) modeId = modeId.Substring("project_n:".Length);
        else if (modeId.StartsWith("base:", StringComparison.Ordinal)) modeId = modeId.Substring("base:".Length);
        if (mode == PNFlowMode.Join && string.IsNullOrWhiteSpace(address)) { preCoverUntil = 0; if (usingHandoff) PNTransitionHandoffHost.Abandon(); SayKey("project_n:error.address_required"); return; }
        CurrentMode = mode;
        var flow = Ensure(); flow.StartCoroutine(flow.Launch(modeId, mode, address));
    }
    // Leave the current room/run and go back to the offline Hall.
    public static void ReturnToHall(string notice = null)
    {
        if (loading) return;
        var flow = Ensure(); flow.StartCoroutine(flow.Return(notice));
    }
    // ---- boot: Title -> Loading #1 -> content -> target (Docs/LoadingHandoffPlan.md P6) ----
    // The only way the game starts: the Title scene (or a scene sent through it) calls this. The loading screen is raised at once (there is no content
    // yet, so it is the default screen built from its payload), the content loads step by step beneath it, then the target starts through the same
    // paths as everything else: Hall = Return, mode = Launch, scene = that scene's own entry. A failure ends the loading and is left in BootFailure for
    // the Title to show with a retry.
    public const double BootBudgetMilliseconds = 8;
    public static string BootFailure { get; private set; }
    public static bool Booting { get; private set; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Test hook: the next boot fails right after the loading screen is up, to check the Title's error line and retry.
    public static bool DebugFailNextBoot;
#endif
    public static void StartBoot(PNBootTarget target)
    {
        if (Booting || Busy) return;
        BootFailure = null; Booting = true;
        var flow = Ensure(); flow.StartCoroutine(flow.Boot(target ?? PNBootTarget.Hall));
    }
    private IEnumerator Boot(PNBootTarget target)
    {
        BeginLoading(target.Kind == PNBootTargetKind.Mode ? target.Value : "hall", "project_n:hall_world"); Raise();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (DebugFailNextBoot) { DebugFailNextBoot = false; BootFailed(new InvalidOperationException("Simulated boot failure (test hook).")); yield break; }
#endif
        PNBootRunner runner = null;
        try { runner = PNGameBootstrap.BeginStagedContent(); }
        catch (Exception error) { BootFailed(error); yield break; }
        while (!runner.Pump(BootBudgetMilliseconds)) { PNTransitionHandoffHost.SetProgress(runner.Fraction); yield return null; }
        if (runner.Failed) { BootFailed(runner.Failure); yield break; }
        PNTransitionHandoffHost.SetProgress(1f);
        Debug.Log("Boot: content loaded in " + runner.TotalMilliseconds.ToString("0") + " ms over " + runner.Timings.Count + " steps (" + string.Join(", ", System.Linq.Enumerable.Select(runner.Timings, t => t.Name + " " + t.Milliseconds.ToString("0") + " ms")) + "), longest step " + runner.LongestStepMilliseconds.ToString("0") + " ms; target " + target);
        PNGameBootstrap.MarkBooted(target.Kind == PNBootTargetKind.Scene);
        switch (target.Kind)
        {
            case PNBootTargetKind.Hall:
                yield return Return(null); break;
            case PNBootTargetKind.Mode:
            {
                var role = PNGameBootstrap.Config.Role;
                var mode = role == EveSessionRole.Offline ? PNFlowMode.Offline : role == EveSessionRole.Host ? PNFlowMode.Host : PNFlowMode.Join;
                CurrentMode = mode;
                yield return Launch(StripPrefix(target.Value), mode, PNGameBootstrap.Config.Address); break;
            }
            default:
                yield return BootScene(target.Value); break;
        }
        Booting = false;
    }
    // A scene that was opened directly comes back: its own entry starts its play (one allowed entry), and the loading ends when the play is ready.
    private IEnumerator BootScene(string path)
    {
        string failure = null;
        var load = LoadScene(path);
        while (true) { bool more; try { more = load.MoveNext(); } catch (Exception error) { failure = error.Message; break; } if (!more) break; yield return load.Current; }
        double until = Time.realtimeSinceStartupAsDouble + RunTimeout;
        while (failure == null && !(EveRegistrySession.IsReady && PNLocalWorldContext.Current?.World != null))
        {
            if (Time.realtimeSinceStartupAsDouble > until) failure = "The scene's play did not become ready.";
            else yield return null;
        }
        if (failure != null) { Debug.LogWarning("Boot scene failed: " + failure); yield return Return(null,"project_n:error.boot_failed"); yield break; }
        EndLoading();
    }
    private void BootFailed(Exception error)
    {
        Debug.LogException(error);
        BootFailure = error.Message; Booting = false; loading = false; coverUntil = 0;
        if (usingHandoff) PNTransitionHandoffHost.Abandon();
        Raise();
    }

    // EN: "Play again" from the pause menu (alone or as the host): leave this play and continue the saved run, which is the start of the room the team is in. A host closes its room, so the clients return to
    //     the Hall and join the new room again. Returns false with a reason when there is nothing to continue.
    public static bool CanRestartFromSave()
    {
        if (Busy || CurrentMode == PNFlowMode.Join || !EveRegistrySession.IsReady) return false;
        var load = PNRunSaveService.Peek();
        return load.Save != null && PNRunSaveService.Check(load.Save, EveRegistrySession.Current) == PNRunSaveProblem.None;
    }
    // "Sum up now" in the pause menu (Docs/StellaPlan.md Q6-Q7): the run ends as it stands (retired). Alone or as the host only: a client has the room's Leave instead.
    private static PNRunDirector CurrentRuns() => PNGameBootstrap.Runtime != null ? PNGameBootstrap.Runtime.GetComponent<PNGameSession>()?.Runs : null;
    public static bool CanRetireRun()
    {
        if (Busy || CurrentMode == PNFlowMode.Join || !EveRegistrySession.IsReady || Phase != PNFlowPhase.Run) return false;
        var runs = CurrentRuns();
        return runs != null && runs.Phase == PNRunPhase.Playing && runs.State != null && runs.State.IsRunning;
    }
    public static bool RetireRun() => CanRetireRun() && CurrentRuns().Retire();
    public static bool RestartFromSave(out string reason)
    {
        reason = null;
        if (Busy || CurrentMode == PNFlowMode.Join || !EveRegistrySession.IsReady) { reason = "busy"; return false; }
        var load = PNRunSaveService.Peek();
        if (load.Save == null) { reason = "no saved run"; return false; }
        var problem = PNRunSaveService.Check(load.Save, EveRegistrySession.Current);
        if (problem != PNRunSaveProblem.None) { reason = "the saved run cannot be continued: " + problem; return false; }
        PNPlayerProfileService.SetLastCharacter(PNRunSaveService.SeatOf(load.Save, PNPlayerProfileService.PlayerId).Character.ToString());
        NextRun = PNRunRequest.Continue(load.Save);
        StartMode("roguelite", CurrentMode);
        return true;
    }

    // EN: A continued run, seen from the host's lobby: the seats of the save that nobody in the room holds. The lobby draws them grey and the Start button asks twice (a seat nobody takes is left out of
    //     the run for good). Empty for a new run, for a client and alone.
    public static System.Collections.Generic.IReadOnlyList<PNRunSaveSeat> MissingSeats()
    {
        var save = NextRun?.Save;
        if (save == null || !IsHost || !PNGameBootstrap.IsRoomOpen) return System.Array.Empty<PNRunSaveSeat>();
        var directory = PNPlayerDirectories.Current;
        return save.Seats.Where(seat => directory.OwnerOf(seat.PlayerId) == null).ToList();
    }
    // A short notice (the toast) from a localization key.
    public static void Warn(string key) => SayKey(key);

    public static bool CanStartRun(out string reason)
    {
        reason = null;
        if (starting) return Refuse("project_n:error.starting",out reason);
        bool alone = EveRegistrySession.IsReady && EveRegistrySession.Context.Role == EveSessionRole.Offline;
        if (!(IsHost || alone) || Phase != PNFlowPhase.Lobby) return Refuse("project_n:error.host_only",out reason);
        if (alone) return true;
        var lobby = PNGameBootstrap.LobbyDriver?.Lobby;
        if (lobby == null) return Refuse("project_n:error.no_room",out reason);
        if (!lobby.AllReady) return Refuse("project_n:error.not_ready",out reason);
        return true;
    }
    public static void StartRun()
    {
        if (!CanStartRun(out _)) { SayKey(startErrorKey); return; }
        var flow = Ensure(); flow.StartCoroutine(flow.BeginRun());
    }

    private static IEnumerator EndCurrent()
    {
        expectSession = false;
        PNGameBootstrap.LobbyDriver?.StopDiscovery();
        PNGameBootstrap.End();
        double until = Time.realtimeSinceStartupAsDouble + SessionEndTimeout;
        while (Time.realtimeSinceStartupAsDouble < until)
        {
            var state = EveRegistrySession.State;
            bool sessionDone = state != EveSessionState.Starting && state != EveSessionState.Ready && state != EveSessionState.Stopping;
            if (sessionDone && PNGameBootstrap.RoomIdle) yield break;
            if (state == EveSessionState.Stopping) EveRegistrySession.PumpCleanup();
            yield return null;
        }
        throw new TimeoutException("Previous play did not finish ending.");
    }
    private static IEnumerator LoadScene(string path)
    {
        var operation = SceneManager.LoadSceneAsync(path, LoadSceneMode.Single);
        if (operation == null) throw new InvalidOperationException("Scene is not in Build Settings: " + path);
        while (!operation.isDone) yield return null;
        yield return null; // let scene Awake/OnEnable settle before the play starts
    }
    private IEnumerator Launch(string modeId, PNFlowMode mode, string address)
    {
        BeginLoading(modeId, mode == PNFlowMode.Offline && LegacyArena ? "project_n:scene_world" : "project_n:roguelite_lobby"); message = null; messageKey=null; Raise();
        string failure = null;
        var network = PNGameBootstrap.Config.Network; var preparation = PNGameBootstrap.Config.Preparation;
        var end = EndCurrent();
        while (true) { bool more; try { more = end.MoveNext(); } catch (Exception error) { failure = error.Message; break; } if (!more) break; yield return end.Current; }
        if (failure == null)
        {
            var load = LoadScene(mode == PNFlowMode.Offline && LegacyArena ? PNScenes.Run : PNScenes.Lobby);
            while (true) { bool more; try { more = load.MoveNext(); } catch (Exception error) { failure = error.Message; break; } if (!more) break; yield return load.Current; }
        }
        if (failure == null)
        {
            try
            {
                var role = mode == PNFlowMode.Offline ? EveSessionRole.Offline : mode == PNFlowMode.Host ? EveSessionRole.Host : EveSessionRole.Client;
                PNGameBootstrap.Begin(new PNPlayConfig(role, modeId, string.IsNullOrWhiteSpace(address) ? "localhost" : address.Trim(), network, preparation, true));
            }
            catch (Exception error) { failure = error.Message; }
        }
        double until = Time.realtimeSinceStartupAsDouble + ConnectTimeout;
        while (failure == null && !(EveRegistrySession.IsReady && PNLocalWorldContext.Current?.World != null && (mode == PNFlowMode.Offline || PNGameBootstrap.IsPlaying)))
        {
            if (Time.realtimeSinceStartupAsDouble > until) failure = LobbyFailure() ?? "Không kết nối được tới phòng.";
            else if (mode != PNFlowMode.Offline && !PNGameBootstrap.IsRoomOpen) failure = LobbyFailure() ?? "Phòng không khả dụng.";
            else yield return null;
        }
        EndLoading();
        if (failure != null) { Debug.LogWarning("Roguelite start failed: " + failure); yield return Return(null,"project_n:error.join_failed"); yield break; }
        expectSession = true; Raise();
        // Alone there is nobody to wait for: the run starts by itself once the lobby is up.
        if (mode == PNFlowMode.Offline && !LegacyArena && modeId == "roguelite")
        {
            // The player is built a moment after the session is ready; the run takes it along, so wait for it.
            double waitUntil = Time.realtimeSinceStartupAsDouble + ConnectTimeout;
            while (Time.realtimeSinceStartupAsDouble < waitUntil && !(EveRegistrySession.IsReady && PNLocalWorldContext.Current?.Actor?.IsReady == true)) yield return null;
            StartRun();
        }
    }
    private IEnumerator Return(string notice,string noticeKey=null)
    {
        BeginLoading("hall", "project_n:hall_world"); Raise();
        string failure = null;
        var network = PNGameBootstrap.Config.Network; var preparation = PNGameBootstrap.Config.Preparation;
        foreach (var step in new Func<IEnumerator>[] { EndCurrent, () => LoadScene(PNScenes.Hall) })
        {
            var routine = step();
            while (failure == null) { bool more; try { more = routine.MoveNext(); } catch (Exception error) { failure = error.Message; break; } if (!more) break; yield return routine.Current; }
            if (failure != null) break;
        }
        if (failure == null)
        {
            try { PNGameBootstrap.Begin(new PNPlayConfig(EveSessionRole.Offline, "hall", "localhost", network, preparation, true)); }
            catch (Exception error) { failure = error.Message; }
        }
        double until = Time.realtimeSinceStartupAsDouble + RunTimeout;
        while (failure == null && !(EveRegistrySession.IsReady && PNLocalWorldContext.Current?.World != null))
        {
            if (Time.realtimeSinceStartupAsDouble > until) failure = "Hall did not start.";
            else yield return null;
        }
        EndLoading(); CurrentMode = PNFlowMode.Offline;
        if (failure != null) Debug.LogError("Return to Hall failed: " + failure);
        if(failure!=null)SayKey("project_n:error.return_failed");else if(noticeKey!=null)SayKey(noticeKey);else Say(notice);
    }
    private IEnumerator BeginRun()
    {
        starting = true; Raise();
        // A room driver exists only for a hosted or joined room; alone there is nothing to lock or mark.
        var driver = PNGameBootstrap.IsRoomOpen ? PNGameBootstrap.LobbyDriver : null;
        string failure = null; PNRunDirector runs = null;
        try
        {
            var game = PNGameBootstrap.Runtime.GetComponent<PNGameSession>();
            driver?.LockAdmissions(true);
            runs = game.Runs;
            var request = NextRun; NextRun = null;
            string reason;
            bool begun = request != null && request.IsContinue ? runs.Resume(request.Save, PNLocalWorldContext.Current.World, out reason) : runs.Begin(request != null ? request.Run : DefaultRun, PNLocalWorldContext.Current.World, RunSeed(), out reason);
            if (!begun) failure = reason;
        }
        catch (Exception error) { failure = error.Message; }
        double until = Time.realtimeSinceStartupAsDouble + RunTimeout;
        while (failure == null && runs.Phase != PNRunPhase.Playing)
        {
            if (runs.Phase == PNRunPhase.Ended || runs.Phase == PNRunPhase.None) failure = runs.Error ?? "The run ended before it started.";
            else if (Time.realtimeSinceStartupAsDouble > until) failure = "Chuyển sang run quá lâu.";
            else yield return null;
        }
        if (failure == null) { try { driver?.MarkRunStarted(); } catch (Exception error) { failure = error.Message; } }
        if (failure != null)
        {
            Debug.LogWarning("Run start failed: " + failure);
            try { runs?.Abandon(); } catch (Exception error) { Debug.LogException(error); }
            try { driver?.LockAdmissions(false); } catch (Exception error) { Debug.LogException(error); }
            starting = false; SayKey("project_n:error.run_failed"); yield break;
        }
        starting = false; Raise();
    }

    // Leaving without UI: a client whose room vanished, or a session that ended unexpectedly, returns to the Hall.
    private void Update()
    {
        if (loading || !expectSession) return;
        var state = EveRegistrySession.State;
        bool ended = state == EveSessionState.Stopped || state == EveSessionState.Failed || (state == EveSessionState.Ready && CurrentMode != PNFlowMode.Offline && !PNGameBootstrap.IsRoomOpen);
        if (!ended) return;
        expectSession = false;
        string reason = LobbyFailure();
        // Only a reason (a failure) is worth telling; "the run ended" or "the room closed" is already plain from the Hall appearing.
        ReturnToHall(CurrentMode == PNFlowMode.Offline || string.IsNullOrEmpty(reason) ? null : "Phòng đã đóng: " + reason);
    }

    // The lobby driver's last failure, translated when it carries a localization key.
    private static string LobbyFailure()
    {
        var driver = PNGameBootstrap.LobbyDriver;
        if (driver == null) return null;
        string key = driver.LastFailureKey;
        return key != null ? PNLocalization.Text(key, null, driver.LastFailure) : driver.LastFailure;
    }

    // While one play is torn down and the next one starts there is no UI of ours, only the freshly loaded scene. This cover stays up from the
    // first moment until the transition screen of the new play is showing (or a short deadline), so the new scene never shows before the
    // loading screen. Same colour and text as the default transition screen, so the hand-over is not seen.
    private static double coverUntil;
    private static bool Covering
    {
        get
        {
            if (loading || Time.realtimeSinceStartupAsDouble < preCoverUntil) return true;
            if (Time.realtimeSinceStartupAsDouble > coverUntil) return false;
            // Handed over once the new play's transition screen is up, or has already come and gone since this load began.
            var current = PNTransitionController.Current;
            if (current == null) return true;
            bool shown = ReferenceEquals(current, coverRef) ? current.ShowCount > coverBase : current.ShowCount > 0;
            return !(current.IsShowing || shown);
        }
    }
    private static PNTransitionController coverRef;
    private static int coverBase;
    // Called the moment the player commits to a start (the menu is still fading out): the cover goes up at once so the scene behind the fading menu is never seen.
    private static double preCoverUntil;
    public static void PrepareStart(string modeId = "roguelite", bool offline = false)
    {
        preCoverUntil = Time.realtimeSinceStartupAsDouble + 3;
        usingHandoff = PNTransitionHandoffHost.Raise(StripPrefix(modeId), offline && LegacyArena ? "project_n:scene_world" : "project_n:roguelite_lobby");
        if (!usingHandoff) ShowCover();
    }
    // Shipped modes keep their short selection name; others use the full id (the same rule StartMode applies).
    private static string StripPrefix(string modeId)
    {
        if (string.IsNullOrEmpty(modeId)) return "roguelite";
        if (modeId.StartsWith("project_n:", StringComparison.Ordinal)) return modeId.Substring("project_n:".Length);
        return modeId.StartsWith("base:", StringComparison.Ordinal) ? modeId.Substring("base:".Length) : modeId;
    }
    // True while the loading screen of the current start belongs to the handoff host (so the plain fallback cover stays down).
    private static bool usingHandoff;
    private static void BeginLoading(string modeId, string worldDefinition)
    {
        preCoverUntil = 0; loading = true; coverRef = PNTransitionController.Current; coverBase = coverRef != null ? coverRef.ShowCount : 0; 
        usingHandoff = PNTransitionHandoffHost.Raise(StripPrefix(modeId), worldDefinition);
        if (!usingHandoff) ShowCover();
    }
    private static void EndLoading()
    {
        loading = false; coverUntil = Time.realtimeSinceStartupAsDouble + 3;
        if (usingHandoff) PNTransitionHandoffHost.MarkLoadingEnded();
    }

    // The cover is a UI Toolkit document of its own (like the transition screen it hands over to, sorted above it), kept outside any play so it
    // survives the UI being torn down.
    private const float CoverSortingOrder = 200f;
    private static GameObject coverHost;
    private static PanelSettings coverSettings;
    private static void ShowCover()
    {
        if (coverHost != null) return;
        var payload = PNTransitionPayload.Default;
        coverHost = new GameObject("PN Loading Cover (UI Toolkit)");
        if (Application.isPlaying) DontDestroyOnLoad(coverHost);
        coverSettings = ScriptableObject.CreateInstance<PanelSettings>(); coverSettings.name = "Loading cover panel settings";
        coverSettings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/Combat/RuntimeTheme");
        coverSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize; coverSettings.referenceResolution = new Vector2Int(1280, 720); coverSettings.match = .5f;
        coverSettings.sortingOrder = CoverSortingOrder;
        var document = coverHost.AddComponent<UIDocument>(); document.panelSettings = coverSettings;
        var root = document.rootVisualElement;
        root.style.position = Position.Absolute; root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;
        root.style.backgroundColor = new Color(payload.R, payload.G, payload.B, payload.A);
        root.style.alignItems = Align.Center; root.style.justifyContent = Justify.Center; root.pickingMode = PickingMode.Ignore;
        var label = new Label(payload.Text) { pickingMode = PickingMode.Ignore };
        label.style.color = Color.white; label.style.fontSize = 36; label.style.unityTextAlign = TextAnchor.MiddleCenter; root.Add(label);
    }
    private static void HideCover()
    {
        if (coverHost != null) Destroy(coverHost);
        if (coverSettings != null) Destroy(coverSettings);
        coverHost = null; coverSettings = null;
    }
    private void LateUpdate()
    {
        PNTransitionHandoffHost.Tick(Time.unscaledDeltaTime);
        // The press committed but the start never began (refused, busy): the raised screen is let go instead of waiting for ever.
        if (usingHandoff && !loading && preCoverUntil > 0 && Time.realtimeSinceStartupAsDouble > preCoverUntil) { preCoverUntil = 0; PNTransitionHandoffHost.Abandon(); }
        if (usingHandoff) HideCover(); else if (Covering) ShowCover(); else HideCover();
    }
}
#endif
