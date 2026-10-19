#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    public static string Message => message;
    public static bool Busy => loading || starting;
    public static PNFlowMode CurrentMode { get; private set; }
    public static PNFlowPhase Phase
    {
        get
        {
            if (loading) return PNFlowPhase.Loading;
            var id = EveRegistrySession.IsReady ? PNLocalWorldContext.Current?.World?.DefinitionId.ToString() : null;
            if (id == "project_n:roguelite_lobby") return PNFlowPhase.Lobby;
            if (id == "project_n:scene_world") return PNFlowPhase.Run;
            if (id == "project_n:hall_world") return PNFlowPhase.Hall;
            // Before the local view exists, fall back to the selected mode.
            if (!EveRegistrySession.IsReady) return PNFlowPhase.Hall;
            return PNGameBootstrap.ModeId == "hall" ? PNFlowPhase.Hall : (PNGameBootstrap.IsRoomOpen ? PNFlowPhase.Lobby : PNFlowPhase.Run);
        }
    }
    public static bool IsHost => EveRegistrySession.IsReady && EveRegistrySession.Context.Role == EveSessionRole.Host;
    public static bool InRoom => PNGameBootstrap.IsRoomOpen;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { instance = null; loading = expectSession = starting = false; message = null; Changed = null; CurrentMode = PNFlowMode.Offline; }
    private static PNGameFlow Ensure()
    {
        if (instance != null) return instance;
        var runtime = PNGameBootstrap.EnsureContent();
        instance = runtime.GetComponent<PNGameFlow>() ?? runtime.gameObject.AddComponent<PNGameFlow>();
        return instance;
    }
    private static void Raise() { try { Changed?.Invoke(); } catch (Exception error) { Debug.LogException(error); } }
    private static void Say(string value) { message = value; Raise(); }

    // Start the roguelite from the Hall (or from anywhere): Offline goes straight to the run scene, Host/Join to the lobby.
    public static void StartRoguelite(PNFlowMode mode, string address = null)
    {
        if (Busy) return;
        if (mode == PNFlowMode.Join && string.IsNullOrWhiteSpace(address)) { Say("Nhập địa chỉ phòng trước khi vào."); return; }
        CurrentMode = mode;
        var flow = Ensure(); flow.StartCoroutine(flow.Launch(mode, address));
    }
    // Leave the current room/run and go back to the offline Hall.
    public static void ReturnToHall(string notice = null)
    {
        if (loading) return;
        var flow = Ensure(); flow.StartCoroutine(flow.Return(notice));
    }
    public static bool CanStartRun(out string reason)
    {
        reason = null;
        if (starting) { reason = "Đang bắt đầu."; return false; }
        if (!IsHost || Phase != PNFlowPhase.Lobby) { reason = "Chỉ chủ phòng bắt đầu trong sảnh."; return false; }
        var lobby = PNGameBootstrap.LobbyDriver?.Lobby;
        if (lobby == null) { reason = "Chưa có phòng."; return false; }
        if (!lobby.AllReady) { reason = "Chưa đủ người sẵn sàng."; return false; }
        return true;
    }
    public static void StartRun()
    {
        if (!CanStartRun(out var reason)) { Say(reason); return; }
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
    private IEnumerator Launch(PNFlowMode mode, string address)
    {
        loading = true; message = null; Raise();
        string failure = null;
        var network = PNGameBootstrap.Config.Network; var preparation = PNGameBootstrap.Config.Preparation;
        var end = EndCurrent();
        while (true) { bool more; try { more = end.MoveNext(); } catch (Exception error) { failure = error.Message; break; } if (!more) break; yield return end.Current; }
        if (failure == null)
        {
            var load = LoadScene(mode == PNFlowMode.Offline ? PNScenes.Run : PNScenes.Lobby);
            while (true) { bool more; try { more = load.MoveNext(); } catch (Exception error) { failure = error.Message; break; } if (!more) break; yield return load.Current; }
        }
        if (failure == null)
        {
            try
            {
                var role = mode == PNFlowMode.Offline ? EveSessionRole.Offline : mode == PNFlowMode.Host ? EveSessionRole.Host : EveSessionRole.Client;
                PNGameBootstrap.Begin(new PNPlayConfig(role, "roguelite", string.IsNullOrWhiteSpace(address) ? "localhost" : address.Trim(), network, preparation, true));
            }
            catch (Exception error) { failure = error.Message; }
        }
        double until = Time.realtimeSinceStartupAsDouble + ConnectTimeout;
        while (failure == null && !(EveRegistrySession.IsReady && PNLocalWorldContext.Current?.World != null && (mode == PNFlowMode.Offline || PNGameBootstrap.IsPlaying)))
        {
            if (Time.realtimeSinceStartupAsDouble > until) failure = PNGameBootstrap.LobbyDriver?.LastFailure ?? "Không kết nối được tới phòng.";
            else if (mode != PNFlowMode.Offline && !PNGameBootstrap.IsRoomOpen) failure = PNGameBootstrap.LobbyDriver?.LastFailure ?? "Phòng không khả dụng.";
            else yield return null;
        }
        loading = false;
        if (failure != null) { Debug.LogWarning("Roguelite start failed: " + failure); yield return Return("Không vào được: " + failure); yield break; }
        expectSession = true; Raise();
    }
    private IEnumerator Return(string notice)
    {
        loading = true; Raise();
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
        loading = false; CurrentMode = PNFlowMode.Offline;
        if (failure != null) Debug.LogError("Return to Hall failed: " + failure);
        Say(failure != null ? "Không về được Hall: " + failure : notice);
    }
    private IEnumerator BeginRun()
    {
        starting = true; Raise();
        var driver = PNGameBootstrap.LobbyDriver;
        string failure = null; EveWorldContext run = null; PNGameSession game = null;
        try
        {
            game = PNGameBootstrap.Runtime.GetComponent<PNGameSession>();
            driver.LockAdmissions(true);
            run = game.Worlds.Create(EveId.Parse("base:scene_world"));
        }
        catch (Exception error) { failure = error.Message; }
        double until = Time.realtimeSinceStartupAsDouble + RunTimeout;
        while (failure == null && run.State != EveWorldState.Ready)
        {
            if (run.State == EveWorldState.Failed || run.State == EveWorldState.Unloading || run.State == EveWorldState.Disposed) failure = "Không nạp được world run: " + (run.Failure?.Message ?? run.State.ToString());
            else if (Time.realtimeSinceStartupAsDouble > until) failure = "Nạp world run quá lâu.";
            else yield return null;
        }
        PNWorldTransferReceipt receipt = null;
        if (failure == null)
        {
            try
            {
                var from = PNLocalWorldContext.Current.World;
                var players = game.Session.Entities.Snapshot(from).Where(x => x.IsReady && x.GetComponent<PNPlayerStat>() != null).ToArray();
                if (players.Length == 0) throw new InvalidOperationException("Không có người chơi nào trong sảnh.");
                // A scene world names its slots "chunk-N"; pick the slot that carries the "player" spawn point instead of assuming a name.
                var slot = game.Worlds.Get(run).Slots.FirstOrDefault(x => x.Roots.Any(r => r != null && r.GetComponentsInChildren<PNPlayerSpawnPoint>(true).Any(p => p.SpawnEntry == "player")));
                if (slot == null) throw new InvalidOperationException("World run không có điểm spawn \"player\".");
                receipt = game.Transfers.Request(new PNWorldTransferRequest(Guid.NewGuid(), players, run, slot.Id, "player"));
            }
            catch (Exception error) { failure = error.Message; }
        }
        until = Time.realtimeSinceStartupAsDouble + RunTimeout;
        while (failure == null && !(receipt.IsTerminal && !receipt.CleanupPending))
        {
            if (Time.realtimeSinceStartupAsDouble > until) failure = "Chuyển sang run quá lâu.";
            else yield return null;
        }
        if (failure == null && receipt.State != PNWorldTransferState.Completed) failure = "Chuyển sang run thất bại (" + receipt.State + "): " + receipt.Error;
        if (failure == null) { try { driver.MarkRunStarted(); } catch (Exception error) { failure = error.Message; } }
        if (failure != null)
        {
            Debug.LogWarning("Run start failed: " + failure);
            try { driver.LockAdmissions(false); } catch (Exception error) { Debug.LogException(error); }
            try { if (run != null && game != null && run.State != EveWorldState.Disposed) game.Worlds.Unload(run); } catch (Exception error) { Debug.LogException(error); }
            starting = false; Say("Không bắt đầu được: " + failure); yield break;
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
        string reason = PNGameBootstrap.LobbyDriver?.LastFailure;
        ReturnToHall(CurrentMode == PNFlowMode.Offline ? "Run đã kết thúc." : (string.IsNullOrEmpty(reason) ? "Phòng đã đóng." : "Phòng đã đóng: " + reason));
    }
}
#endif
