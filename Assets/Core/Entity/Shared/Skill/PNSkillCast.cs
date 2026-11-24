using System;
using System.Collections.Generic;

// EN: G3a (Docs/SkillNetworkFrameworkPlan.md 4.3). What a skill keeps for ONE cast lives with the invocation: a typed memory it declares, and the running Actions/Operations the cast registered so that
//     ONE cancel path can stop them (in reverse order of creation). The skill gets one `OnCastEnded` call per invocation, whether the cast finished or was cancelled; the memory is dropped after it.
// VI: Thứ skill giữ cho MỘT lần cast nằm cùng invocation: bộ nhớ có kiểu do skill khai báo, và các Action/Operation đang chạy mà cast đã đăng ký để MỘT đường hủy dừng chúng (ngược thứ tự tạo).
//     Skill nhận đúng một lần `OnCastEnded` cho mỗi invocation (xong hoặc bị hủy); bộ nhớ bỏ sau đó.
public static class PNCastEnd
{
    public const string Interrupted = "interrupted";
    public const string Died = "died";
    public const string Disabled = "disabled";
    public const string Unloaded = "unloaded";
    public const string Rejected = "rejected";
    public const string Replaced = "replaced";
    public const string Executions = "executions_cancelled";
    // G9a: the owner's machine went into grace (paused, stalled, resuming): the authority ends the cast it was running.
    public const string Disconnected = "disconnected";
}

// One typed object per cast: the skill picks the class (`Use<T>()`), the invocation owns it. A different type for the same cast is a bug and throws.
public sealed class PNCastMemory
{
    private object value;
    public bool HasValue => value != null;
    public T Use<T>() where T : class, new()
    {
        if (value == null) value = new T();
        if (value is T typed) return typed;
        throw new InvalidOperationException("Cast memory already holds " + value.GetType().Name + ", not " + typeof(T).Name + ".");
    }
    internal void Clear() => value = null;
}

// One running thing a cast registered (an Action, an Operation lease, a motion). `Release` is the normal end (the cast finished it itself); a cancel calls the registered callback instead.
public sealed class PNCastTrack
{
    internal PNSkillInvocation Owner; internal Action<string> OnCancel;
    internal PNCastTrack(PNSkillInvocation owner, Action<string> onCancel) { Owner = owner; OnCancel = onCancel; }
    public bool IsActive => Owner != null;
    public void Release() { var owner = Owner; Owner = null; OnCancel = null; owner?.Untrack(this); }
}

public sealed partial class PNSkillInvocation
{
    private readonly List<PNCastTrack> tracks = new List<PNCastTrack>();
    // True from the moment the end of this invocation starts until it is disposed: a second end request from inside the end hook does nothing.
    internal bool Ending;
    public PNCastMemory Memory { get; } = new PNCastMemory();
    public PNCastTrack Track(Action<string> onCancel)
    {
        if (onCancel == null) throw new ArgumentNullException(nameof(onCancel));
        if (IsEnded) throw new InvalidOperationException("The cast has ended.");
        var track = new PNCastTrack(this, onCancel); tracks.Add(track); return track;
    }
    internal void Untrack(PNCastTrack track) => tracks.Remove(track);
    // Cancels what the cast still runs, the last created first. A failing callback is reported and does not stop the others.
    internal void CancelTracked(string reason)
    {
        for (int i = tracks.Count - 1; i >= 0; i--)
        {
            if (i >= tracks.Count) continue;
            var track = tracks[i]; tracks.RemoveAt(i); var cancel = track.OnCancel; track.OnCancel = null; track.Owner = null;
            try { cancel?.Invoke(reason); } catch (Exception error) { State.Report(error); }
        }
    }
    // The one cancel path (rejected, died, disconnect, world change, binding change, paused guess): stops what the cast runs, calls the skill's `OnCastEnded(cancelled: true)` once, ends the invocation.
    public void Cancel(string reason) => State.CancelInvocation(this, reason);
}
