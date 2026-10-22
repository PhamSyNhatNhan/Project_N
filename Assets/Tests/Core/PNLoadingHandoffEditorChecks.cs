#if UNITY_EDITOR
using System;
using EveEngine;
using UnityEngine;

// Native checks for the loading handoff (Docs/LoadingHandoffPlan.md P2-P4): the screen chosen before any session comes from the content
// registry through the same resolver the controller uses; the host raises it once, ticks it, hands the very same object over once, and
// disposes an abandoned one; a failing screen falls back; the disable switch gives the old behaviour. Real frames and the controller's
// side of the adoption are covered by the Player fixtures.
public static class PNLoadingHandoffEditorChecks
{
    private sealed class CountingScreen : IPNTransitionScreen
    {
        public int Shows, Ticks, Hides, Disposes; public float LastElapsed; public bool Failing;
        public bool ReadyToHide => true;
        public bool Finished => Hides > 0;
        public void Show(PNTransitionContext context) { Shows++; if (Failing) throw new InvalidOperationException("fixture screen failed to show"); }
        public void Tick(PNTransitionState state, float unscaledDeltaTime) { Ticks++; LastElapsed = state.Elapsed; }
        public void Hide(string reason) { Hides++; }
        public void Dispose() { Disposes++; }
    }
    private sealed class Policy : IPNSessionPolicy
    {
        private readonly EveId screen;
        public Policy(EveId screen) { this.screen = screen; }
        public string ModeId => "fixture"; public int MaximumClients => 2; public double GraceSeconds => 15;
        public void OnOwnerLeft(Action retire) => retire();
        public EveId TransitionScreen => screen;
    }

    private static readonly EveId Default = PNTransitionSelector.DefaultScreen, ModeScreen = EveId.Parse("fixture:mode_screen"), BrokenScreen = EveId.Parse("fixture:broken_screen"), NoneScreen = EveId.Parse("fixture:no_screen");

    public static string Run()
    {
        int checks = 0; void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
        var made = new System.Collections.Generic.List<CountingScreen>();
        Func<PNTransitionPayload, IPNTransitionScreen> factory(bool failing = false) => p => { var s = new CountingScreen { Failing = failing }; made.Add(s); return s; };
        var hub = new EveRegistryHub();
        var screens = hub.GetOrCreate<PNTransitionScreenDefinition>();
        screens.Register(new PNTransitionScreenDefinition(Default, PNTransitionKind.UiToolkit, PNTransitionPayload.Default, "fixture", factory()));
        screens.Register(new PNTransitionScreenDefinition(ModeScreen, PNTransitionKind.UiToolkit, PNTransitionPayload.Default, "fixture", factory()));
        screens.Register(new PNTransitionScreenDefinition(BrokenScreen, PNTransitionKind.UiToolkit, PNTransitionPayload.Default, "fixture", factory(true)));
        screens.Register(new PNTransitionScreenDefinition(NoneScreen, PNTransitionKind.None, PNTransitionPayload.Default, "fixture", p => null));
        var modes = hub.GetOrCreate<PNModeDefinition>();
        modes.Register(new PNModeDefinition(EveId.Parse("fixture:cold"), typeof(Policy), p => new Policy(ModeScreen)));
        modes.Register(new PNModeDefinition(EveId.Parse("fixture:plain"), typeof(Policy), p => new Policy(default)));
        modes.Register(new PNModeDefinition(EveId.Parse("fixture:broken"), typeof(Policy), p => new Policy(BrokenScreen)));
        modes.Register(new PNModeDefinition(EveId.Parse("fixture:blank"), typeof(Policy), p => new Policy(NoneScreen)));
        hub.Seal();
        var network = new PNNetworkProfile();

        // ---- resolution from the registry, before any session ----
        string failure;
        Check(PNTransitionResolver.Resolve(hub, default, default, "fixture:cold", network, out failure).Definition.Id == ModeScreen, "the mode's screen is chosen when the mode names one");
        Check(PNTransitionResolver.Resolve(hub, default, default, "fixture:plain", network, out _).Definition.Id == Default, "a mode without a screen gets the default");
        Check(PNTransitionResolver.Resolve(hub, default, default, "fixture:missing", network, out failure).Definition.Id == Default && failure != null, "an unknown mode falls to the default and reports why");
        Check(PNTransitionResolver.Resolve(hub, BrokenScreen, default, "fixture:cold", network, out _).Definition.Id == BrokenScreen, "a request id outranks the mode");
        Check(PNTransitionResolver.Resolve(hub, default, default, null, network, out _).Definition.Id == Default, "no mode at all gives the default");

        // ---- raise / tick / adopt ----
        PNTransitionHandoffHost.ResetForTests(); PNTransitionHandoffHost.Enabled = true; made.Clear();
        Check(PNTransitionHandoffHost.Raise("fixture:cold", "", hub, network) && PNTransitionHandoffHost.State == PNHandoffState.Raised && PNTransitionHandoffHost.ScreenId == ModeScreen, "raise creates the mode's screen and covers the start");
        var raised = (CountingScreen)PNTransitionHandoffHost.Screen;
        Check(made.Count == 1 && raised.Shows == 1, "the screen is shown once");
        Check(PNTransitionHandoffHost.Raise("fixture:plain", "", hub, network) && made.Count == 1 && PNTransitionHandoffHost.ScreenId == ModeScreen, "a second raise keeps the first screen: never two");
        System.Threading.Thread.Sleep(20); PNTransitionHandoffHost.Tick(.016f); PNTransitionHandoffHost.Tick(.016f);
        Check(raised.Ticks == 2 && raised.LastElapsed > 0, "a raised screen keeps being ticked with the time since the press");
        Check(PNTransitionHandoffHost.TryAdopt(out var adopted, out var adoptedId, out var pressedAt) && ReferenceEquals(adopted, raised) && adoptedId == ModeScreen && pressedAt > 0, "adopt hands over the very same object, its id and the press time");
        Check(raised.Shows == 1 && raised.Disposes == 0 && PNTransitionHandoffHost.Screen == null && PNTransitionHandoffHost.State == PNHandoffState.Idle && PNTransitionHandoffHost.Adoptions == 1, "after adopting, the host no longer holds or touches the screen");
        PNTransitionHandoffHost.Tick(.016f);
        Check(raised.Ticks == 2, "the host does not tick a screen it gave away");
        Check(!PNTransitionHandoffHost.TryAdopt(out _, out _, out _), "a second adopt finds nothing");

        // ---- abandon: hide, finish, dispose ----
        made.Clear();
        PNTransitionHandoffHost.Raise("fixture:cold", "", hub, network); var abandoned = (CountingScreen)PNTransitionHandoffHost.Screen;
        PNTransitionHandoffHost.Abandon();
        Check(abandoned.Hides == 1 && PNTransitionHandoffHost.State == PNHandoffState.Abandoning && PNTransitionHandoffHost.Covering, "abandon hides the screen and keeps it until it is finished");
        Check(!PNTransitionHandoffHost.Raise("fixture:cold", "", hub, network) || made.Count == 1, "no new screen is raised while the old one is still hiding");
        PNTransitionHandoffHost.Tick(.016f);
        Check(abandoned.Disposes == 1 && PNTransitionHandoffHost.State == PNHandoffState.Idle && PNTransitionHandoffHost.Screen == null && !PNTransitionHandoffHost.Covering, "a finished abandoned screen is disposed once and the host is idle");

        // ---- a failing screen ----
        made.Clear();
        // the host logs the failing screen (as it should); keep this expected exception out of the console
        var logger = Debug.unityLogger; bool wasLogging = logger.logEnabled; logger.logEnabled = false; bool brokenRaised;
        try { brokenRaised = PNTransitionHandoffHost.Raise("fixture:broken", "", hub, network); } finally { logger.logEnabled = wasLogging; }
        Check(brokenRaised && PNTransitionHandoffHost.ScreenId == Default && made.Count == 2 && made[0].Disposes == 1 && made[1].Shows == 1, "a screen that fails to show is disposed and the default takes its place");
        PNTransitionHandoffHost.TryAdopt(out var fallback, out _, out _); fallback.Dispose();

        // ---- a deliberate "no screen" ----
        made.Clear();
        Check(PNTransitionHandoffHost.Raise("fixture:blank", "", hub, network) && PNTransitionHandoffHost.Screen == null && PNTransitionHandoffHost.State == PNHandoffState.Raised && made.Count == 0, "a mode that chose no screen raises a handoff with nothing to draw");
        Check(PNTransitionHandoffHost.TryAdopt(out var none, out var noneId, out _) && none == null && noneId == NoneScreen, "adopting a deliberate no-screen hands over nothing, successfully");

        // ---- content not loaded yet: the default screen straight from its payload ----
        Check(PNTransitionHandoffHost.Raise("roguelite", "", null, network) && PNTransitionHandoffHost.ScreenId == Default && PNTransitionHandoffHost.Screen != null, "before content exists the default screen is built from its payload");
        PNTransitionHandoffHost.Abandon(); PNTransitionHandoffHost.Tick(.016f);

        // ---- the switch ----
        PNTransitionHandoffHost.ResetForTests(); made.Clear(); PNTransitionHandoffHost.Enabled = false;
        Check(!PNTransitionHandoffHost.Raise("fixture:cold", "", hub, network) && made.Count == 0 && PNTransitionHandoffHost.State == PNHandoffState.Idle, "with the handoff switched off nothing is raised: the caller keeps its own cover");
        PNTransitionHandoffHost.ResetForTests();
        return "PNLoadingHandoffEditorChecks PASS (" + checks + " checks)";
    }
}
#endif
