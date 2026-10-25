#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

// EN: Per-world candidate lists of one session, shared by projectile contacts and target selection. The projectile manager drives Tick
//     while projectiles fly (membership at a limited rate of game time, boxes every tick). Acquire serves callers outside that loop
//     (autotarget, a volley's aim): it refreshes membership when the entity list changed or the last refresh is older than the period,
//     re-reads the boxes unless a manager tick already did it this frame, and re-reads aggro. No allocation when nothing changed. In the
//     manager tick aggro is read with the membership (every RefreshSeconds), so a homing search sees an aggro change within that period.
// VI: Danh sách ứng viên theo world của một phiên, dùng chung cho va chạm đạn và chọn mục tiêu. Manager đạn gọi Tick khi có đạn bay;
//     Acquire phục vụ nơi gọi ngoài vòng đó (autotarget, ngắm loạt đạn): làm mới thành viên khi danh sách entity đổi hoặc quá chu kỳ,
//     đọc lại hộp trừ khi tick của manager đã đọc trong frame này.
public sealed class PNTargetIndex
{
    private sealed class State
    {
        internal readonly PNTargetCandidates Candidates = new PNTargetCandidates();
        internal float Since = float.MaxValue;              // game time since the last membership refresh (manager ticks)
        internal float RefreshedAt = float.NegativeInfinity; // realtime of the last membership refresh (any path)
        internal ulong Revision; internal bool Refreshed;
        internal int TickedFrame = -1;                       // frame of the last manager tick (boxes already read)
    }
    private static readonly object NoWorld = new object();
    private readonly Dictionary<object, State> states = new Dictionary<object, State>();
    public EveSessionContext Session { get; private set; }
    public float RefreshSeconds { get; private set; } = .1f;
    public void Initialize(EveSessionContext session, float refreshSeconds)
    {
        if (!ReferenceEquals(Session, session)) states.Clear();
        Session = session; RefreshSeconds = refreshSeconds;
    }
    private State Get(EveWorldContext world)
    {
        object key = (object)world ?? NoWorld;
        if (!states.TryGetValue(key, out var state)) states.Add(key, state = new State());
        return state;
    }
    private void Refresh(State state, EveWorldContext world)
    {
        state.Candidates.Refresh(Session, world);
        state.Since = 0f; state.RefreshedAt = Time.realtimeSinceStartup; state.Refreshed = true;
        state.Revision = Session != null && Session.IsActive ? Session.Entities.Revision : 0;
    }
    // Manager tick (game time): membership every RefreshSeconds, boxes every call. Unchanged from the former per-manager lists, so a new
    // entity joins on the next period, a moved one is found at its new place on the next tick and a dead one is dropped at once.
    public PNTargetCandidates Tick(EveWorldContext world, float gameDelta)
    {
        var state = Get(world);
        state.Since += gameDelta;
        if (state.Since >= RefreshSeconds) Refresh(state, world);
        state.Candidates.UpdateBounds();
        state.TickedFrame = Time.frameCount;
        return state.Candidates;
    }
    // Up-to-date list for one query outside the manager tick.
    public PNTargetCandidates Acquire(EveWorldContext world)
    {
        var state = Get(world);
        bool changed = Session != null && Session.IsActive && Session.Entities.Revision != state.Revision;
        if (!state.Refreshed || changed || Time.realtimeSinceStartup - state.RefreshedAt >= RefreshSeconds) Refresh(state, world);
        if (!(Application.isPlaying && state.TickedFrame == Time.frameCount)) state.Candidates.UpdateBounds();
        state.Candidates.UpdateAggro();
        return state.Candidates;
    }
    // Last list of a world without refreshing it (null when none was built yet).
    public PNTargetCandidates Peek(EveWorldContext world) => states.TryGetValue((object)world ?? NoWorld, out var state) ? state.Candidates : null;
    public void ReleaseWorld(EveWorldContext world) => states.Remove((object)world ?? NoWorld);
    public void Clear() => states.Clear();
}
#endif
