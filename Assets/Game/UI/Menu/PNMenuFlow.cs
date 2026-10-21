#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

// EN: One screen of the mode / setup / character flow. The flow animates `Items` (enter, leave); a screen that draws its own background
//     returns false from UsesBackdrop.
// VI: Một màn của luồng chế độ / thiết lập / nhân vật.
internal interface IPNMenuScreen : IDisposable
{
    VisualElement Root { get; }
    IReadOnlyList<VisualElement> Items { get; }
    bool UsesBackdrop { get; }
    Color Tint { get; }
    // True when the screen handled "back" itself (for example a confirm before leaving a room); false lets the flow pop.
    bool HandleBack();
    void Tick(float dt);
    void Entered();
    // Called every frame while the menu is open: `entering` is true from the screen's entry until it starts leaving; `elapsed` counts from that start.
    // Screens with their own decoration (orbits, dials) animate it here with their own timings.
    void Animate(bool entering, float elapsed);
}

// EN: The menu frame: screen stack, the shared backdrop, the two corner buttons, enter / leave motion with the pause menu's timings,
//     and the input/time lock (PNMenuLock). It does not decide where it opens: callers use Open (Hall) or OpenOn (lobby) and never
//     while pause is open. Esc / gamepad B = back (the first screen closes). The pause menu is not modified.
// VI: Khung menu: ngăn xếp màn hình, nền chung, hai nút góc, chuyển động như pause và khóa input/thời gian.
internal sealed class PNMenuFlow : IDisposable
{
    private enum Phase { Closed, Entering, Shown, Leaving, Closing }
    private struct ItemState { internal float Alpha, Y, Scale; }

    private readonly PNUiRuntime owner;
    private readonly VisualElement overlay, content;
    private readonly PNMenuBackdrop backdrop;
    private readonly PNMenuCorner corners;
    private readonly PNMenuLock menuLock;
    private readonly List<IPNMenuScreen> stack = new List<IPNMenuScreen>();
    private readonly InputAction back;
    private ItemState[] states = new ItemState[0];
    private Phase phase = Phase.Closed;
    private float elapsed, openElapsed, closeFromAlpha, firstDelay, decoration;
    private Action pending;
    private IPNMenuScreen leaving;
    private Guid session;
    private bool disposed;
    public bool IsOpen => phase != Phase.Closed;
    public bool IsClosing => phase == Phase.Closing;
    public bool IsBusy => phase == Phase.Entering || phase == Phase.Leaving || phase == Phase.Closing;
    public int Depth => stack.Count;
    internal string DebugState => phase + " elapsed=" + elapsed.ToString("0.000") + " pending=" + (pending != null);
    public IPNMenuScreen Current => stack.Count == 0 ? null : stack[stack.Count - 1];
    public bool InputLocked => menuLock.Held;
    // Raised once when the menu has fully closed (after the exit animation and the lock release).
    public event Action Closed;

    public PNMenuFlow(PNUiRuntime owner, VisualElement root)
    {
        this.owner = owner;
        menuLock = new PNMenuLock(owner);
        overlay = new VisualElement { name = "pn-menu-overlay", pickingMode = PickingMode.Position };
        overlay.style.position = Position.Absolute; overlay.style.left = 0; overlay.style.right = 0; overlay.style.top = 0; overlay.style.bottom = 0; overlay.style.overflow = Overflow.Hidden;
        backdrop = new PNMenuBackdrop(); overlay.Add(backdrop.Root);
        content = new VisualElement { name = "pn-menu-content", pickingMode = PickingMode.Ignore };
        content.style.position = Position.Absolute; content.style.left = 0; content.style.right = 0; content.style.top = 0; content.style.bottom = 0;
        overlay.Add(content);
        corners = new PNMenuCorner(() => RequestBack(), () => Close()); overlay.Add(corners);
        overlay.style.display = DisplayStyle.None;
        root.Add(overlay);
        back = new InputAction("Menu back", InputActionType.Button);
        back.AddBinding("<Keyboard>/escape"); back.AddBinding("<Gamepad>/buttonEast");
        back.performed += OnBack; back.Enable();
    }

    private void OnBack(InputAction.CallbackContext _) { if (IsOpen && phase != Phase.Closing) RequestBack(); }

    // Opens on a first screen. Refused (false) while pause is open, while already open, or without a ready session.
    public bool Open(IPNMenuScreen first)
    {
        if (disposed || first == null) return false;
        if (IsOpen || owner.PauseOpen || !EveRegistrySession.IsReady) { first.Dispose(); return false; }
        session = EveRegistrySession.Context.SessionId;
        stack.Clear(); stack.Add(first); content.Add(first.Root);
        phase = Phase.Entering; elapsed = 0; openElapsed = 0; firstDelay = PNMenuMotion.FirstEnterDelay; decoration = first.UsesBackdrop ? 1 : 0;
        overlay.style.display = DisplayStyle.Flex;
        menuLock.Acquire();
        backdrop.SurfaceAlpha = 0; backdrop.DecorationAlpha = decoration; backdrop.Tint = first.Tint; backdrop.SetOrbits(first.UsesBackdrop);
        Present(first, true);
        return true;
    }

    // Opens with several screens already stacked (the lobby opens on the character screen, whose back closes the menu: pass one screen).
    public bool OpenOn(IPNMenuScreen first) => Open(first);

    public void Push(IPNMenuScreen next)
    {
        if (!IsOpen || phase == Phase.Closing || next == null) { next?.Dispose(); return; }
        backdrop.SetOrbits(next.UsesBackdrop); Transition(() => { stack.Add(next); content.Add(next.Root); }, null);
    }

    // Replaces the current screen without keeping it in the stack (for example from the setup screen to the character screen).
    public void Replace(IPNMenuScreen next)
    {
        if (!IsOpen || phase == Phase.Closing || next == null) { next?.Dispose(); return; }
        var old = Current;
        backdrop.SetOrbits(next.UsesBackdrop); Transition(() => { stack[stack.Count - 1] = next; content.Add(next.Root); }, old);
    }

    public void RequestBack()
    {
        if (!IsOpen || phase == Phase.Closing) return;
        var screen = Current;
        if (screen != null && screen.HandleBack()) return;
        if (stack.Count <= 1) { Close(); return; }
        var old = screen;
        backdrop.SetOrbits(stack[stack.Count - 2].UsesBackdrop); Transition(() => { stack.RemoveAt(stack.Count - 1); }, old);
    }

    public void Close()
    {
        if (!IsOpen || phase == Phase.Closing) return;
        phase = Phase.Closing; elapsed = 0; closeFromAlpha = backdrop.SurfaceAlpha; pending = null; backdrop.SetOrbits(false);
    }

    private void Transition(Action change, IPNMenuScreen dispose)
    {
        // A later request replaces a queued one only when nothing is leaving yet; otherwise it waits for the leaving screen.
        Action run = () => { change(); if (dispose != null) { dispose.Root.RemoveFromHierarchy(); dispose.Dispose(); } };
        if (phase == Phase.Leaving) { var previous = pending; pending = () => { previous?.Invoke(); run(); }; return; }
        phase = Phase.Leaving; elapsed = 0; pending = run; leaving = Current;
    }

    private void Present(IPNMenuScreen screen, bool initial)
    {
        foreach (var other in stack) other.Root.style.display = ReferenceEquals(other, screen) ? DisplayStyle.Flex : DisplayStyle.None;
        screen.Root.style.display = DisplayStyle.Flex;
        var items = screen.Items;
        states = new ItemState[items.Count];
        for (int i = 0; i < items.Count; i++) { states[i] = new ItemState { Alpha = 0, Y = PNMenuMotion.EnterRise, Scale = 1 }; Apply(items[i], states[i]); }
        if (!initial) firstDelay = 0;
        phase = Phase.Entering; elapsed = 0;
        backdrop.Tint = screen.Tint;
    }

    private static void Apply(VisualElement item, ItemState s)
    {
        item.style.opacity = s.Alpha; item.style.translate = new Translate(0, s.Y); item.style.scale = new Scale(new Vector2(s.Scale, s.Scale));
        item.pickingMode = s.Alpha < .5f ? PickingMode.Ignore : PickingMode.Position;
    }

    public void Tick() => Advance(Time.unscaledDeltaTime);

    // Tests drive time through this.
    internal void Advance(float dt)
    {
        if (disposed) return;
        menuLock.Tick();
        if (!IsOpen) return;
        if (!EveRegistrySession.IsReady || EveRegistrySession.Context.SessionId != session) { Abort(); return; }
        elapsed += dt; openElapsed += dt;
        FitSafeArea();
        var screen = Current;
        var items = screen?.Items;
        switch (phase)
        {
            case Phase.Entering:
                backdrop.SurfaceAlpha = PNMenuMotion.SurfaceIn(openElapsed);
                for (int i = 0; i < items.Count; i++)
                { PNMenuMotion.Enter(elapsed, PNMenuMotion.EnterDelay(i, firstDelay), out var a, out var y); states[i] = new ItemState { Alpha = a, Y = y, Scale = 1 }; Apply(items[i], states[i]); }
                if (elapsed >= PNMenuMotion.EnterTotal(items.Count, firstDelay)) { phase = Phase.Shown; screen.Entered(); }
                break;
            case Phase.Leaving:
            {
                var old = leaving.Items;
                var from = states;
                for (int i = 0; i < old.Count && i < from.Length; i++)
                { PNMenuMotion.Exit(elapsed, PNMenuMotion.ExitDelay(i, old.Count), from[i].Alpha, from[i].Y, from[i].Scale, out var a, out var y, out var s); Apply(old[i], new ItemState { Alpha = a, Y = y, Scale = s }); }
                if (elapsed >= PNMenuMotion.ExitTotal(old.Count))
                {
                    var run = pending; pending = null; leaving = null;
                    run?.Invoke();
                    var next = Current; if (next == null) { Close(); break; }
                    decoration = next.UsesBackdrop ? 1 : 0;
                    Present(next, false);
                }
                break;
            }
            case Phase.Closing:
            {
                var current = items;
                if (current != null)
                    for (int i = 0; i < current.Count && i < states.Length; i++)
                    { PNMenuMotion.Exit(elapsed, PNMenuMotion.ExitDelay(i, current.Count), states[i].Alpha, states[i].Y, states[i].Scale, out var a, out var y, out var s); Apply(current[i], new ItemState { Alpha = a, Y = y, Scale = s }); }
                backdrop.SurfaceAlpha = PNMenuMotion.SurfaceOut(closeFromAlpha, elapsed);
                if (elapsed >= PNMenuMotion.CloseTotal) FinishClose();
                break;
            }
        }
        if (IsOpen)
        {
            // Decoration (aura, stars) follows the screen: fades out for a screen that draws its own background.
            backdrop.DecorationAlpha = Mathf.MoveTowards(backdrop.DecorationAlpha, decoration, dt / .25f);
            backdrop.Tick(dt);
            corners.Alpha = backdrop.SurfaceAlpha;
            corners.BackDimmed = stack.Count <= 1;
            var animated = phase == Phase.Leaving && leaving != null ? leaving : Current;
            animated?.Animate(phase == Phase.Entering || phase == Phase.Shown, elapsed);
            if (Current != null) backdrop.Tint = Current.Tint;
            Current?.Tick(dt);
        }
    }

    private void FinishClose()
    {
        phase = Phase.Closed; overlay.style.display = DisplayStyle.None;
        DisposeScreens(); menuLock.Release(); owner.ResetMobileInput();
        Closed?.Invoke();
    }

    private void Abort()
    {
        phase = Phase.Closed; pending = null; leaving = null; overlay.style.display = DisplayStyle.None;
        DisposeScreens(); menuLock.Abort();
    }

    private void DisposeScreens()
    {
        foreach (var screen in stack) { screen.Root.RemoveFromHierarchy(); screen.Dispose(); }
        stack.Clear(); states = new ItemState[0];
    }

    private void FitSafeArea()
    {
        bool simulated = PNMobilePlatform.UsesSimulatedScreen;
        var area = simulated ? UnityEngine.Device.Screen.safeArea : Screen.safeArea;
        float sw = simulated ? UnityEngine.Device.Screen.width : Screen.width, sh = simulated ? UnityEngine.Device.Screen.height : Screen.height;
        float w = owner.HudRoot.resolvedStyle.width, h = owner.HudRoot.resolvedStyle.height;
        if (sw <= 0 || sh <= 0 || !float.IsFinite(w) || !float.IsFinite(h)) return;
        if (area.width <= 0 || area.height <= 0 || area.xMin < 0 || area.yMin < 0 || area.xMax > sw + .5f || area.yMax > sh + .5f) area = new Rect(0, 0, sw, sh);
        corners.Inset(w * area.xMin / sw, w * (1 - area.xMax / sw), h * (1 - area.yMax / sh));
        corners.Mobile = PNMobilePlatform.IsMobile(simulated) || owner.ShowMobileControls;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Abort();
        back.performed -= OnBack; back.Disable(); back.Dispose();
        backdrop.Dispose(); overlay.RemoveFromHierarchy();
    }
}
#endif
