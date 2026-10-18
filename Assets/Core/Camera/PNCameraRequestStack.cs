using System;
using System.Collections.Generic;
using System.Linq;

// Why a request ended. Reported once through IPNCameraHandle.Ended.
public enum PNCameraHandleEnd { Released, Replaced, Completed, WorldChanged, SessionEnded, OwnerGone, TargetLost }

// Handle returned for every camera request. Dispose releases exactly this request and is harmless when repeated, when the request was
// replaced by a newer one with the same (owner, key), or after the session ended.
public interface IPNCameraHandle : IDisposable
{
    bool IsActive { get; }
    // Empty while active or for a normally ended request; a stable code when the request was refused (see PNCameraRefusal).
    string RefusalReason { get; }
    // Null while active (and for refused requests, which never started).
    PNCameraHandleEnd? EndReason { get; }
    // Raised once when an accepted request ends, for any reason, on the thread that ended it. Handlers must not throw.
    event Action<PNCameraHandleEnd> Ended;
}

public static class PNCameraRefusal
{
    public const string NoCamera = "NoCamera";
    public const string OtherWorld = "OtherWorld";
    public const string OwnerGone = "OwnerGone";
    public const string NotSupported = "NotSupported";
    public const string LimitReached = "LimitReached";
    public const string InvalidParameter = "InvalidParameter";
    public const string OutOfRange = "OutOfRange";
}

// Refused-from-the-start handle (also what the empty service returns).
public sealed class PNRefusedCameraHandle : IPNCameraHandle
{
    public bool IsActive => false;
    public string RefusalReason { get; }
    public PNCameraHandleEnd? EndReason => null;
    public event Action<PNCameraHandleEnd> Ended { add { } remove { } }
    public PNRefusedCameraHandle(string reason) { RefusalReason = reason ?? PNCameraRefusal.NoCamera; }
    public void Dispose() { }
}

// Live handle owned by a PNCameraRequestStack. Derive to add request-specific events (cinematic shot changes).
public class PNCameraHandle : IPNCameraHandle
{
    private Action<PNCameraHandle> release;
    public bool IsActive { get; private set; }
    public string RefusalReason => "";
    public PNCameraHandleEnd? EndReason { get; private set; }
    public event Action<PNCameraHandleEnd> Ended;
    internal void Attach(Action<PNCameraHandle> releaseAction) { release = releaseAction; IsActive = true; EndReason = null; }
    internal void Finish(PNCameraHandleEnd reason)
    {
        if (!IsActive) return;
        IsActive = false; EndReason = reason;
        var handlers = Ended; Ended = null;
        if (handlers == null) return;
        foreach (Action<PNCameraHandleEnd> handler in handlers.GetInvocationList()) handler(reason);
    }
    public void Dispose() { var action = release; if (IsActive) action?.Invoke(this); }
}

// Pure C# request bookkeeping shared by zoom, lock-on, cinematic and shake: replacement by (owner, key) with generations,
// deterministic winner (highest priority, later registration wins a tie), losers stay alive, release by owner/world/everything,
// bounded size. Owners may be any object; "alive" is decided by a predicate supplied by the host (Unity objects are not known here).
public sealed class PNCameraRequestStack<TPayload>
{
    public sealed class Entry
    {
        public object Owner { get; internal set; }
        public string Key { get; internal set; }
        public int Priority { get; internal set; }
        public Guid World { get; internal set; }
        public long Generation { get; internal set; }
        public long Sequence { get; internal set; }
        public TPayload Payload { get; internal set; }
        public PNCameraHandle Handle { get; internal set; }
    }

    private readonly List<Entry> entries = new List<Entry>();
    private readonly int capacity;
    private long sequence, generation;
    public Func<object, bool> OwnerAlive { get; set; } = owner => true;
    public int Count => entries.Count;
    public int Capacity => capacity;
    public IReadOnlyList<Entry> Entries => entries;

    public PNCameraRequestStack(int capacity = 32)
    {
        if (capacity < 1 || capacity > 1024) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
    }

    private static bool Same(object a, object b) => ReferenceEquals(a, b) || Equals(a, b);

    // Same (owner, key) replaces the earlier request (its handle ends with Replaced). World may be Guid.Empty for "not world scoped".
    public PNCameraHandle Request(object owner, string key, int priority, Guid world, TPayload payload, PNCameraHandle handle = null)
    {
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        if (string.IsNullOrEmpty(key) || key.Length > 128) throw new ArgumentException("A bounded request key is required.", nameof(key));
        Sweep();
        var existing = entries.Find(x => Same(x.Owner, owner) && x.Key == key);
        if (existing == null && entries.Count >= capacity) throw new InvalidOperationException("Camera request limit reached (" + capacity + ").");
        handle = handle ?? new PNCameraHandle();
        if (handle.IsActive) throw new InvalidOperationException("A handle can only be attached to one request.");
        long id = checked(++generation);
        PNCameraHandle replaced = null;
        if (existing == null) { existing = new Entry(); entries.Add(existing); } else replaced = existing.Handle;
        existing.Owner = owner; existing.Key = key; existing.Priority = priority; existing.World = world; existing.Generation = id; existing.Sequence = checked(++sequence); existing.Payload = payload; existing.Handle = handle;
        handle.Attach(h => Release(h));
        replaced?.Finish(PNCameraHandleEnd.Replaced);
        return handle;
    }

    // Highest priority wins; a later registration wins a tie. Dead owners are dropped first.
    public Entry Winner()
    {
        Sweep();
        Entry best = null;
        foreach (var entry in entries)
            if (best == null || entry.Priority > best.Priority || entry.Priority == best.Priority && entry.Sequence > best.Sequence) best = entry;
        return best;
    }

    public IReadOnlyList<Entry> Ordered()
    {
        Sweep();
        return entries.OrderByDescending(x => x.Priority).ThenByDescending(x => x.Sequence).ToArray();
    }

    // Registration order (priority is not an ordering for modifiers that compose).
    public IReadOnlyList<Entry> InRegistrationOrder()
    {
        Sweep();
        return entries.OrderBy(x => x.Priority).ThenBy(x => x.Sequence).ToArray();
    }

    public int ReleaseOwner(object owner) => RemoveWhere(x => Same(x.Owner, owner), PNCameraHandleEnd.Released);
    public int ReleaseWorld(Guid world, PNCameraHandleEnd reason = PNCameraHandleEnd.WorldChanged) => RemoveWhere(x => x.World != Guid.Empty && x.World == world, reason);
    public int ReleaseWhere(Func<Entry, bool> predicate, PNCameraHandleEnd reason = PNCameraHandleEnd.Released) => RemoveWhere(predicate, reason);
    public void Clear(PNCameraHandleEnd reason = PNCameraHandleEnd.SessionEnded) => RemoveWhere(x => true, reason);

    private int RemoveWhere(Func<Entry, bool> predicate, PNCameraHandleEnd reason)
    {
        var removed = entries.Where(predicate).ToArray();
        if (removed.Length == 0) return 0;
        foreach (var entry in removed) entries.Remove(entry);
        foreach (var entry in removed) entry.Handle?.Finish(reason);
        return removed.Length;
    }

    private void Release(PNCameraHandle handle)
    {
        // An older handle must not remove a newer request that replaced it.
        var entry = entries.Find(x => ReferenceEquals(x.Handle, handle));
        if (entry == null) return;
        entries.Remove(entry);
        handle.Finish(PNCameraHandleEnd.Released);
    }

    private void Sweep()
    {
        if (entries.Count > 0) RemoveWhere(x => !OwnerAlive(x.Owner), PNCameraHandleEnd.OwnerGone);
    }
}
