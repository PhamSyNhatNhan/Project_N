using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using EveEngine;
using UnityEngine;

// Immutable full replacement, shared by capture and client value view. No gameplay writes.
public sealed class PNStatFrame
{
    public long Revision { get; }
    public long LifeRevision { get; }
    public bool Dead { get; }
    public bool LifeOnly { get; }
    public IReadOnlyDictionary<EveId, PNResourceValue> Resources { get; }
    public PNStatFrame(long revision, long lifeRevision, bool dead, IDictionary<EveId, PNResourceValue> resources, bool lifeOnly = false)
    {
        if (revision < 1 || lifeRevision < 1 || resources == null || resources.Count > 128) throw new FormatException("Stat frame bounds.");
        var copy = new Dictionary<EveId, PNResourceValue>();
        foreach (var entry in resources)
        {
            if (!entry.Key.IsValid) throw new FormatException("Stat ID required.");
            entry.Value.Validate(); copy.Add(entry.Key, entry.Value);
        }
        if(lifeOnly && copy.Count != 0) throw new FormatException("Life notice has resources.");
        LifeOnly = lifeOnly; Revision = revision; LifeRevision = lifeRevision; Dead = dead;
        Resources = new ReadOnlyDictionary<EveId, PNResourceValue>(copy);
    }
}
public readonly struct PNResourceValue
{
    public double Current { get; }
    public double Max { get; }
    public PNResourceValue(double current, double max) { Current = current; Max = max; Validate(); }
    public void Validate()
    {
        if (double.IsNaN(Current) || double.IsInfinity(Current) || double.IsNaN(Max) || double.IsInfinity(Max) ||
            Max < 0 || Current < 0 || Current > Max) throw new FormatException("Invalid resource pair.");
    }
}
public static class PNStatCodec
{
    public const int MaximumBytes = 32768;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    public static byte[] Encode(PNStatFrame frame)
    {
        if (frame == null) throw new ArgumentNullException(nameof(frame));
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Utf8, true))
        {
            writer.Write((byte)1); writer.Write((byte)(frame.LifeOnly ? 1 : 0)); writer.Write(frame.Revision); writer.Write(frame.LifeRevision); writer.Write(frame.Dead);
            writer.Write((ushort)frame.Resources.Count);
            foreach (var pair in frame.Resources.OrderBy(x => x.Key.ToString(), StringComparer.Ordinal))
            {
                var id = Utf8.GetBytes(pair.Key.ToString());
                if (id.Length < 1 || id.Length > 128) throw new FormatException("Stat ID wire limit.");
                writer.Write((byte)id.Length); writer.Write(id); writer.Write(pair.Value.Current); writer.Write(pair.Value.Max);
            }
            writer.Flush(); if (stream.Length > MaximumBytes) throw new FormatException("Stat payload limit.");
            return stream.ToArray();
        }
    }
    public static PNStatFrame Decode(byte[] payload)
    {
        if (payload == null || payload.Length < 21 || payload.Length > MaximumBytes) throw new FormatException("Stat payload size.");
        try
        {
            using (var stream = new MemoryStream(payload, false))
            using (var reader = new BinaryReader(stream, Utf8))
            {
                if (reader.ReadByte() != 1) throw new FormatException("Stat wire version.");
                byte kind = reader.ReadByte(); if(kind > 1) throw new FormatException("Stat payload kind.");
                long revision = reader.ReadInt64(), life = reader.ReadInt64(); byte dead = reader.ReadByte();
                int count = reader.ReadUInt16(); if (dead > 1 || count > 128) throw new FormatException("Stat header.");
                var values = new Dictionary<EveId, PNResourceValue>();
                for (int i = 0; i < count; i++)
                {
                    int length = reader.ReadByte(); if (length < 1 || length > 128) throw new FormatException("Stat ID size.");
                    var bytes = reader.ReadBytes(length); if (bytes.Length != length) throw new FormatException("Truncated Stat ID.");
                    var id = EveId.Parse(Utf8.GetString(bytes));
                    if (values.ContainsKey(id)) throw new FormatException("Duplicate Stat ID.");
                    values.Add(id, new PNResourceValue(reader.ReadDouble(), reader.ReadDouble()));
                }
                if (stream.Position != stream.Length) throw new FormatException("Trailing Stat bytes.");
                return new PNStatFrame(revision, life, dead != 0, values, kind == 1);
            }
        }
        catch (Exception error) when (error is EndOfStreamException || error is ArgumentException || error is DecoderFallbackException)
        { throw new FormatException("Invalid Stat payload.", error); }
    }
}

[DisallowMultipleComponent]
public sealed class PNStatReplication : MonoBehaviour, EveIEntityLoadStep, EveIEntityCommitStep,
    EveIEntityReplicationPart, EveIFrozenEntityReplicationPart
{
    private EveEntityLoadContext context;
    private PNStat stat;
    private PNStatFrame hostFrame, view, publishedFrame;
    private Guid boundHost;
    private EveId[] replicatedIds;
    private double accumulator;
    public Exception SamplingFailure { get; private set; }
    private void Report(Exception error)
    { if(SamplingFailure == null) Debug.LogException(error, this); SamplingFailure = error; }
    public EveId ReplicationId => EveId.Parse("project_n:stat");
    public int LoadOrder => 11;
    public IEnumerable<Type> Dependencies => new[] { typeof(PNStat) };
    public bool IsReady => context != null && context.CanUse;
    public bool CanCaptureReplication => IsReady && context.Session.HasAuthority &&
        (GetComponent<PNBuffManager>() == null || GetComponent<PNBuffManager>().CanCaptureReplication);
    public PNStatFrame View => IsReady && !context.Session.HasAuthority ? view : null;
    public PNStatFrame AuthoritativeState => IsReady && context.Session.HasAuthority ? hostFrame : null;
    public event Action<byte[]> ReplicationDelta;
    public void Load(EveEntityLoadContext value)
    {
        if (context != null) throw new InvalidOperationException("Stat replica already loaded.");
        context = value ?? throw new ArgumentNullException(nameof(value)); stat = GetComponent<PNStat>();
        // Validate selected policies before admitting this actor; no per-peer owner filter exists yet.
        foreach (var pair in stat.Set.BaseSnapshot())
        {
            var definition = context.Registry.Stats.GetEffective(pair.Key);
            if (definition.Replication == EveStatReplication.Owner || definition.Replication != EveStatReplication.None &&
                (definition.Replication != EveStatReplication.Everyone || definition.Kind != EveStatKind.Resource))
                throw new InvalidOperationException("Selected Stat replication policy unsupported.");
        }
        replicatedIds = stat.Set.BaseSnapshot().Keys.Where(id => context.Registry.Stats.GetEffective(id).Replication == EveStatReplication.Everyone)
            .Select(id => context.Registry.Stats.ResolveIdentity(id)).OrderBy(id => id.ToString(), StringComparer.Ordinal).ToArray();
        if (context.Session.HasAuthority) stat.Died += OnDied;
    }
    public void CommitEntity(EveEntityLoadContext value) { if (context.Session.HasAuthority) { Refresh(); publishedFrame = hostFrame; } }
    public void EntityPublished(EveEntityLoadContext value) { }
    private PNStatFrame ReadHost()
    {
        var values = new Dictionary<EveId, PNResourceValue>();
        foreach (var id in replicatedIds)
            values.Add(id, new PNResourceValue(stat.GetCurrent(id), stat.GetMax(id)));
        bool dead = stat.IsDead;
        long life = hostFrame == null ? 1 : hostFrame.Dead != dead ? checked(hostFrame.LifeRevision + 1) : hostFrame.LifeRevision;
        bool changed = hostFrame == null || hostFrame.Dead != dead || hostFrame.Resources.Count != values.Count ||
            values.Any(x => !hostFrame.Resources.TryGetValue(x.Key, out var old) || old.Current != x.Value.Current || old.Max != x.Value.Max);
        return changed ? new PNStatFrame(hostFrame == null ? 1 : checked(hostFrame.Revision + 1), life, dead, values) : hostFrame;
    }
    private bool Refresh() { var next = ReadHost(); bool changed = !ReferenceEquals(next, hostFrame); hostFrame = next; SamplingFailure = null; return changed; }
    private void Publish()
    {
        var listeners = ReplicationDelta; publishedFrame = hostFrame; if (listeners == null) return;
        var payload = PNStatCodec.Encode(hostFrame);
        foreach (Action<byte[]> observer in listeners.GetInvocationList())
            try { observer(payload); } catch (Exception error) { Report(error); }
    }
    private void OnDied(PNDamageResult result)
    {
        if (!IsReady || !context.Session.HasAuthority || hostFrame == null) return;
        try
        {
            // Died runs after HP commit, possibly inside a Buff mutation. Capture life only;
            // numeric/max reads wait for the drained sampling/snapshot boundary.
            bool dead = stat.IsDead;
            if (hostFrame.Dead == dead) return;
            hostFrame = new PNStatFrame(checked(hostFrame.Revision + 1), checked(hostFrame.LifeRevision + 1), dead,
                new Dictionary<EveId, PNResourceValue>(hostFrame.Resources));
            var notice = new PNStatFrame(hostFrame.Revision, hostFrame.LifeRevision, dead,
                new Dictionary<EveId, PNResourceValue>(), true);
            var listeners = ReplicationDelta;
            if(listeners != null) foreach(Action<byte[]> listener in listeners.GetInvocationList())
                try { listener(PNStatCodec.Encode(notice)); } catch(Exception error) { Report(error); }
        }
        catch (Exception error) { Report(error); }
    }
    // Public deterministic boundary for harnesses; Update uses unscaled wall time, never entity time.
    public void Advance(double unscaledDelta)
    {
        if (double.IsNaN(unscaledDelta) || double.IsInfinity(unscaledDelta) || unscaledDelta < 0) throw new ArgumentOutOfRangeException(nameof(unscaledDelta));
        if (!CanCaptureReplication) return;
        accumulator += unscaledDelta; if (accumulator < .05d) return;
        accumulator %= .05d;
        try { Refresh(); if (!ReferenceEquals(hostFrame, publishedFrame)) Publish(); } catch (Exception error) { Report(error); }
    }
#if UNITY_5_3_OR_NEWER
    private void LateUpdate() => Advance(Time.unscaledDeltaTime);
#endif
    public byte[] CaptureReplication()
    {
        if (!CanCaptureReplication) throw new InvalidOperationException("Committed Stat host required.");
        Refresh(); return PNStatCodec.Encode(hostFrame);
    }
    public Func<byte[]> FreezeReplication()
    {
        var bytes = CaptureReplication(); return () => (byte[])bytes.Clone();
    }
    private PNStatFrame Validate(byte[] bytes, Guid host, long generation)
    {
        if (context == null || !context.IsValid || !context.Session.IsActive || context.Session.HasAuthority || host == Guid.Empty ||
            generation != context.Generation || boundHost != Guid.Empty && boundHost != host)
            throw new FormatException("Stat replica identity.");
        var frame = PNStatCodec.Decode(bytes);
        var expected = replicatedIds;
        if (!frame.LifeOnly && !expected.SequenceEqual(frame.Resources.Keys.OrderBy(id => id.ToString(), StringComparer.Ordinal))) throw new FormatException("Stat resource set differs from selected content.");
        if (view != null)
        {
            if (frame.LifeRevision == view.LifeRevision && frame.Dead != view.Dead) throw new FormatException("Conflicting Stat life version.");
            if (!frame.LifeOnly && frame.Revision == view.Revision && !PNStatCodec.Encode(frame).SequenceEqual(PNStatCodec.Encode(view))) throw new FormatException("Conflicting Stat version.");
            if (frame.Revision > view.Revision && frame.LifeRevision < view.LifeRevision) throw new FormatException("Stat life version regressed.");
        }
        return frame;
    }
    private void Apply(PNStatFrame frame, Guid host)
    {
        if(frame.LifeOnly && view == null) throw new FormatException("Life notice requires baseline.");
        if (view == null || frame.Revision > view.Revision)
        {
            view = frame.LifeOnly ? new PNStatFrame(frame.Revision,frame.LifeRevision,frame.Dead,
                new Dictionary<EveId,PNResourceValue>(view.Resources)) : frame;
            boundHost = host;
        }
    }
    public void ApplyReplicationDelta(byte[] bytes, Guid authoritativeSession, long generation)
    {
        if (!IsReady) throw new InvalidOperationException("Stat delta requires Ready.");
        Apply(Validate(bytes, authoritativeSession, generation), authoritativeSession);
    }
    public EveIReplicaStage StageReplication(byte[] bytes, Guid authoritativeSession, long generation)
        {
        var frame = Validate(bytes, authoritativeSession, generation);
        if(frame.LifeOnly)throw new FormatException("Baseline requires full Stat state.");
        return new Stage(this, frame, authoritativeSession);
    }
    private sealed class Stage : EveIReplicaStage
    {
        private readonly PNStatReplication owner; private readonly PNStatFrame frame, before; private readonly Guid host, oldHost;
        private bool committed, published, disposed;
        private readonly EveEntityLoadContext admitted; private PNStatFrame applied;
        public Stage(PNStatReplication owner, PNStatFrame frame, Guid host)
        { this.owner = owner; this.frame = frame; this.host = host; before = owner.view; oldHost = owner.boundHost; admitted = owner.context; }
        public void Commit()
        {
            if(disposed || !ReferenceEquals(owner.context,admitted) || !admitted.IsValid || !admitted.Session.IsActive)
                throw new InvalidOperationException("Stale Stat baseline stage.");
            owner.Apply(frame, host); applied = owner.view; committed = true;
        }
        public void Published()
        { if(disposed || !committed)throw new InvalidOperationException("Stat baseline not committed."); published = true; }
        public void Dispose()
        {
            if(disposed)return; disposed = true;
            if(committed && !published && ReferenceEquals(owner.context,admitted) && ReferenceEquals(owner.view,applied))
            { owner.view = before; owner.boundHost = oldHost; }
        }
    }
    public void Unload()
    {
        if (stat != null) stat.Died -= OnDied;
        context = null; stat = null; replicatedIds = null; hostFrame = view = publishedFrame = null; boundHost = Guid.Empty; accumulator = 0; SamplingFailure = null; ReplicationDelta = null;
    }
}

public static class PNVitals
{
    public static bool TryRead(PNStat stat, EveId resourceId, out double current, out double max, out bool dead)
    {
        current = max = 0; dead = false;
        if (stat == null || !stat.IsReady || !EveRegistrySession.IsReady) return false;
        if (EveRegistrySession.Context.HasAuthority)
        { current = stat.GetCurrent(resourceId); max = stat.GetMax(resourceId); dead = stat.IsDead; return true; }
        var view = stat.GetComponent<PNStatReplication>()?.View;
        var canonical = EveRegistrySession.Current.Stats.ResolveIdentity(resourceId);
        if (view == null || !view.Resources.TryGetValue(canonical, out var value)) return false;
        current = value.Current; max = value.Max; dead = view.Dead; return true;
    }
}
