#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using UnityEngine;

// The owner of the loading screen between two plays (Docs/LoadingHandoffPlan.md). Outside every session: raised the moment the player commits,
// ticked by PNGameFlow every frame while it waits, adopted once by the new play's PNTransitionController at its Join cycle, or abandoned
// (hidden, disposed) when nobody adopts it. The bookkeeping lives in PNHandoffMachine (Core, smoke-tested); this class creates, ticks and
// disposes the screen. `--pn-loading-handoff=off` (Editor / Development builds) switches it off: PNGameFlow then uses its older plain cover
// and the controller opens its own screen, to compare the two behaviours.
public static class PNTransitionHandoffHost
{
    public const string OffFlag = "--pn-loading-handoff=off";
    private static readonly PNHandoffMachine machine = new PNHandoffMachine();
    private static IPNTransitionScreen screen;
    private static bool enabledOverride, enabled = true;
    private static float progress = -1;

    public static bool Enabled
    {
        get
        {
            if (!enabledOverride)
            {
                enabledOverride = true;
                bool off = Array.IndexOf(Environment.GetCommandLineArgs(), OffFlag) >= 0;
                enabled = !(off && (Application.isEditor || Debug.isDebugBuild));
            }
            return enabled;
        }
        set { enabledOverride = true; enabled = value; }
    }
    public static PNHandoffState State => machine.State;
    // True while a raised screen is waiting (or hiding): the plain fallback cover must stay down.
    public static bool Covering => machine.State != PNHandoffState.Idle;
    public static int Raises => machine.Raises;
    public static int Adoptions => machine.Adoptions;
    public static int Abandons => machine.Abandons;
    public static IPNTransitionScreen Screen => screen;
    public static EveId ScreenId => machine.ScreenId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        DisposeScreen(); machine.Reset(); enabledOverride = false; enabled = true; progress = -1;
    }

    // Raise the loading screen for a play about to start (modeId: selection name such as "roguelite" or "hall"; worldDefinition: the first world
    // of its scene when known). True when a screen (or a deliberate "no screen") now covers the start; false when the caller must use its own cover.
    public static bool Raise(string modeId, string worldDefinition)
    {
        var content = PNGameBootstrap.Runtime?.Content;
        return Raise(modeId, worldDefinition, content != null && content.IsReady ? content.Registry : null, PNGameBootstrap.Config.Network);
    }
    // `registry` is the content registry (null = content not loaded yet: the default screen is built straight from its payload).
    internal static bool Raise(string modeId, string worldDefinition, EveRegistryHub registry, PNNetworkProfile network)
    {
        if (!Enabled) return false;
        if (machine.State == PNHandoffState.Raised) return true;
        if (machine.State != PNHandoffState.Idle) return false;
        try
        {

            PNTransitionScreenDefinition definition = null; PNTransitionPayload payload = PNTransitionPayload.Default;
            if (registry != null)
            {
                var world = string.IsNullOrEmpty(worldDefinition) ? default : EveId.Parse(worldDefinition);
                var choice = PNTransitionResolver.Resolve(registry, default, world, modeId, network, out _);
                definition = choice.Definition; payload = choice.Payload;
            }
            var context = new PNTransitionContext(PNTransitionReason.Join, "", worldDefinition ?? "", false, payload.MinimumSeconds, payload.MaximumSeconds, payload.Coverage);
            IPNTransitionScreen created = null; EveId id = definition != null ? definition.Id : PNTransitionSelector.DefaultScreen;
            try
            {
                created = definition != null ? definition.Create() : new PNUiToolkitTransitionScreen(payload);
                created?.Show(context);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                try { created?.Dispose(); } catch { }
                created = null;
                // A failing screen falls back to the default screen once; if that fails too, the caller's plain cover takes over.
                if (registry != null && !ReferenceEquals(id, PNTransitionSelector.DefaultScreen) && registry.Get<PNTransitionScreenDefinition>().TryGet(PNTransitionSelector.DefaultScreen, out var fallback))
                { try { created = fallback.Create(); created?.Show(context); id = fallback.Id; } catch (Exception again) { Debug.LogException(again); try { created?.Dispose(); } catch { } return false; } }
                else return false;
            }
            screen = created;
            machine.Raise(Time.realtimeSinceStartupAsDouble, id); progress = -1;
            return true;
        }
        catch (Exception error) { Debug.LogException(error); DisposeScreen(); return false; }
    }

    // Loading progress for the raised screen (0..1, -1 = unknown), for example the staged boot's step progress.
    public static void SetProgress(float fraction) => progress = float.IsNaN(fraction) ? -1 : Math.Max(-1, Math.Min(1, fraction));

    // The next play is ready: the adopt window starts.
    public static void MarkLoadingEnded() => machine.MarkLoadingEnded(Time.realtimeSinceStartupAsDouble);

    // The start did not happen after all (refused, failed): hide the screen.
    public static void Abandon()
    {
        if (machine.Abandon(Time.realtimeSinceStartupAsDouble)) { try { screen?.Hide("handoff-abandoned"); } catch (Exception error) { Debug.LogException(error); } }
    }
    internal static void ResetForTests() => Reset();

    // The new play's controller takes the raised screen (a null screen with true = the chosen "no screen"). Once, only while raised.
    public static bool TryAdopt(out IPNTransitionScreen adopted, out EveId id, out double startedAt)
    {
        adopted = null;
        if (!machine.TryAdopt(out id, out startedAt)) return false;
        adopted = screen; screen = null;
        return true;
    }

    // Every frame (PNGameFlow.LateUpdate): the raised screen keeps animating on real time; an unadopted one is hidden and disposed.
    public static void Tick(float unscaledDelta)
    {
        if (machine.State == PNHandoffState.Idle) return;
        double now = Time.realtimeSinceStartupAsDouble;
        try { screen?.Tick(new PNTransitionState(PNTransitionPhase.Loading, progress, (float)Math.Max(0, now - machine.StartedAt)), unscaledDelta); }
        catch (Exception error) { Debug.LogException(error); }
        var action = machine.Update(now, screen == null || screen.Finished);
        if (action == PNHandoffAction.StartHide) { try { screen?.Hide("handoff-abandoned"); } catch (Exception error) { Debug.LogException(error); } }
        else if (action == PNHandoffAction.Dispose) DisposeScreen();
    }

    private static void DisposeScreen()
    {
        var value = screen; screen = null;
        try { value?.Dispose(); } catch (Exception error) { Debug.LogException(error); }
    }
}
#endif
