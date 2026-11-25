using System;

// EN: The shared life of a spawned instance (Docs/InstanceLifecyclePlan.md, IL0): Running -> Ending -> Done. A reason is an END (a real finish: it gets an Ending phase and the
//     implementation's `OnEnd`) or a CANCEL (forced: no Ending, `OnCancel`). Cleanup runs exactly once, last, for both. The machine holds no Unity type and no reason enum of a
//     kind: each manager passes its own reason as an int and a `PNInstanceReasonTable`, so the three kinds (area, blast, projectile) share the rules and keep their own manager.
// VI: Vong doi chung cua mot instance sinh vao world: Running -> Ending -> Done. Ly do la END (ket thuc that: co pha Ending va `OnEnd`) hoac CANCEL (cuong buc: khong Ending,
//     `OnCancel`). Cleanup chay dung mot lan, sau cung, cho ca hai. May trang thai khong giu kieu Unity hay enum ly do cua kind nao: moi manager truyen ly do dang int va bang ly do.
public enum PNInstancePhase { Running, Ending, Done }

// What the caller of `PNInstanceLifecycle.Begin` must do next.
public enum PNInstanceBegin
{
    // Nothing: the instance already ended, or the end came again (a second End while Ending is dropped).
    Ignored,
    // An END from Running: the phase is now Ending. Call `OnEnd(reason)`, then `ArmEnding(duration)` with the implementation's `EndingDuration` (read after `OnEnd`).
    RunEnd,
    // A CANCEL (from Running or Ending): the phase is now Done. Call `OnCancel(reason)` (not `OnEnd`), then `Cleanup` once (`TakeCleanup`).
    RunCancel,
}

// Which reasons of one kind are an END; every other defined reason is a CANCEL. Reasons are the kind's enum values as ints, contiguous from 0 (`Count` of them).
public sealed class PNInstanceReasonTable
{
    private readonly bool[] ends;
    public int Count => ends.Length;
    public PNInstanceReasonTable(int count, params int[] endReasons)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        ends = new bool[count];
        if (endReasons == null) return;
        foreach (var reason in endReasons)
        {
            if (reason < 0 || reason >= count) throw new ArgumentOutOfRangeException(nameof(endReasons), "An end reason is outside the table.");
            ends[reason] = true;
        }
    }
    public bool IsDefined(int reason) => reason >= 0 && reason < ends.Length;
    public bool IsEnd(int reason) => IsDefined(reason) ? ends[reason] : throw new ArgumentException("Unknown end reason.", nameof(reason));
}

public sealed class PNInstanceLifecycle
{
    private readonly PNInstanceReasonTable table;
    private float remaining;
    private bool armed, cleanupPending;
    public PNInstanceLifecycle(PNInstanceReasonTable table) { this.table = table ?? throw new ArgumentNullException(nameof(table)); }

    public PNInstancePhase Phase { get; private set; } = PNInstancePhase.Done;
    // The reason that ended the instance (the first one that took effect); null while Running.
    public int? Reason { get; private set; }
    // True when the first reason was an END (so `OnEnd` ran); false after a CANCEL from Running (no `OnEnd`).
    public bool EndRan { get; private set; }
    public bool IsRunning => Phase == PNInstancePhase.Running;
    public bool IsEnding => Phase == PNInstancePhase.Ending;
    public float EndingRemaining => Phase == PNInstancePhase.Ending && armed ? remaining : 0f;

    // A fresh use of a pooled instance: Running, nothing owed.
    public void Start()
    {
        Phase = PNInstancePhase.Running; Reason = null; EndRan = false; remaining = 0f; armed = false; cleanupPending = false;
    }

    // The pool takes the instance back: Done, nothing owed, nothing recorded (a pooled object that is not in use ignores every reason).
    public void Release()
    {
        Phase = PNInstancePhase.Done; Reason = null; EndRan = false; remaining = 0f; armed = false; cleanupPending = false;
    }

    // An END or CANCEL arrives. The caller does what the result says, in the order the result documents.
    public PNInstanceBegin Begin(int reason)
    {
        bool end = table.IsEnd(reason);
        switch (Phase)
        {
            case PNInstancePhase.Running:
                Reason = reason;
                if (end) { Phase = PNInstancePhase.Ending; EndRan = true; armed = false; remaining = 0f; return PNInstanceBegin.RunEnd; }
                Phase = PNInstancePhase.Done; cleanupPending = true; return PNInstanceBegin.RunCancel;
            case PNInstancePhase.Ending:
                // An END while Ending is dropped; a CANCEL ends the rest of the Ending (the reason that began it stays the recorded one).
                if (end) return PNInstanceBegin.Ignored;
                Phase = PNInstancePhase.Done; armed = false; remaining = 0f; cleanupPending = true; return PNInstanceBegin.RunCancel;
            default:
                return PNInstanceBegin.Ignored;
        }
    }

    // After `OnEnd`: the implementation's `EndingDuration` (seconds of its own clock). Zero, negative or not finite means no Ending: the phase is Done and Cleanup is due now.
    // Returns true when Cleanup is due. Only valid right after an END from Running.
    public bool ArmEnding(float duration)
    {
        if (Phase != PNInstancePhase.Ending || armed) throw new InvalidOperationException("ArmEnding follows an End from Running, once.");
        armed = true;
        if (float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0f) { Phase = PNInstancePhase.Done; remaining = 0f; cleanupPending = true; return true; }
        remaining = duration; return false;
    }

    // One step of the Ending, `delta` seconds of the instance's clock. Returns true when the Ending is over and Cleanup is due. Does nothing outside Ending.
    public bool AdvanceEnding(float delta)
    {
        if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0f) throw new ArgumentOutOfRangeException(nameof(delta));
        if (Phase != PNInstancePhase.Ending || !armed) return false;
        remaining -= delta;
        if (remaining > 0f) return false;
        Phase = PNInstancePhase.Done; remaining = 0f; cleanupPending = true; return true;
    }

    // Cleanup is owed exactly once per use: true the first time it is asked after the instance became Done.
    public bool TakeCleanup()
    {
        if (!cleanupPending) return false;
        cleanupPending = false; return true;
    }
    public bool CleanupPending => cleanupPending;
}
