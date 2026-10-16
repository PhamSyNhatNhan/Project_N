#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Runs only on the disposable ErMode copy selected as playModeStartScene.
public sealed class PNBootstrapPlayChecks : MonoBehaviour
{
    private int checks;
    private bool early;
    private void Awake() { early = PNGameBootstrap.Runtime != null && PNGameBootstrap.Runtime.IsContentReady; }
    private void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    private IEnumerator Start()
    {
        yield return null;
        Keyboard keyboard = null;
        PNPlayerMove move = null;
        Vector3 start = default;
        try
        {
            move = FindAnyObjectByType<PNPlayerMove>();
            Check(move != null && move.CanInput, "Offline player not input-ready.");
            start = move.transform.position;
            keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
        }
        catch (Exception error)
        {
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            File.WriteAllText(".utmp/P1S3/PlayResult.txt", "FAIL " + error); Debug.LogException(error);
            EditorApplication.isPlaying = false; yield break;
        }
        // Actual Update + FixedUpdate, with disposable synthetic keyboard state.
        yield return new WaitForSecondsRealtime(.2f);
        try
        {
            Check(move.transform.position.x > start.x + .01f, "Offline keyboard input did not move Eve in native Play.");
            InputSystem.RemoveDevice(keyboard); keyboard = null;
            Run();
        }
        catch (Exception error) { File.WriteAllText(".utmp/P1S3/PlayResult.txt", "FAIL " + error); Debug.LogException(error); }
        finally { if (keyboard != null) InputSystem.RemoveDevice(keyboard); EditorApplication.isPlaying = false; }
    }
    private void Run()
    {
        var runtime = PNGameBootstrap.Runtime;
        Check(early, "Content was not loaded before scene Awake.");
        Check(runtime.IsReady && EveRegistrySession.Context.Role == EveSessionRole.Offline, "ErMode entry did not start Offline.");
        var content = runtime.Content;
        var first = EveRegistrySession.Context;
        Check(first.Entities.Count >= 2, "Authored Eve/Dummy failed to load.");
        var ui = runtime.GetComponent<PNUiRuntime>();
        var areas = runtime.GetComponent<PNAreaManager>();
        Check(ui != null && ui.HudRoot != null && areas.Session == first, "Game services were not bound.");
        ui.RefreshUI();
        var moves = FindObjectsByType<PNPlayerMove>();
        Check(moves.Length > 0 && moves.All(x => x.CanInput), "Authored Eve input did not become available.");
        ui.OpenMenu();
        Check(ui.MenuOpen && Time.timeScale == 0, "Offline pause did not acquire time.");
        Check(moves.All(x => x.MenuInputBlocked && !x.CanInput), "Pause did not block player input.");
        PNGameBootstrap.End();
        Check(!first.IsActive && ui.HudRoot == null && !ui.MenuOpen && Time.timeScale == 1 && areas.Session == null, "End retained UI/pause/Area.");
        Check(moves.All(x => !x.MenuInputBlocked && !x.CanInput), "End retained menu gate or allowed inactive-session input.");
        // Same authored objects are explicit fixture reuse; S6 will own factory identity enrollment.
        foreach (var loader in FindObjectsByType<EveEntityLoader>(FindObjectsInactive.Include))
            loader.GetComponent<EveEntityIdentity>().ResetIdentity();
        for (int i = 0; i < 2; i++)
        {
            PNGameBootstrap.Begin(new PNPlayConfig());
            Check(ReferenceEquals(content, runtime.Content) && EveRegistrySession.Context.SessionId != first.SessionId, "New play rebuilt content or reused context.");
            Check(runtime.GetComponents<PNUiRuntime>().Length == 1 && runtime.GetComponents<PNAreaManager>().Length == 1 && ui.HudRoot != null, "Duplicate or unbound services.");
            Check(EveRegistrySession.Context.Entities.Count >= 2, "Ready did not reload authored entities.");
            PNGameBootstrap.End();
            foreach (var loader in FindObjectsByType<EveEntityLoader>(FindObjectsInactive.Include))
                loader.GetComponent<EveEntityIdentity>().ResetIdentity();
        }
        // A new scene entry after a completed play must reuse the persistent content owner.
        var scene = SceneManager.CreateScene("S3 disposable transition");
        var entry = new GameObject("S3 scene entry"); entry.SetActive(false); SceneManager.MoveGameObjectToScene(entry, scene);
        entry.AddComponent<EveGameRuntime>(); entry.SetActive(true);
        Check(runtime.IsReady && ReferenceEquals(content, runtime.Content), "Scene entry took over content after End.");
        PNGameBootstrap.End();
        bool rejected = false;
        try { PNGameBootstrap.Begin(new PNPlayConfig(EveSessionRole.Host)); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && !runtime.IsReady && runtime.IsContentReady, "Missing network driver fell back to Offline.");
        File.WriteAllText(".utmp/P1S3/PlayResult.txt", "PASS " + checks + " bootstrap/ErMode/service checks; domainReload=" + !EditorSettings.enterPlayModeOptions.HasFlag(EnterPlayModeOptions.DisableDomainReload));
    }
}
#endif
