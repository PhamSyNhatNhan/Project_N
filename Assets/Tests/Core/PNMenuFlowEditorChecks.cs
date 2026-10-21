#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEditor.SceneManagement;

// Native checks for the menu frame (PNMenuFlow / PNMenuLock / corner buttons) on a disposable offline session; time is driven by Advance.
internal sealed class PNMenuTestScreen : IPNMenuScreen
{
    public readonly VisualElement RootElement = new VisualElement();
    private readonly List<VisualElement> items = new List<VisualElement>();
    public bool Disposed, VetoBack;
    public int Entered_, Ticks, BackRequests, Animated;
    public PNMenuTestScreen(int count = 3, bool backdrop = true) { UsesBackdrop = backdrop; for (int i = 0; i < count; i++) { var item = new VisualElement(); items.Add(item); RootElement.Add(item); } }
    public VisualElement Root => RootElement;
    public IReadOnlyList<VisualElement> Items => items;
    public bool UsesBackdrop { get; }
    public Color Tint => Color.white;
    public bool HandleBack() { BackRequests++; return VetoBack; }
    public void Tick(float dt) { Ticks++; }
    public void Entered() { Entered_++; }
    public void Animate(bool entering, float elapsed) { Animated++; }
    public void Dispose() { Disposed = true; }
}

public static class PNMenuFlowEditorChecks
{
    public static string Run()
    {
        if (Application.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Edit required.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty; float time = Time.timeScale;
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Menu flow disposable runtime"); SceneManager.MoveGameObjectToScene(root, scene);
        var runtime = root.AddComponent<PNS3NativeRuntime>();
        int checks = 0; void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
        try
        {
            runtime.EnsureContentLoaded();
            runtime.BeginSession(EveSessionRole.Offline); var session = EveRegistrySession.Context;
            PNUiRuntime.Attach(runtime); var ui = root.GetComponent<PNUiRuntime>(); session.RegisterCleanup(ui.Unbind);
            var flow = ui.MenuFlow;
            void Run(float seconds) { for (float t = 0; t < seconds; t += .05f) flow.Advance(.05f); }
            Check(flow != null && !flow.IsOpen && !ui.MenuOpen && !flow.InputLocked, "flow exists and starts closed");

            // ---- open ----
            var a = new PNMenuTestScreen(3);
            Check(flow.Open(a) && flow.IsOpen && ui.MenuOpen && ui.FlowOpen && !ui.PauseOpen, "open makes the menu open without touching pause");
            Check(flow.InputLocked && Time.timeScale == 0, "open holds the input lock and freezes offline time");
            var extra = new PNMenuTestScreen(); Check(!flow.Open(extra) && extra.Disposed && !a.Disposed, "a second open is refused and its screen disposed");
            Check(a.Items[0].resolvedStyle.opacity < 1 || a.Items[0].style.opacity.value == 0, "items start hidden");
            Run(.2f); Check(!a.Disposed && a.Entered_ == 0, "still entering after 0.2 s (first-open delay 0.26 s)");
            Run(PNMenuMotion.EnterTotal(3, PNMenuMotion.FirstEnterDelay) + .1f);
            Check(a.Entered_ == 1 && a.Items[2].style.opacity.value == 1 && a.Items[2].style.translate.value.y.value == 0, "entered: items fully visible and in place");
            Check(a.Ticks > 0, "the current screen is ticked");

            // ---- stack ----
            var b = new PNMenuTestScreen(2, false); flow.Push(b);
            Run(PNMenuMotion.ExitTotal(3) + .05f);
            Check(flow.Depth == 2 && flow.Current == b && a.Root.style.display == DisplayStyle.None, "push swaps screens after the old one leaves");
            Run(PNMenuMotion.EnterTotal(2, 0) + .1f); Check(b.Entered_ == 1, "pushed screen enters without the first-open delay");
            b.VetoBack = true; flow.RequestBack(); Run(1f);
            Check(b.BackRequests == 1 && flow.Depth == 2 && flow.Current == b, "a screen can consume back");
            b.VetoBack = false; flow.RequestBack(); Run(PNMenuMotion.ExitTotal(2) + .05f);
            Check(flow.Depth == 1 && flow.Current == a && b.Disposed && !a.Disposed, "back pops and disposes only the popped screen");
            Run(PNMenuMotion.EnterTotal(3, 0) + .1f); Check(a.Entered_ == 2, "the screen below enters again");
            var c = new PNMenuTestScreen(1); flow.Replace(c); Run(PNMenuMotion.ExitTotal(3) + PNMenuMotion.EnterTotal(1, 0) + .1f);
            Check(flow.Depth == 1 && flow.Current == c && a.Disposed && c.Entered_ == 1, "replace swaps the current screen and disposes the old one");

            // ---- close ----
            bool closed = false; flow.Closed += () => closed = true;
            flow.RequestBack();
            Check(flow.IsClosing && flow.InputLocked, "back on the first screen starts closing; the lock is held during the exit animation");
            Run(PNMenuMotion.CloseTotal - .1f); Check(flow.IsOpen && !closed, "still closing before 0.58 s");
            Run(.2f);
            Check(!flow.IsOpen && closed && !ui.MenuOpen && !flow.InputLocked && Time.timeScale == time && c.Disposed && flow.Depth == 0, "closed: lock released, time restored, screens disposed");

            // ---- pause exclusion ----
            ui.OpenMenu(); Check(ui.PauseOpen && ui.MenuOpen, "pause opens as before");
            var d = new PNMenuTestScreen(); Check(!flow.Open(d) && d.Disposed && !ui.FlowOpen, "the flow does not open over the pause menu");

            // ---- session end while open ----
        }
        finally
        {
            if (EveRegistrySession.State == EveSessionState.Stopping) EveRegistrySession.RetryCleanup();
            runtime.UnloadContent(); root.GetComponent<PNUiRuntime>()?.Unbind(); UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene);
        }
        Check(original.isDirty == dirty && SceneManager.GetActiveScene() == original && Time.timeScale == time, "original scene and time preserved");
        return "PASS " + checks + " menu flow checks (frame, stack, motion timing, lock, pause exclusion); dirty " + dirty + " -> " + original.isDirty;
    }

    // The real screens on a real session (no layout in Edit Mode: logic only): mode ring, setup, character screen and the controller's order.
    public static string RunScreens()
    {
        if (Application.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Edit required.");
        float time = Time.timeScale;
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Menu screens disposable runtime"); SceneManager.MoveGameObjectToScene(root, scene);
        var runtime = root.AddComponent<PNS3NativeRuntime>();
        var folder = System.IO.Path.Combine(Application.temporaryCachePath, "PNMenuScreens_" + Guid.NewGuid().ToString("N"));
        int checks = 0; void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
        try
        {
            PNPlayerProfileService.ResetStatics(); PNPlayerProfileService.Load(new PNProfileStore(folder));
            runtime.EnsureContentLoaded(); runtime.BeginSession(EveSessionRole.Offline);
            PNUiRuntime.Attach(runtime); var ui = root.GetComponent<PNUiRuntime>(); EveRegistrySession.Context.RegisterCleanup(ui.Unbind);
            var flow = ui.MenuFlow;
            void Run(float seconds) { for (float t = 0; t < seconds; t += .05f) flow.Advance(.05f); }
            var hub = EveRegistrySession.Current;

            // ---- mode ring ----
            var listed = PNModeCatalog.Listed(hub, false);
            PNListedMode picked = null;
            var ring = new PNModeScreen(listed, m => picked = m);
            Check(ring.Items.Count == 1 && ring.UsesBackdrop && ring.FrontIndex == 0 && ring.Front.Id.ToString() == "project_n:roguelite", "the ring starts on the first mode");
            ring.Pick(); Check(picked != null && picked.Id.ToString() == "project_n:roguelite", "picking the front gate reports its mode");
            ring.Turn(1); ring.Tick(1f); Check(ring.FrontIndex == 0, "a one-mode ring turns around onto the same mode");
            ring.Dispose();
            Reject(() => new PNModeScreen(new PNListedMode[0], null), "a ring needs at least one mode");

            // ---- the controller's order: ring -> setup -> character, back pops ----
            var controller = new PNMenuController(ui);
            Check(controller.OpenHall() && flow.IsOpen && flow.Current is PNModeScreen, "the Hall flow opens on the mode ring");
            Run(1.2f); ((PNModeScreen)flow.Current).Pick(); Run(1.2f);
            Check(flow.Depth == 2 && flow.Current is PNSetupScreen setup && setup.Items.Count == 3, "picking a mode opens the setup screen with the mode's three starts");
            ((PNSetupScreen)flow.Current).Choose(0); Run(1.2f);
            Check(flow.Depth == 3 && flow.Current is PNCharacterScreen && !flow.Current.UsesBackdrop, "starting alone opens the character screen, which draws its own background");
            var character = (PNCharacterScreen)flow.Current;
            Check(character.SelectedIndex == 0 && character.Selected.Id == "project_n:eve" && character.Items.Count == 5, "the character screen lists Eve first and selects her");
            Check(character.Root.Q<Label>("pn-char-name").text == "Eve" && character.Root.Q("pn-char-stats").childCount == 6 && character.Root.Q<Button>("pn-char-continue").enabledSelf, "name, six stat rows and an enabled Continue button");
            Check(character.Root.Q("pn-char-track").childCount == 2, "two round buttons: Eve and Eve 2");
            flow.RequestBack(); Run(1.2f); Check(flow.Depth == 2 && flow.Current is PNSetupScreen, "back from the character screen returns to setup");
            flow.RequestBack(); Run(1.2f); Check(flow.Depth == 1 && flow.Current is PNModeScreen, "back from setup returns to the ring");
            flow.RequestBack(); Run(.8f); Check(!flow.IsOpen, "back on the ring closes the menu");

            // ---- the lobby's character change ----
            Check(controller.OpenCharacterChange() && flow.Current is PNCharacterScreen && flow.Depth == 1, "the lobby opens straight on the character screen");
            Run(1.2f); flow.RequestBack(); Run(.8f); Check(!flow.IsOpen && Time.timeScale == time, "back closes the lobby's character screen and restores time");

            // ---- profile filtering ----
            PNPlayerProfileService.SetState("project_n:eve", PNCharacterState.NotOwned); PNPlayerProfileService.SetState("project_n:eve_2", PNCharacterState.NotOwned);
            var empty = PNPlayableCatalog.Playable(hub.Characters.Snapshot().Values, PNPlayerProfileService.Current, false);
            var none = new PNCharacterScreen(empty, "", null, null);
            Check(empty.Count == 0 && none.SelectedIndex == -1 && !none.Root.Q<Button>("pn-char-continue").enabledSelf, "with no usable character the screen has no selection and Continue is disabled");
            none.Dispose();
            PNPlayerProfileService.SetState("project_n:eve", PNCharacterState.Owned); PNPlayerProfileService.SetState("project_n:eve_2", PNCharacterState.Owned);

            // ---- the host's check of a picked character ----
            Check(ProjectN.Coop.PNNetworkManager.AcceptCharacter("project_n:eve"), "the host accepts Eve");
            Check(!ProjectN.Coop.PNNetworkManager.AcceptCharacter("project_n:dummy") && !ProjectN.Coop.PNNetworkManager.AcceptCharacter("nope:nobody") && !ProjectN.Coop.PNNetworkManager.AcceptCharacter("") && !ProjectN.Coop.PNNetworkManager.AcceptCharacter(new string('x', 200)) && !ProjectN.Coop.PNNetworkManager.AcceptCharacter(null), "the host refuses non-playable, unknown, empty and over-long picks");
        }
        finally
        {
            PNPlayerProfileService.ResetStatics();
            if (EveRegistrySession.State == EveSessionState.Stopping) EveRegistrySession.RetryCleanup();
            runtime.UnloadContent(); root.GetComponent<PNUiRuntime>()?.Unbind(); UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene);
            try { System.IO.Directory.Delete(folder, true); } catch (System.IO.IOException) { }
        }
        return "PASS " + checks + " menu screen checks (ring, setup, character, controller order, host pick check)";
    }

    private static void Reject(Action action, string name)
    { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected rejection: " + name); }

    // Separate session: ending the session while the menu is open frees everything.
    public static string RunSessionEnd()
    {
        if (Application.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Edit required.");
        float time = Time.timeScale;
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Menu flow session-end runtime"); SceneManager.MoveGameObjectToScene(root, scene);
        var runtime = root.AddComponent<PNS3NativeRuntime>();
        int checks = 0; void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
        try
        {
            runtime.EnsureContentLoaded(); runtime.BeginSession(EveSessionRole.Offline);
            PNUiRuntime.Attach(runtime); var ui = root.GetComponent<PNUiRuntime>(); EveRegistrySession.Context.RegisterCleanup(ui.Unbind);
            var screen = new PNMenuTestScreen();
            Check(ui.MenuFlow.Open(screen) && Time.timeScale == 0, "menu open before the session ends");
            runtime.EndSession();
            Check(Time.timeScale == time && screen.Disposed && ui.MenuFlow == null && !ui.MenuOpen, "ending the session frees the lock, restores time and disposes the screen");
        }
        finally
        {
            if (EveRegistrySession.State == EveSessionState.Stopping) EveRegistrySession.RetryCleanup();
            runtime.UnloadContent(); root.GetComponent<PNUiRuntime>()?.Unbind(); UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene);
        }
        return "PASS " + checks + " menu flow session-end checks";
    }
}
#endif
