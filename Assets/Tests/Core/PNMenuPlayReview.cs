using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;

// EN: Opt-in Player review of the real menu flow on the Hall/lobby/run Flow Player (CameraFlowBuild): from the Hall it opens the menu, picks the mode,
//     the start option and the character exactly as a player would (through the screens' own entry points), and checks what the game does next.
//       -pn-menu-play-review=solo | host | join   (+ -pn-menu-play-address=<host> for join, -pn-menu-play-peers=<n> for host, -pn-menu-play-output=<dir>)
//     Not production code. Run the exe in a window (no -batchmode). Results: <dir>/result.txt (+ PNG frames).
// VI: Review Player tùy chọn cho luồng menu thật: từ Hall mở menu, chọn chế độ / cách chơi / nhân vật rồi kiểm game làm gì tiếp.
public sealed class PNMenuPlayReview : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Arg("-pn-menu-play-review=") == null) return;
        var host = new GameObject("PNMenuPlayReview"); DontDestroyOnLoad(host); host.AddComponent<PNMenuPlayReview>();
    }
    private static string Arg(string name) => Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith(name, StringComparison.Ordinal))?.Substring(name.Length);
    private readonly List<string> trace = new List<string>();
    private readonly List<string> errors = new List<string>();
    private string output; private int shot;

    private void Log(string line) { trace.Add(Time.realtimeSinceStartup.ToString("0.0") + "s " + line); }
    // Every frame at the end of the frame: is the screen the blue loading cover or the scene, and what the flow says; a line is logged (and the frame
    // saved) whenever any of it changes.
    private IEnumerator FrameTimeline()
    {
        string last = ""; int frame = 0; float end = Time.realtimeSinceStartup + 14f;
        while (Time.realtimeSinceStartup < end)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture(); var px = tex.GetPixels32(); int blue = 0, n = 0; long r = 0, g = 0, b = 0;
            for (int y = 0; y < tex.height; y += 40) for (int x = 0; x < tex.width; x += 40) { var c = px[y * tex.width + x]; n++; r += c.r; g += c.g; b += c.b; if (c.b > 150 && c.r < 80 && c.g < 120) blue++; }
            string cls = blue * 10 > n * 8 ? "BLUE" : "scene";
            bool ui = PNGameBootstrap.Runtime != null && PNGameBootstrap.Runtime.GetComponent<PNUiRuntime>() != null;
            string key = cls + " phase=" + PNGameFlow.Phase + " busy=" + PNGameFlow.Busy + " ready=" + EveRegistrySession.IsReady + " ui=" + ui;
            if (key != last)
            {
                last = key; Log("frame " + frame + ": " + key + " avg=(" + (r / n) + "," + (g / n) + "," + (b / n) + ")");
                File.WriteAllBytes(Path.Combine(output, "f" + frame.ToString("0000") + "_" + cls + ".png"), tex.EncodeToPNG());
            }
            Destroy(tex); frame++;
        }
    }
    private IEnumerator Shot(string label)
    {
        yield return new WaitForEndOfFrame();
        var screen = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(output, (shot++).ToString("00") + "_" + label + ".png"), screen.EncodeToPNG()); Destroy(screen);
    }
    private static IEnumerator Seconds(float s) { float until = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < until) yield return null; }

    private IEnumerator Start()
    {
        string kind = Arg("-pn-menu-play-review="); output = Arg("-pn-menu-play-output=") ?? "MenuPlay_" + kind; Directory.CreateDirectory(output);
        Application.logMessageReceived += OnLog;
        Exception failure = null;
        yield return Run(kind, e => failure = e);
        Application.logMessageReceived -= OnLog;
        trace.Add("errors/exceptions logged: " + errors.Count); trace.AddRange(errors.Take(10));
        File.WriteAllText(Path.Combine(output, "result.txt"), (failure == null ? "ok" : failure.ToString()) + Environment.NewLine + string.Join(Environment.NewLine, trace));
        Application.Quit(failure == null && errors.Count == 0 ? 0 : 1);
    }
    private void OnLog(string message, string stack, LogType type)
    { if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) errors.Add(type + ": " + message.Split('\n')[0]); }

    private IEnumerator Until(string what, Func<bool> condition, float timeout, Action<Exception> fail)
    {
        float start = Time.realtimeSinceStartup;
        while (!condition()) { if (Time.realtimeSinceStartup - start > timeout) { fail(new TimeoutException("Timed out waiting for: " + what)); yield break; } yield return null; }
        Log("reached: " + what + " after " + (Time.realtimeSinceStartup - start).ToString("0.0") + "s");
    }

    private IEnumerator Run(string kind, Action<Exception> fail)
    {
        bool failed = false; Action<Exception> Fail = e => { failed = true; fail(e); };
        PNUiRuntime ui = null;
        yield return Until("Hall ready with UI", () =>
        {
            if (!EveRegistrySession.IsReady || PNGameFlow.Phase != PNFlowPhase.Hall || PNLocalWorldContext.Current?.World == null) return false;
            ui = PNGameBootstrap.Runtime == null ? null : PNGameBootstrap.Runtime.GetComponent<PNUiRuntime>();
            return ui != null && ui.MenuFlow != null;
        }, 90, Fail);
        if (failed) yield break;
        yield return Seconds(1f);
        var flow = ui.MenuFlow; var controller = new PNMenuController(ui);
        Log("kind=" + kind + " phase=" + PNGameFlow.Phase + " timeScale=" + Time.timeScale);
        yield return Shot("hall");
        if (!controller.OpenHall()) { Fail(new InvalidOperationException("OpenHall refused.")); yield break; }
        yield return Until("mode ring shown", () => flow.Current is PNModeScreen && !flow.IsBusy, 10, Fail); if (failed) yield break;
        Log("menu open: MenuOpen=" + ui.MenuOpen + " lock=" + flow.InputLocked + " timeScale=" + Time.timeScale);
        yield return Shot("mode");
        ((PNModeScreen)flow.Current).Pick();
        yield return Until("setup shown", () => flow.Current is PNSetupScreen && !flow.IsBusy, 10, Fail); if (failed) yield break;
        yield return Shot("setup");
        int start = kind == "solo" ? 0 : kind == "host" ? 1 : 2;
        ((PNSetupScreen)flow.Current).Choose(start);
        if (kind == "join")
        {
            yield return Until("room screen shown", () => flow.Current is PNRoomScreen && !flow.IsBusy, 10, Fail); if (failed) yield break;
            yield return Shot("rooms");
            ((PNRoomScreen)flow.Current).Join(Arg("-pn-menu-play-address=") ?? "localhost");
        }
        yield return Until("character screen shown", () => flow.Current is PNCharacterScreen && !flow.IsBusy, 10, Fail); if (failed) yield break;
        var character = (PNCharacterScreen)flow.Current;
        Log("character screen: selected=" + character.Selected?.Id + " of " + "list");
        yield return Seconds(1f); yield return Shot("character");
        // -pn-menu-play-frames: log what is on screen every frame from the press until the next scene has settled (investigation of the scene change).
        if (Arg("-pn-menu-play-frames") != null) StartCoroutine(FrameTimeline());
        character.Continue();
        yield return Until("menu closed", () => !flow.IsOpen, 5, Fail); if (failed) yield break;
        Log("menu closed: lock=" + flow.InputLocked + " timeScale=" + Time.timeScale + " MenuOpen=" + ui.MenuOpen);
        string profile = Path.Combine(Application.persistentDataPath, "Profile", "profile.json");
        Log("profile file exists=" + File.Exists(profile) + (File.Exists(profile) ? " content=" + File.ReadAllText(profile).Replace("\n", " ").Replace("\r", "") : ""));

        if (kind == "solo")
        {
            yield return Until("run started (phase Run, session ready)", () => PNGameFlow.Phase == PNFlowPhase.Run && EveRegistrySession.IsReady && !PNGameFlow.Busy, 60, Fail); if (failed) yield break;
            yield return Seconds(3f); yield return Shot("run");
            Log("run: role=" + EveRegistrySession.Context.Role + " timeScale=" + Time.timeScale + " players=" + EveRegistrySession.Context.Entities.Snapshot().Count(x => x.GetComponent<PNPlayerStat>() != null));
            yield break;
        }
        yield return Until("lobby (room open, session ready)", () => PNGameFlow.Phase == PNFlowPhase.Lobby && EveRegistrySession.IsReady && PNGameBootstrap.LobbyDriver?.Lobby != null && !PNGameFlow.Busy, 60, Fail); if (failed) yield break;
        var driver = PNGameBootstrap.LobbyDriver;
        yield return Until("local peer shows the picked character", () => driver.Lobby != null && driver.Lobby.Peers.Any(p => p.Local && !string.IsNullOrEmpty(p.Character)), 20, Fail); if (failed) yield break;
        Log("lobby: role=" + EveRegistrySession.Context.Role + " peers=" + string.Join(", ", driver.Lobby.Peers.Select(p => p.Label + (p.Local ? "(local)" : "") + " [" + p.Character + "] ready=" + p.Ready)));
        yield return Shot("lobby");
        // -pn-menu-play-pick=<id>: change the character in the lobby and see the actor respawn as it, at the same place.
        string pick = Arg("-pn-menu-play-pick=");
        if (!string.IsNullOrEmpty(pick))
        {
            long me = kind == "host" ? 0 : EveRegistrySession.Context.LocalOwnerId.GetValueOrDefault(-1);
            Func<System.Collections.Generic.List<EveEntityLoader>> mine = () => EveRegistrySession.Context.Entities.Snapshot().Where(x => x.GetComponent<PNPlayerStat>() != null && x.GetComponent<EveEntityIdentity>().OwnerId == me && x.gameObject.activeInHierarchy).ToList();
            yield return Until("own player actor present", () => mine().Count == 1, 30, Fail); if (failed) yield break;
            Vector3 before = mine()[0].transform.position; string was = mine()[0].GetComponent<PNStat>().CharacterDefinitionId;
            Log("pick: before=" + was + " at " + before.ToString("F2"));
            PNPlayerProfileService.SetLastCharacter(pick);
            yield return Until("own actor is the picked character", () => { var m = mine(); return m.Count == 1 && m[0].GetComponent<PNStat>().CharacterDefinitionId == pick; }, 30, Fail); if (failed) yield break;
            yield return Seconds(1f);
            var after = mine()[0];
            Log("pick: after=" + after.GetComponent<PNStat>().CharacterDefinitionId + " at " + after.transform.position.ToString("F2") + " moved=" + Vector3.Distance(before, after.transform.position).ToString("F3") + " actors=" + mine().Count + " sprite=" + after.GetComponentInChildren<SpriteRenderer>()?.color);
            yield return Shot("lobby_changed");
        }
        if (kind == "host")
        {
            int want = int.TryParse(Arg("-pn-menu-play-peers="), out var n) ? n : 0;
            if (want > 0)
            {
                yield return Until("a peer joined with its character", () => driver.Lobby.Peers.Count(p => !p.Host && !string.IsNullOrEmpty(p.Character)) >= want, 120, Fail); if (failed) yield break;
                Log("peers: " + string.Join(", ", driver.Lobby.Peers.Select(p => p.Label + " [" + p.Character + "] ready=" + p.Ready)));
                yield return Until("everyone ready", () => driver.Lobby.AllReady, 60, Fail); if (failed) yield break;
                PNGameFlow.StartRun();
                yield return Until("run started", () => PNGameFlow.Phase == PNFlowPhase.Run && EveRegistrySession.IsReady && !PNGameFlow.Busy, 60, Fail); if (failed) yield break;
                float seen = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - seen < 10f && EveRegistrySession.Context.Entities.Snapshot().Count(x => x.GetComponent<PNPlayerStat>() != null) < 2) yield return null;
                yield return Seconds(1f); yield return Shot("run");
                Log("run (host): players=" + EveRegistrySession.Context.Entities.Snapshot().Count(x => x.GetComponent<PNPlayerStat>() != null));
                yield return Seconds(6f);
            }
            else yield return Seconds(3f);
            yield break;
        }
        // join: say ready, then wait for the host to start the run
        driver.SetReady(true); yield return Seconds(1f); yield return Shot("ready");
        yield return Until("run started (client)", () => PNGameFlow.Phase == PNFlowPhase.Run && EveRegistrySession.IsReady && !PNGameFlow.Busy, 120, Fail); if (failed) yield break;
        yield return Seconds(8f); yield return Shot("run");
        Log("run (client): players=" + EveRegistrySession.Context.Entities.Snapshot().Count(x => x.GetComponent<PNPlayerStat>() != null));
    }
}
