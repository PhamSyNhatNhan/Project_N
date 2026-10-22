using System;
using EveEngine;

// EN: Bookkeeping for the one loading screen that crosses the boundary between two plays (Docs/LoadingHandoffPlan.md). A screen is "raised"
//     the moment the player commits (before any session of the next play exists), kept by an owner outside every session, and "adopted" once
//     by the new play's transition controller; if nobody adopts it in time it is abandoned (hidden and disposed). Pure time/state logic, no
//     Unity types: the caller supplies real time and does the drawing.
// VI: Sổ sách cho màn Loading duy nhất đi qua ranh giới hai lượt chơi: dựng ngay khi người chơi bấm, chủ giữ nằm ngoài mọi phiên, bộ điều
//     khiển của lượt chơi mới nhận một lần; không ai nhận kịp thì bỏ (ẩn rồi huỷ).
public enum PNHandoffState { Idle, Raised, Abandoning }
public enum PNHandoffAction { None, StartHide, Dispose }

public sealed class PNHandoffMachine
{
    // After the new play is ready, how long the raised screen waits for its controller (a play that expects no local actor never adopts).
    public const double AdoptWindowSeconds = 3;
    // An abandoned screen that does not finish its fade-out is disposed after this long (same watchdog as a transition cycle).
    public const double HideWatchdogSeconds = PNTransitionMachine.HideWatchdogSeconds;

    public PNHandoffState State { get; private set; }
    public EveId ScreenId { get; private set; }
    public double StartedAt { get; private set; }
    public int Raises { get; private set; }
    public int Adoptions { get; private set; }
    public int Abandons { get; private set; }
    private double loadingEndedAt = -1, hideStarted;

    // True when this call opened a new handoff (the caller creates the screen); false when one is already raised or still hiding.
    public bool Raise(double now, EveId screenId)
    {
        if (double.IsNaN(now) || double.IsInfinity(now)) throw new ArgumentOutOfRangeException(nameof(now));
        if (!screenId.IsValid) throw new ArgumentException("A handoff needs a valid screen id.", nameof(screenId));
        if (State != PNHandoffState.Idle) return false;
        State = PNHandoffState.Raised; ScreenId = screenId; StartedAt = now; loadingEndedAt = -1; Raises++;
        return true;
    }

    // The next play is ready (its session exists): from here the adopt window runs.
    public void MarkLoadingEnded(double now)
    {
        if (State == PNHandoffState.Raised && loadingEndedAt < 0) loadingEndedAt = now;
    }

    // The new play's controller takes the screen: it is no longer ours. Returns false when there is nothing raised to take.
    public bool TryAdopt(out EveId screenId, out double startedAt)
    {
        screenId = default; startedAt = 0;
        if (State != PNHandoffState.Raised) return false;
        screenId = ScreenId; startedAt = StartedAt;
        State = PNHandoffState.Idle; ScreenId = default; loadingEndedAt = -1; Adoptions++;
        return true;
    }

    // The play that was starting failed for good, or the owner gave up: hide the screen now.
    public bool Abandon(double now)
    {
        if (State != PNHandoffState.Raised) return false;
        State = PNHandoffState.Abandoning; hideStarted = now; loadingEndedAt = -1; Abandons++;
        return true;
    }

    // Per frame. `screenFinished`: the screen reports its fade-out done. Returns what the owner must do to the screen.
    public PNHandoffAction Update(double now, bool screenFinished)
    {
        switch (State)
        {
            case PNHandoffState.Raised:
                if (loadingEndedAt >= 0 && now - loadingEndedAt >= AdoptWindowSeconds && Abandon(now)) return PNHandoffAction.StartHide;
                return PNHandoffAction.None;
            case PNHandoffState.Abandoning:
                if (screenFinished || now - hideStarted >= HideWatchdogSeconds) { State = PNHandoffState.Idle; ScreenId = default; return PNHandoffAction.Dispose; }
                return PNHandoffAction.None;
            default: return PNHandoffAction.None;
        }
    }

    // Static reset / teardown: forget everything without any callback (the owner disposes the screen itself).
    public void Reset() { State = PNHandoffState.Idle; ScreenId = default; loadingEndedAt = -1; }
}
