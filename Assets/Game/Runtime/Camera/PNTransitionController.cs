#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;

// Opens a transition screen for the local participant when the local view changes world or starts, and closes it once the new view is
// usable. A screen only draws; it holds no input and drives no gameplay. Runs on real time so pause cannot keep a screen up.
public sealed class PNTransitionController : MonoBehaviour, IPNTransitionSignalSink
{
    public static PNTransitionController Current { get; private set; }
    private EveSessionContext session;
    private PNLocalWorldContext local;
    private PNWorldTransitionCoordinator transfers;
    private PNCameraDirector director;
    private readonly PNTransitionMachine machine = new PNTransitionMachine();
    private readonly HashSet<IPNTransitionSignalSource> attached = new HashSet<IPNTransitionSignalSource>();
    private IPNTransitionScreen screen;
    private EveId screenId;
    private EveId requestedScreen;
    private bool released = true, covered, joinPending, hadWorld;
    private int framesSinceView;
    private double lostSince, now;
    private float lastUnscaled;
    private EveEventSubscription binding;

    public bool IsShowing => machine.Stage != PNTransitionStage.Idle;
    public PNTransitionMachine Machine => machine;
    public EveId ScreenId => screenId;
    public IPNTransitionScreen Screen => screen;
    public int ShowCount { get; private set; }
    public int HideCount { get; private set; }
    public string LastFailure { get; private set; }

    public static PNTransitionController Attach(GameObject host, EveSessionContext session, PNLocalWorldContext local, PNWorldTransitionCoordinator transfers, PNCameraDirector director)
    {
        var controller = host.GetComponent<PNTransitionController>() ?? host.AddComponent<PNTransitionController>();
        controller.Bind(session, local, transfers, director);
        return controller;
    }

    public void Bind(EveSessionContext value, PNLocalWorldContext context, PNWorldTransitionCoordinator coordinator, PNCameraDirector cameraDirector)
    {
        Release();
        released = false; session = value; local = context; transfers = coordinator; director = cameraDirector; Current = this;
        lastUnscaled = Time.realtimeSinceStartup;
        PNViewCamera.Updated += OnViewCameraUpdated;
        if (transfers != null) transfers.HoldGate = ScreenCovers;
        binding = EveEventManager.Global.Subscribe<PNLocalWorldBound>(PNLocalWorldBound.Event, (_, notice) =>
        {
            if (released || local == null || notice.Session != local.Session.SessionId) return;
            framesSinceView = 0;
        });
        // Joining (and late joining) is a transition until a world and a ready actor are published, if this play expects a local actor.
        joinPending = PNGameBootstrap.Config.Network.SelectPlayer;
        value.RegisterCleanup(Release);
    }

    // ---- signals -----------------------------------------------------------------------------------------------------------------

    void IPNTransitionSignalSink.Begin(PNTransitionReason reason, string fromWorld, string toWorld, bool group) => BeginCycle(reason, fromWorld, toWorld, group, default, default);
    void IPNTransitionSignalSink.Progress(PNTransitionPhase phase, float fraction) => machine.SetPhase(phase, fraction);

    public void BeginCycle(PNTransitionReason reason, string fromWorld, string toWorld, bool group, EveId request, EveId destination)
    {
        if (released || session?.IsActive != true) return;
        requestedScreen = request.IsValid ? request : requestedScreen;
        if (machine.Stage != PNTransitionStage.Idle) { machine.Begin(Placeholder(reason, fromWorld, toWorld, group), now); return; }
        var screenDefinition = Resolve(request, destination);
        var payload = screenDefinition.payload;
        var context = new PNTransitionContext(reason, fromWorld, toWorld, group, payload.MinimumSeconds, payload.MaximumSeconds, payload.Coverage);
        if (machine.Begin(context, Time.realtimeSinceStartupAsDouble)) OpenScreen(screenDefinition.definition, context);
    }

    private static PNTransitionContext Placeholder(PNTransitionReason reason, string from, string to, bool group) => new PNTransitionContext(reason, from, to, group, 0, 1, PNTransitionCoverage.Opaque);

    // Chain: transfer request > world policy > mode > default. A candidate that is missing or fails to create falls to the next.
    private (PNTransitionScreenDefinition definition, PNTransitionPayload payload) Resolve(EveId request, EveId destination)
    {
        var registry = session.Registry.Get<PNTransitionScreenDefinition>();
        EveId worldScreen = default, modeScreen = default;
        try
        {
            var target = destination.IsValid ? destination : (local?.World?.DefinitionId ?? default);
            if (target.IsValid && session.Registry.Get<PNWorldDefinition>().TryGet(target, out var worldDefinition)) worldScreen = worldDefinition.Policy.TransitionScreen;
        }
        catch (Exception error) { Debug.LogException(error); }
        try { modeScreen = PNModeDefinition.Select(session.Registry, PNGameBootstrap.ModeId, PNGameBootstrap.Config.Network).TransitionScreen; }
        catch (Exception error) { LastFailure = error.Message; }
        foreach (var id in PNTransitionSelector.Chain(request, worldScreen, modeScreen))
        {
            if (registry.TryGet(id, out var definition)) return (definition, definition.Payload);
            LastFailure = "Transition screen is not registered: " + id; Debug.LogWarning(LastFailure);
        }
        throw new InvalidOperationException("Default transition screen is missing from the content registry.");
    }

    private void OpenScreen(PNTransitionScreenDefinition definition, PNTransitionContext context)
    {
        screen = null; screenId = definition.Id;
        try { screen = definition.Create(); screen?.Show(context); ShowCount++; }
        catch (Exception error)
        {
            LastFailure = error.Message; Debug.LogException(error);
            try { screen?.Dispose(); } catch { }
            screen = null;
            // Presentation failure never breaks the transfer: fall back to the default screen once, then to no screen.
            if (!ReferenceEquals(definition.Id, PNTransitionSelector.DefaultScreen) && session.Registry.Get<PNTransitionScreenDefinition>().TryGet(PNTransitionSelector.DefaultScreen, out var fallback))
            { try { screen = fallback.Create(); screen?.Show(context); screenId = fallback.Id; ShowCount++; } catch (Exception again) { Debug.LogException(again); try { screen?.Dispose(); } catch { } screen = null; } }
        }
    }

    // ---- per frame ---------------------------------------------------------------------------------------------------------------

    // True once a transfer that includes the local actor may swap worlds: the screen already covers the view, or there is nothing to wait for.
    private bool ScreenCovers(PNWorldTransferRequest request)
    {
        var actor = local?.Actor;
        if (released || actor == null || !actor.IsReady || !request.EntityIds.Contains(actor.LoadContext.Identity.Id.Value)) return true;
        if (machine.Stage == PNTransitionStage.Idle) return false;
        return machine.Stage != PNTransitionStage.Showing || screen == null || screen.ReadyToHide;
    }

    private void OnViewCameraUpdated() { if (framesSinceView < 1000) framesSinceView++; }

    private void AttachSources()
    {
        foreach (var source in PNTransitionSources.Snapshot())
            if (attached.Add(source)) { try { source.Attach(this); } catch (Exception error) { Debug.LogException(error); } }
    }

    private bool ConditionsMet()
    {
        if (local?.World == null) return false;
        var actor = local.Actor;
        if (actor == null) return !PNGameBootstrap.Config.Network.SelectPlayer;
        if (!actor.IsReady) return false;
        var gate = actor.GetComponent<PNNetworkInputGate>();
        if (gate != null && gate.Blocked) return false;
        if (transfers != null && transfers.TryGetActive(actor.LoadContext.Identity.Id.Value, out _, out _)) return false;
        // The camera has cut to the viewed world and at least one frame rendered through it.
        return director == null || director.ViewedWorld == local.World && framesSinceView >= 2;
    }

    private void Update()
    {
        if (released || session?.IsActive != true) return;
        now = Time.realtimeSinceStartupAsDouble;
        float delta = Time.realtimeSinceStartup - lastUnscaled; lastUnscaled = Time.realtimeSinceStartup;
        AttachSources();
        PollLocalState();
        bool met = ConditionsMet();
        bool ready = screen == null || screen.ReadyToHide;
        bool finished = screen == null || screen.Finished;
        var action = machine.Update(now, met, ready, finished, out var reopened);
        if (action == PNTransitionAction.StartHide)
        {
            try { screen?.Hide(machine.ForcedHide ? "timeout" : "ready"); } catch (Exception error) { LastFailure = error.Message; Debug.LogException(error); }
            SetCover(false);
        }
        else if (action == PNTransitionAction.Close)
        {
            DisposeScreen(); HideCount++;
            if (reopened != null) { var chosen = Resolve(requestedScreen, default); OpenScreen(chosen.definition, new PNTransitionContext(reopened.Reason, reopened.FromWorld, reopened.ToWorld, reopened.Group, chosen.payload.MinimumSeconds, chosen.payload.MaximumSeconds, chosen.payload.Coverage)); }
            else requestedScreen = default;
        }
        if (machine.Stage == PNTransitionStage.Showing && screen != null && screen.ReadyToHide) SetCover(machine.Context.Coverage == PNTransitionCoverage.Opaque);
        if (machine.Stage != PNTransitionStage.Idle && screen != null)
        {
            try { screen.Tick(new PNTransitionState(machine.Phase, machine.Fraction, machine.Elapsed(now)), delta); }
            catch (Exception error) { LastFailure = error.Message; Debug.LogException(error); }
        }
    }

    // Host-local transfers, join and a vanished world are derived from local state; the network source only opens the cycle earlier.
    private void PollLocalState()
    {
        var actor = local.Actor;
        if (joinPending && machine.Stage == PNTransitionStage.Idle)
        { joinPending = false; BeginCycle(PNTransitionReason.Join, "", local.World?.DefinitionId.ToString() ?? "", false, default, default); }
        if (actor != null && actor.IsReady && transfers != null && transfers.TryGetActive(actor.LoadContext.Identity.Id.Value, out var request, out var state) && state >= PNWorldTransferState.Holding)
        {
            string from = local.World?.DefinitionId.ToString() ?? ""; string to = request.Destination.DefinitionId.ToString();
            if (machine.Stage == PNTransitionStage.Idle) BeginCycle(PNTransitionReason.Transfer, from, to, request.Actors.Count > 1, request.TransitionScreen, request.Destination.DefinitionId);
            machine.SetPhase(state == PNWorldTransferState.Holding ? PNTransitionPhase.Holding : state == PNWorldTransferState.Committing ? PNTransitionPhase.Loading : PNTransitionPhase.CatchingUp);
        }
        else if (machine.Stage == PNTransitionStage.Showing && actor != null && actor.IsReady) machine.SetPhase(PNTransitionPhase.CatchingUp);
        if (local.World != null) { hadWorld = true; lostSince = 0; }
        else if (hadWorld && machine.Stage == PNTransitionStage.Idle)
        {
            if (lostSince == 0) lostSince = now;
            else if (now - lostSince > .25) { hadWorld = false; lostSince = 0; BeginCycle(PNTransitionReason.ActorLost, "", "", false, default, default); }
        }
    }

    private void SetCover(bool on)
    {
        if (covered == on) return;
        covered = on;
        try { director?.SetTransitionCover(on); } catch (Exception error) { Debug.LogException(error); }
    }

    private void DisposeScreen()
    {
        SetCover(false);
        var value = screen; screen = null;
        try { value?.Dispose(); } catch (Exception error) { Debug.LogException(error); }
    }

    public void Release()
    {
        if (released) return;
        released = true;
        PNViewCamera.Updated -= OnViewCameraUpdated;
        if (transfers != null) transfers.HoldGate = null;
        binding?.Dispose(); binding = null;
        foreach (var source in attached) try { source.Detach(); } catch (Exception error) { Debug.LogException(error); }
        attached.Clear();
        if (screen != null) { try { screen.Hide("session-end"); } catch { } }
        DisposeScreen();
        machine.Reset();
        if (ReferenceEquals(Current, this)) Current = null;
        session = null; local = null; transfers = null; director = null;
    }
    private void OnDestroy() => Release();
}
#endif
