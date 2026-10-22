#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;

public enum PNAgentSignalKind { Alert, Assist, Regroup, FocusTarget }
public sealed class PNAgentSignal
{
    public PNAgentSignalKind Kind { get; }
    public PNActorHandle Sender { get; }
    public PNActorHandle Target { get; }
    public Vector3 ObservedPosition { get; }
    public double SentAt { get; }
    public double ExpiresAt { get; }
    internal readonly Guid Group;
    internal readonly long Sequence;
    internal PNAgentSignal(PNAgentSignalKind kind, PNActorHandle sender, PNActorHandle target, Vector3 position, double now, float ttl, Guid group, long sequence)
    { Kind = kind; Sender = sender; Target = target; ObservedPosition = position; SentAt = now; ExpiresAt = now + ttl; Group = group; Sequence = sequence; }
}

public sealed class PNGroupReservation : IDisposable
{
    private PNAgentGroupService.Group group;
    internal readonly PNAgentGroupMember Owner;
    internal readonly string Key;
    internal readonly PNActorHandle Target;
    internal readonly double ExpiresAt;
    public bool IsCurrent => group != null && Owner.IsCurrent && group.Reservations.TryGetValue(Key, out var current) && ReferenceEquals(current, this) &&
        group.World.GameplayTime < ExpiresAt && (Target == null || Target.TryResolve(out _));
    internal PNGroupReservation(PNAgentGroupService.Group group, PNAgentGroupMember owner, string key, PNActorHandle target, float ttl)
    { this.group = group; Owner = owner; Key = key; Target = target; ExpiresAt = group.World.GameplayTime + ttl; }
    public void Dispose()
    { var previous = group; if (previous == null) return; if (previous.Reservations.TryGetValue(Key, out var current) && ReferenceEquals(current, this)) previous.Reservations.Remove(Key); group = null; }
}

public sealed class PNAgentGroupMember : IDisposable
{
    internal readonly PNAgentGroupService.Group Group;
    internal readonly IPNAgentContext Agent;
    private EveEventSubscription subscription;
    private readonly Queue<PNAgentSignal> inbox = new Queue<PNAgentSignal>();
    private readonly Dictionary<EveEntityLoadContext, long> received = new Dictionary<EveEntityLoadContext, long>();
    private long sequence;
    private bool retired;
    public PNActorHandle Actor => Agent.Self;
    public int Slot { get; }
    public int DroppedSignals { get; private set; }
    public int Priority { get; }
    public PNActorHandle Leader => Group.Leader?.Actor;
    public bool IsCurrent => !retired && Group.Active && Agent.IsCurrent && Group.Members.Contains(this);
    internal PNAgentGroupMember(PNAgentGroupService.Group group, IPNAgentContext agent, int slot, int priority)
    {
        Group = group; Agent = agent; Slot = slot; Priority = priority;
        subscription = EveEventManager.Global.Subscribe<PNAgentSignal>(PNAgentGroupService.SignalEvent, (_, signal) => Receive(signal));
    }
    private void Receive(PNAgentSignal signal)
    {
        if (!IsCurrent || signal == null || signal.Group != Group.Generation || !ReferenceEquals(signal.Sender.World, Actor.World) ||
            signal.ExpiresAt <= Group.World.GameplayTime || !signal.Sender.TryResolve(out _) ||
            !Group.Members.Any(m => m.IsCurrent && ReferenceEquals(m.Actor.Load, signal.Sender.Load))) return;
        if (received.TryGetValue(signal.Sender.Load, out long previous) && signal.Sequence <= previous) return;
        if (received.Count >= 64 && !received.ContainsKey(signal.Sender.Load)) received.Remove(received.Keys.First());
        received[signal.Sender.Load] = signal.Sequence;
        if (inbox.Count == 32) { inbox.Dequeue(); DroppedSignals++; }
        inbox.Enqueue(signal);
    }
    public int Drain(Action<PNAgentSignal> receive, int budget = 8)
    {
        if (receive == null) throw new ArgumentNullException(nameof(receive));
        if (budget < 1 || budget > 32) throw new ArgumentOutOfRangeException(nameof(budget));
        if (!IsCurrent || !Agent.CanExecute) return 0;
        int count = 0;
        while (count < budget && inbox.Count > 0)
        {
            var signal = inbox.Dequeue();
            if (signal.ExpiresAt <= Group.World.GameplayTime || !signal.Sender.TryResolve(out _) || signal.Target != null && !signal.Target.TryResolve(out _) ||
                !Group.Members.Any(m => m.IsCurrent && ReferenceEquals(m.Actor.Load, signal.Sender.Load))) continue;
            count++; receive(signal);
            if (!IsCurrent) break;
        }
        return count;
    }
    public void Send(PNAgentSignalKind kind, Vector3 observedPosition, PNActorHandle target = null, float ttl = 1)
    {
        if (!IsCurrent || !Agent.CanExecute) throw new InvalidOperationException("Signal requires active group membership.");
        if (!Enum.IsDefined(typeof(PNAgentSignalKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        PNNavigationSettings.Position(observedPosition); PNNavigationSettings.Positive(ttl, nameof(ttl));
        if (target != null && (!target.TryResolve(out _) || !ReferenceEquals(target.World, Actor.World))) throw new ArgumentException("Signal target belongs to another lifetime/world.");
        var signal = new PNAgentSignal(kind, Actor, target, observedPosition, Group.World.GameplayTime, ttl, Group.Generation, checked(++sequence));
        var errors = EveEventManager.Global.PublishSafely(PNAgentGroupService.SignalEvent, signal);
        if (errors.Count != 0) throw new AggregateException("Group signal dispatch failed.", errors);
    }
    public PNGroupReservation Reserve(string key, PNActorHandle target = null, float ttl = 1)
    {
        if (!IsCurrent || !Agent.CanExecute) throw new InvalidOperationException("Reservation requires active group membership.");
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128) throw new ArgumentException("Reservation key required.");
        PNNavigationSettings.Positive(ttl, nameof(ttl));
        if (target != null && (!target.TryResolve(out _) || !ReferenceEquals(target.World, Actor.World))) throw new ArgumentException("Reservation target retired or foreign.");
        if (Group.Reservations.TryGetValue(key, out var previous)) { if (previous.IsCurrent) return null; previous.Dispose(); }
        if (Group.Reservations.Count >= 128) return null;
        var lease = new PNGroupReservation(Group, this, key, target, ttl); Group.Reservations.Add(key, lease); return lease;
    }
    public Vector3 FormationPoint(float spacing = 1)
    {
        PNNavigationSettings.Positive(spacing, nameof(spacing));
        if (!IsCurrent || Leader == null || !Leader.TryResolve(out _)) throw new InvalidOperationException("No active group leader.");
        if (ReferenceEquals(Group.Leader, this)) return Actor.Position;
        float angle = Slot * 2.399963f; float radius = spacing * Mathf.Sqrt(Slot + 1);
        return Leader.Position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
    }
    public void Dispose()
    {
        if (retired) return;
        foreach (var lease in Group.Reservations.Values.Where(r => ReferenceEquals(r.Owner, this)).ToArray()) lease.Dispose();
        subscription?.Dispose(); subscription = null; inbox.Clear(); received.Clear(); Group.Members.Remove(this); retired = true;
    }
}

// No global singleton or Update. Game/session composition creates and drives it explicitly.
public sealed class PNAgentGroupService : IDisposable
{
    public static int ActiveInstances { get; private set; }
    internal static readonly EveId SignalEvent = EveId.Parse("project_n:agent_group_signal");
    internal sealed class Group
    {
        internal EveWorldContext World;
        internal EveId Kind;
        internal string Key;
        internal readonly Guid Generation = Guid.NewGuid();
        internal readonly List<PNAgentGroupMember> Members = new List<PNAgentGroupMember>();
        internal readonly Dictionary<string, PNGroupReservation> Reservations = new Dictionary<string, PNGroupReservation>(StringComparer.Ordinal);
        internal PNAgentGroupMember Leader;
        internal bool Active = true;
        internal int NextSlot, LastFrame = -1;
    }
    private readonly EveSessionContext session;
    private readonly List<Group> groups = new List<Group>();
    private readonly HashSet<EveWorldContext> watchedWorlds = new HashSet<EveWorldContext>();
    private bool disposed;
    public int GroupCount => groups.Count;
    public int Ticks { get; private set; }
    public PNAgentGroupService(EveSessionContext session)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        if (!session.IsActive || !session.HasAuthority) throw new InvalidOperationException("Groups require active authority.");
        session.RegisterCleanup(Dispose, afterEntities: true);
        ActiveInstances++;
    }
    public PNAgentGroupMember Join(IPNAgentContext agent, EveId kind, string instance, Func<PNStat, PNStat, bool> eligible, int priority = 0)
    {
        if (disposed || !session.IsActive || !session.HasAuthority || agent == null || !agent.CanExecute || !ReferenceEquals(agent.Self.World.Session, session)) throw new InvalidOperationException("Group join requires active authority.");
        if (!kind.IsValid || string.IsNullOrWhiteSpace(instance) || instance.Length > 128 || eligible == null) throw new ArgumentException("Explicit group kind/instance/eligibility required.");
        if (!agent.Self.TryResolve(out var actor)) throw new InvalidOperationException("Retired actor.");
        var group = groups.FirstOrDefault(g => ReferenceEquals(g.World, agent.Self.World) && g.Kind == kind && g.Key == instance);
        if (group == null)
        {
            if (groups.Count(g => ReferenceEquals(g.World, agent.Self.World)) >= 32) throw new InvalidOperationException("Group capacity reached.");
            group = new Group { World = agent.Self.World, Kind = kind, Key = instance }; groups.Add(group);
            var world = group.World;
            if (watchedWorlds.Add(world)) world.RegisterCleanup(() => ReleaseWorld(world));
        }
        if (group.Members.Count >= 32 || group.Members.Any(m => ReferenceEquals(m.Actor.Load, agent.Self.Load))) throw new InvalidOperationException("Group full or actor already enrolled.");
        foreach (var member in group.Members)
            if (member.Actor.TryResolve(out var peer) && (!eligible(actor, peer) || !eligible(peer, actor))) throw new InvalidOperationException("Game policy rejects group affinity.");
        var result = new PNAgentGroupMember(group, agent, checked(group.NextSlot++), priority); group.Members.Add(result); SelectLeader(group); return result;
    }
    private static void SelectLeader(Group group)
    { group.Leader = group.Members.Where(m => m.IsCurrent).OrderByDescending(m => m.Priority).ThenBy(m => m.Actor.Id.Value).FirstOrDefault(); }
    public void Tick()
    {
        if (disposed) return;
        foreach (var group in groups.ToArray())
        {
            foreach (var member in group.Members.ToArray()) if (!member.IsCurrent) member.Dispose();
            if (!group.Active || group.Members.Count == 0) { Release(group); continue; }
            if (!group.World.CanExecute || group.LastFrame == Time.frameCount) continue;
            group.LastFrame = Time.frameCount; Ticks++;
            foreach (var lease in group.Reservations.Values.ToArray()) if (!lease.IsCurrent) lease.Dispose();
            SelectLeader(group);
        }
    }
    private void Release(Group group)
    {
        group.Active = false;
        foreach (var member in group.Members.ToArray()) member.Dispose();
        foreach (var reservation in group.Reservations.Values.ToArray()) reservation.Dispose();
        group.Leader = null; groups.Remove(group);
    }
    public void ReleaseWorld(EveWorldContext world)
    { foreach (var group in groups.Where(g => ReferenceEquals(g.World, world)).ToArray()) Release(group); watchedWorlds.Remove(world); }
    public void Dispose() { if (disposed) return; foreach (var group in groups.ToArray()) Release(group); disposed = true; ActiveInstances--; }
}
#endif
