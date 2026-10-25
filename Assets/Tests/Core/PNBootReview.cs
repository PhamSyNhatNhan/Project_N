using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// Player review of the boot (Docs/LoadingHandoffPlan.md P6/P7): from the first frame it records what is on screen every frame (loading cover or scene),
// the active scene, the boot and handoff state and how many transition-screen documents are alive, and saves the frame whenever any of it changes.
// Enabled only by -pn-boot-review=<directory>. Options: -pn-boot-review-click (press the Title's Start itself), -pn-boot-review-fail (the first boot
// fails, then the retry is pressed), -pn-boot-review-quit (press Quit), -pn-boot-review-hold=<s> (seconds to keep recording after the Hall is ready).
// Run the exe in a window.
public sealed class PNBootReview : MonoBehaviour
{
    private static string Arg(string name) => Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith(name, StringComparison.Ordinal))?.Substring(name.Length);
    private static bool Has(string name) => Environment.GetCommandLineArgs().Contains(name);
    private string output; private readonly StringBuilder trace = new StringBuilder(); private int shot, frame; private string last = "";
    private int errors; private readonly List<string> errorLines = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (Arg("-pn-boot-review=") == null) return;
        var go = new GameObject("PNBootReview"); DontDestroyOnLoad(go); var self = go.AddComponent<PNBootReview>();
        Application.logMessageReceived += (c, s, t) => { if (t == LogType.Error || t == LogType.Exception) { self.errors++; if (self.errorLines.Count < 5) self.errorLines.Add(t + ": " + c); } };
    }

    private void Log(string line) { trace.AppendLine(Time.realtimeSinceStartup.ToString("0.00") + "s f" + frame + " " + line); }
    private IEnumerator Start()
    {
        output = Arg("-pn-boot-review="); Directory.CreateDirectory(output); Application.runInBackground = true;
        StartCoroutine(Record());
        bool fail = Has("-pn-boot-review-fail"), click = Has("-pn-boot-review-click") || fail, quit = Has("-pn-boot-review-quit");
        float until = Time.realtimeSinceStartup + 20;
        UIDocument title = null; while (Time.realtimeSinceStartup < until && (title = FindTitleDocument()) == null) yield return null;
        Log("title document " + (title != null ? "found" : "NOT found") + " in scene " + SceneManager.GetActiveScene().name);
        yield return new WaitForSeconds(1.5f);
        float hold = float.TryParse(Arg("-pn-boot-review-hold="), out var hv) ? hv : 3;
        if (title != null && !click && !quit && PNGameBootstrap.AutoStart == null && PNGameBootstrap.PendingTarget == null)
        { Log("waiting at the Title (no click, no auto-start)"); yield return Shot("title"); yield return new WaitForSeconds(Mathf.Max(2, hold)); Finish("title-only"); yield break; }
        if (quit && title != null) { yield return Shot("title"); Press(title, "pn-title-quit"); Log("pressed Quit"); yield return new WaitForSeconds(4f); Finish("quit-pressed-and-still-running"); yield break; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (fail) { PNGameFlow.DebugFailNextBoot = true; Log("test hook: the next boot will fail"); }
#else
        if (fail) Log("the fail hook exists only in Editor and Development builds; ignored");
#endif
        if (click && title != null)
        {
            Press(title, "pn-title-start"); Log("pressed Start");
            if (fail)
            {
                float wait = Time.realtimeSinceStartup + 15; while (Time.realtimeSinceStartup < wait && PNGameFlow.BootFailure == null) yield return null;
                Log("boot failure reported: " + (PNGameFlow.BootFailure ?? "NONE") + " booting=" + PNGameFlow.Booting + " busy=" + PNGameFlow.Busy);
                yield return new WaitForSeconds(1f); yield return Shot("title-error");
                var doc = FindTitleDocument(); var label = doc?.rootVisualElement.Q<Label>("pn-title-error"); var startButton = doc?.rootVisualElement.Q<Button>("pn-title-start");
                Log("error line visible=" + (label != null && label.resolvedStyle.display == DisplayStyle.Flex) + " text='" + label?.text + "' button='" + startButton?.text + "' enabled=" + (startButton != null && startButton.enabledInHierarchy));
                if (doc != null) { Press(doc, "pn-title-start"); Log("pressed the retry"); }
            }
        }
        float limit = Time.realtimeSinceStartup + 120; bool ready = false;
        while (Time.realtimeSinceStartup < limit)
        {
            var want = (Arg("-pn-boot-review-expect=") ?? "hall") == "run" ? PNFlowPhase.Run : (Arg("-pn-boot-review-expect=") == "lobby" ? PNFlowPhase.Lobby : PNFlowPhase.Hall);
            ready = EveRegistrySession.IsReady && PNGameFlow.Phase == want && PNLocalWorldContext.Current?.Actor != null && PNLocalWorldContext.Current.Actor.IsReady;
            if (ready) break; yield return null;
        }
        Log("hall ready=" + ready + " booted=" + PNGameBootstrap.Booted + " scene=" + SceneManager.GetActiveScene().name);
        yield return new WaitForSeconds(hold); yield return Shot("hall");
        Finish(ready ? "hall-ready" : "hall-NOT-ready");
    }
    private static UIDocument FindTitleDocument() => FindObjectsByType<UIDocument>(FindObjectsInactive.Include).FirstOrDefault(d => d.rootVisualElement != null && d.rootVisualElement.Q("pn-title-column") != null);
    private static void Press(UIDocument doc, string name)
    {
        var button = doc.rootVisualElement.Q<Button>(name); if (button == null) return;
        using (var e = NavigationSubmitEvent.GetPooled()) { e.target = button; button.SendEvent(e); }
    }
    private IEnumerator Shot(string label)
    {
        yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(output, (shot++).ToString("00") + "_" + label + ".png"), tex.EncodeToPNG()); Destroy(tex);
    }
    private IEnumerator Record()
    {
        while (true)
        {
            yield return new WaitForEndOfFrame(); frame++;
            Texture2D tex = null; int blue = 0, n = 0; long r = 0, g = 0, b = 0;
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture(); var px = tex.GetPixels32();
                for (int y = 0; y < tex.height; y += 40) for (int x = 0; x < tex.width; x += 40) { var c = px[y * tex.width + x]; n++; r += c.r; g += c.g; b += c.b; if (c.b > 150 && c.r < 80 && c.g < 120) blue++; }
            }
            catch (Exception) { }
            string cls = n == 0 ? "?" : blue * 10 > n * 8 ? "BLUE" : "scene";
            int screens = FindObjectsByType<UIDocument>(FindObjectsInactive.Include).Count(d => d.name.Contains("Transition") || d.name.Contains("Loading Cover"));
            string key = cls + " scene=" + SceneManager.GetActiveScene().name + " booted=" + PNGameBootstrap.Booted + " booting=" + PNGameFlow.Booting + " busy=" + PNGameFlow.Busy + " phase=" + PNGameFlow.Phase + " ready=" + EveRegistrySession.IsReady + " handoff=" + PNTransitionHandoffHost.State + "/" + PNTransitionHandoffHost.Raises + "r" + PNTransitionHandoffHost.Adoptions + "a" + PNTransitionHandoffHost.Abandons + "x" + " screens=" + screens;
            if (key != last && tex != null)
            {
                last = key; Log("frame " + frame + ": " + key + (n > 0 ? " avg=(" + r / n + "," + g / n + "," + b / n + ")" : ""));
                if (frame < 4000) File.WriteAllBytes(Path.Combine(output, "f" + frame.ToString("0000") + "_" + cls + ".png"), tex.EncodeToPNG());
            }
            if (tex != null) Destroy(tex);
        }
    }
    private void Finish(string result)
    {
        Log("RESULT " + result + " errors=" + errors + (errorLines.Count > 0 ? " first=" + string.Join(" || ", errorLines) : ""));
        File.WriteAllText(Path.Combine(output, "result.txt"), trace.ToString());
        Application.Quit(0);
    }
}
