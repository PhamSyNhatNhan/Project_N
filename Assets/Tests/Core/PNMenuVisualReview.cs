using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: Opt-in Player review of the menu flow (Docs/ModeMenuPlan.md M3-M7): opens the real screens with fixture modes and characters on an offline
//     session and writes PNG frames (opening, settled, turning, setup, character screen, closing) plus frame times. Not production code;
//     enabled only by -pn-menu-visual-review (+ -pn-menu-visual-output=<directory>). Run the exe in a window (no -batchmode).
// VI: Review Player tùy chọn cho menu: mở các màn thật với chế độ / nhân vật giả và ghi ảnh PNG cùng thời gian khung.
public sealed class PNMenuVisualReview : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!Environment.GetCommandLineArgs().Contains("-pn-menu-visual-review")) return;
        var host = new GameObject("PNMenuVisualReview"); DontDestroyOnLoad(host); host.AddComponent<PNMenuVisualReview>();
    }
    private static string Arg(string name) => Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith(name, StringComparison.Ordinal))?.Substring(name.Length);
    private readonly List<string> trace = new List<string>();

    private IEnumerator Start()
    {
        string output = Arg("-pn-menu-visual-output=") ?? "MenuVisual"; Directory.CreateDirectory(output);
        Exception failure = null;
        yield return Run(output, e => failure = e);
        File.WriteAllText(Path.Combine(output, "result.txt"), (failure == null ? "ok" : failure.ToString()) + Environment.NewLine + string.Join(Environment.NewLine, trace));
        Application.Quit(failure == null ? 0 : 1);
    }

    private static PNListedMode Mode(string id, string name, int[] accent, int order) =>
        new PNListedMode(EveId.Parse(id), new PNModeListing(name, "", accent, "", order, new[] { new PNModeStartOption("solo", "Chơi đơn", PNModeStart.Offline), new PNModeStartOption("host", "Tạo phòng LAN", PNModeStart.Host), new PNModeStartOption("join", "Vào phòng LAN", PNModeStart.Join) }));

    private static PNPlayableEntry Character(string id, string name, string description, int[] accent, params double[] stats)
    {
        string[] ids = { "health", "attack_power", "defense", "attack_speed", "crit_rate", "crit_damage", "cooldown_reduction", "move_speed" };
        var list = new List<EveInitialStat>(); for (int i = 0; i < ids.Length; i++) list.Add(new EveInitialStat(EveId.Parse("base:" + ids[i]), stats[i]));
        var data = new JObject { ["playable"] = new JObject { ["name"] = name, ["description"] = description, ["accent"] = new JArray(accent), ["availability"] = "default" } };
        return new PNPlayableEntry(id, PNPlayableSettings.Parse(data), new EveCharacterDefinition(EveId.Parse(id), list, null, "", data));
    }

    private IEnumerator Run(string output, Action<Exception> fail)
    {
        var runtime = PNGameBootstrap.Runtime;
        if (runtime == null) { fail(new InvalidOperationException("No game runtime.")); yield break; }
        try { runtime.UnloadContent(); } catch (Exception e) { fail(e); yield break; }
        for (int i = 0; i < 300 && EveRegistrySession.State == EveSessionState.Stopping; i++) yield return null;
        try { runtime.EnsureContentLoaded(); runtime.BeginSession(EveSessionRole.Offline); } catch (Exception e) { fail(e); yield break; }
        for (int i = 0; i < 600 && !EveRegistrySession.IsReady; i++) yield return null;
        if (!EveRegistrySession.IsReady) { fail(new InvalidOperationException("Session not ready.")); yield break; }
        PNUiRuntime ui = null;
        for (int i = 0; i < 300; i++) { ui = runtime.GetComponent<PNUiRuntime>(); if (ui != null && ui.MenuFlow != null) break; yield return null; }
        if (ui == null || ui.MenuFlow == null) { fail(new InvalidOperationException("No UI runtime.")); yield break; }
        Time.captureFramerate = 0;
        var camera = new GameObject("review camera").AddComponent<Camera>(); camera.tag = "MainCamera"; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.2f, .25f, .3f);
        for (int i = 0; i < 20; i++) yield return null;
        var flow = ui.MenuFlow; int shot = 0;
        // Waits in real time (the menu runs on unscaled delta time): n "frames" = n / 60 seconds.
        IEnumerator Frames(int count) { float until = Time.realtimeSinceStartup + count / 60f; while (Time.realtimeSinceStartup < until) yield return null; }
        IEnumerator Shot(string label)
        {
            yield return new WaitForEndOfFrame();
            var screen = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(output, (shot++).ToString("00") + "_" + label + ".png"), screen.EncodeToPNG()); Destroy(screen);
            trace.Add(label + ": " + flow.DebugState + " dt=" + Time.unscaledDeltaTime.ToString("0.0000") + " depth=" + flow.Depth + " open=" + flow.IsOpen + " timeScale=" + Time.timeScale);
        }
        IEnumerator Cost(string label, int frames)
        {
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            for (int i = 0; i < 10; i++) yield return null;
            float total = 0f, worst = 0f; for (int i = 0; i < frames; i++) { yield return null; float dt = Time.unscaledDeltaTime; total += dt; if (dt > worst) worst = dt; }
            trace.Add("cost " + label + ": avg " + (total / frames * 1000f).ToString("0.00") + " ms, worst " + (worst * 1000f).ToString("0.00") + " ms");
            Time.captureFramerate = 0;
        }

        var modes = new List<PNListedMode>
        {
            Mode("fixture:rogue", "Roguelite", new[] { 231, 185, 143 }, 0), Mode("fixture:arena", "Đấu trường", new[] { 157, 182, 201 }, 1), Mode("fixture:sand", "Thử nghiệm", new[] { 170, 190, 170 }, 2),
            Mode("fixture:mods", "Mod khác", new[] { 176, 168, 206 }, 3), Mode("fixture:daily", "Thử thách", new[] { 150, 176, 196 }, 4), Mode("fixture:survive", "Sinh tồn", new[] { 170, 196, 170 }, 5),
        };
        PNModeScreen ring = null; PNListedMode picked = null;
        ring = new PNModeScreen(modes, m => picked = m);
        if (!flow.Open(ring)) { fail(new InvalidOperationException("Flow refused to open.")); yield break; }
        yield return Frames(10); yield return Shot("ring_opening_a");
        yield return Frames(20); yield return Shot("ring_opening_b");
        yield return Frames(110); yield return Shot("ring_settled");
        yield return Cost("ring (6 gates)", 120);
        ring.Turn(2); yield return Frames(6); yield return Shot("ring_turning"); yield return Frames(60); yield return Shot("ring_after_turn");

        flow.Push(new PNSetupScreen(modes[0], (m, s) => { }));
        yield return Frames(40); yield return Shot("setup_entering"); yield return Frames(80); yield return Shot("setup");

        var characters = new List<PNPlayableEntry>
        {
            Character("fixture:eve", "Eve", "Kiếm sĩ nhanh nhẹn, lướt qua kẻ địch và tung loạt đạn tự dẫn.", new[] { 231, 185, 143 }, 100, 10, 5, 100, 20, 150, 0, 3),
            Character("fixture:vex", "Vex", "Nhân vật do mod thêm vào. Chịu đòn tốt, tầm đánh ngắn.", new[] { 157, 182, 201 }, 140, 8, 9, 90, 10, 150, 0, 2.6),
            Character("fixture:mira", "Mira", "Pháp sư tầm xa, đòn chậm nhưng mạnh.", new[] { 176, 168, 206 }, 80, 14, 3, 85, 15, 170, 10, 2.8),
            Character("fixture:ren", "Ren", "Sát thủ, đòn chí mạng cao.", new[] { 200, 150, 150 }, 90, 11, 4, 115, 35, 190, 0, 3.4),
            Character("fixture:kai", "Kai", "Chiến binh giáp nặng.", new[] { 170, 190, 170 }, 160, 7, 12, 80, 5, 140, 0, 2.4),
            Character("fixture:zoe", "Zoe", "Hỗ trợ, hồi chiêu nhanh.", new[] { 220, 196, 150 }, 95, 8, 5, 100, 10, 150, 25, 3.1),
            Character("fixture:lux", "Lux", "Xạ thủ linh hoạt.", new[] { 150, 196, 196 }, 85, 12, 4, 110, 25, 160, 5, 3.2),
        };
        PNCharacterScreen character = null;
        character = new PNCharacterScreen(characters, "fixture:eve", () => new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("Bạn", "đang chọn"), new KeyValuePair<string, string>("Người chơi 1", "đã chọn") }, e => trace.Add("continue " + e.Id));
        flow.Push(character);
        yield return Frames(40); yield return Shot("character_entering"); yield return Frames(120); yield return Shot("character_eve");
        yield return Cost("character screen", 120);
        character.Select(2, true); yield return Frames(8); yield return Shot("character_switching"); yield return Frames(60); yield return Shot("character_mira");

        flow.Close(); yield return Frames(12); yield return Shot("closing_a"); yield return Frames(24); yield return Shot("closing_b");
        for (int i = 0; i < 90 && flow.IsOpen; i++) yield return null;
        trace.Add("closed=" + !flow.IsOpen + " timeScale=" + Time.timeScale + " lock=" + flow.InputLocked);
        Destroy(camera.gameObject);
    }
}
