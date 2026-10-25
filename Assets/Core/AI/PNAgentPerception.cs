#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;

public sealed class PNPerceptionSettings
{
    public float Range { get; }
    public float FieldOfView { get; }
    public float Interval { get; }
    public float ReactionDelay { get; }
    public float MemoryDuration { get; }
    public int CandidateBudget { get; }
    public int SightBudget { get; }
    public int MemoryCapacity { get; }
    public PNPerceptionSettings(float range = 20, float fieldOfView = 360, float interval = .2f,
        float reactionDelay = .15f, float memoryDuration = 3, int candidateBudget = 32, int sightBudget = 8, int memoryCapacity = 64)
    {
        PNNavigationSettings.Positive(range, nameof(range)); PNNavigationSettings.Positive(interval, nameof(interval));
        PNAgentGeometry.Nonnegative(reactionDelay, nameof(reactionDelay)); PNNavigationSettings.Positive(memoryDuration, nameof(memoryDuration));
        if (fieldOfView <= 0 || fieldOfView > 360 || float.IsNaN(fieldOfView) || candidateBudget < 1 || candidateBudget > 256 ||
            sightBudget < 1 || sightBudget > 64 || memoryCapacity < 1 || memoryCapacity > 1024) throw new ArgumentOutOfRangeException();
        Range = range; FieldOfView = fieldOfView; Interval = interval; ReactionDelay = reactionDelay;
        MemoryDuration = memoryDuration; CandidateBudget = candidateBudget; SightBudget = sightBudget; MemoryCapacity = memoryCapacity;
    }
}
public enum PNAgentVisibility { Unknown, Visible, Occluded, OutOfRange }
public sealed class PNPerceptionRecord
{
    public PNActorHandle Actor { get; internal set; }
    public Vector3 LastPosition { get; internal set; }
    public Vector3 ObservedVelocity { get; internal set; }
    public PNAgentVisibility Visibility { get; internal set; }
    public bool Visible => Visibility == PNAgentVisibility.Visible;
    public bool ReadyToReact { get; internal set; }
    internal double LastObservedWorldTime;
    internal float LastSeen, LastTested, VisibleSince;
}

// Perception is caller-ticked. It never runs its own Update, casts, or changes targets for an owner.
public sealed class PNAgentPerception : IDisposable
{
    public static int ActiveInstances { get; private set; }
    private readonly IPNAgentContext agent;
    private readonly PNPerceptionSettings settings;
    private readonly Dictionary<EveEntityLoadContext, PNPerceptionRecord> records = new Dictionary<EveEntityLoadContext, PNPerceptionRecord>();
    private readonly List<EveEntityLoadContext> retired = new List<EveEntityLoadContext>();
    private RaycastHit[] sight = new RaycastHit[32];
    private float time, nextSense;
    private int cursor;
    private bool disposed, ticking;
    public int SightQueries { get; private set; }
    public int Evictions { get; private set; }
    public IEnumerable<PNPerceptionRecord> Records => records.Values;
    public PNAgentPerception(IPNAgentContext agent, PNPerceptionSettings settings)
    { this.agent = agent ?? throw new ArgumentNullException(nameof(agent)); this.settings = settings ?? throw new ArgumentNullException(nameof(settings)); ActiveInstances++; }
    public void Tick(float delta)
    {
        if (ticking) throw new InvalidOperationException("Perception tick cannot reenter.");
        ticking = true; try { TickCore(delta); } finally { ticking = false; }
    }
    private void TickCore(float delta)
    {
        PNAgentGeometry.Nonnegative(delta, nameof(delta));
        if (disposed) throw new ObjectDisposedException(nameof(PNAgentPerception));
        if (!agent.IsCurrent) { Dispose(); return; }
        if (!agent.CanExecute || delta == 0) return;
        time += delta; retired.Clear();
        foreach (var pair in records)
        {
            var record = pair.Value;
            if (!record.Actor.TryResolve(out _) || time - record.LastSeen > settings.MemoryDuration) { retired.Add(pair.Key); continue; }
            if (time - record.LastTested > settings.Interval * 2) { record.Visibility = PNAgentVisibility.Unknown; record.ReadyToReact = false; }
            else record.ReadyToReact = record.Visible && time - record.VisibleSince >= settings.ReactionDelay;
        }
        foreach (var key in retired) records.Remove(key);
        if (time < nextSense) return; nextSense = time + settings.Interval;
        if (!agent.Self.TryResolve(out var source)) return;
        var entries = agent.Self.World.Session.Entities.Snapshot(agent.Self.World);
        int usedSight = 0;
        for (int used = 0; used < settings.CandidateBudget && used < entries.Count && usedSight < settings.SightBudget; used++)
        {
            var loader = entries[cursor++ % entries.Count];
            var target = loader.GetComponent<PNStat>(); if (!agent.CanTarget(target)) continue;
            var position = PNActorHandle.Pose(target); var offset = PNAgentGeometry.Planar(position - agent.Self.Position);
            bool inRange = offset.magnitude <= settings.Range && (offset.sqrMagnitude < .000001f ||
                settings.FieldOfView >= 360 || Vector2.Angle(agent.Heading, offset) <= settings.FieldOfView * .5f);
            bool visible = false;
            if (inRange)
            { usedSight++; SightQueries++; visible = PNAgentSpatial.HasSight(source, agent.Self.Position + Vector3.up * .6f, target, position + Vector3.up * .6f, ref sight); }
            var key = loader.LoadContext;
            if (!records.TryGetValue(key, out var record))
            {
                if (!visible) continue;
                if (records.Count >= settings.MemoryCapacity)
                {
                    var victim = records.OrderBy(p => p.Value.LastSeen).ThenBy(p => p.Value.Actor.Id.Value).First();
                    records.Remove(victim.Key); Evictions++;
                }
                record = new PNPerceptionRecord { Actor = PNActorHandle.Capture(target), VisibleSince = time, LastObservedWorldTime = agent.Self.World.GameplayTime };
                records.Add(key, record);
            }
            record.LastTested = time;
            if (visible)
            {
                if (!record.Visible) record.VisibleSince = time;
                double elapsed = agent.Self.World.GameplayTime - record.LastObservedWorldTime;
                var velocity = elapsed > .00001 && elapsed < 1 && record.LastSeen > 0 ? (position - record.LastPosition) / (float)elapsed : Vector3.zero;
                record.ObservedVelocity = Vector3.ClampMagnitude(velocity, 50);
                record.LastPosition = position; record.LastObservedWorldTime = agent.Self.World.GameplayTime; record.LastSeen = time;
            }
            record.Visibility = visible ? PNAgentVisibility.Visible : inRange ? PNAgentVisibility.Occluded : PNAgentVisibility.OutOfRange;
            record.ReadyToReact = visible && time - record.VisibleSince >= settings.ReactionDelay;
        }
    }
    // Best visible, reaction-ready record by the Aggro rule (PNTargetRule.Aggro): distance to its last position minus the actor's aggro stat,
    // smallest first, ties to the lower entity ID. Without aggro this is the nearest one.
    public PNPerceptionRecord NearestVisible()
    {
        if (disposed || !agent.IsCurrent) return null;
        PNPerceptionRecord best = null; var bestScore = default(PNTargetScore);
        foreach (var record in records.Values)
            if (record.Visible && record.ReadyToReact && record.Actor.TryResolve(out var actor) && agent.CanTarget(actor))
            {
                var score = new PNTargetScore { Distance = PNAgentGeometry.CenterDistance(agent.Self.Position, record.LastPosition), Aggro = PNTargetSelector.AggroOf(actor), EntityId = actor.EntityId.Value };
                if (best == null || PNTargetRanking.Better(score, bestScore, PNTargetRule.Aggro)) { best = record; bestScore = score; }
            }
        return best;
    }
    public void Dispose() { if (disposed) return; disposed = true; records.Clear(); retired.Clear(); ActiveInstances--; }
}
#endif
