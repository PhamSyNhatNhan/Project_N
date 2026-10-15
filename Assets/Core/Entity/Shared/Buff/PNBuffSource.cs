using System;
using System.Linq;
using EveEngine;

// Attribution survives Unity component destruction; live access is always explicitly optional.
public sealed class PNBuffSource
{
    private readonly WeakReference<PNStat> live;
    private readonly WeakReference<EveEntityLoader> owner;
    private readonly long generation, loadId;
    public Guid SessionId { get; }
    public EveEntityId EntityId { get; }
    public PNCombatSourceSnapshot Snapshot { get; }
    public PNStat Live
        => GetLive(false);
    // DOT may keep reading a loaded dead source, without relaxing ordinary combat APIs.
    public PNStat LoadedSource => GetLive(true);
    private PNStat GetLive(bool allowDead)
    {
        if (live == null || !live.TryGetTarget(out var target) || target == null || !target.IsReady) return null;
        if (owner == null || !owner.TryGetTarget(out var loader) || loader == null || loader.Generation != generation || loader.LoadContext?.LoadId != loadId || !EveRegistrySession.IsReady ||
            !EveRegistrySession.Context.Entities.TryGetPublishedPresence(EntityId, generation, out var current) || !ReferenceEquals(current, loader)) return null;
        try { return loader.SessionId == SessionId && loader.LoadContext.Identity.Id == EntityId && (allowDead || !target.IsDead) ? target : null; }
        catch (InvalidOperationException) { return null; }
    }
    public PNBuffSource(Guid sessionId, EveEntityId entityId)
    { if (sessionId == Guid.Empty || !entityId.IsValid) throw new ArgumentException("Source requires session and stable entity/system attribution."); SessionId = sessionId; EntityId = entityId; }
    private PNBuffSource(PNStat source) : this(source.SessionId, source.EntityId)
    { Snapshot = source.CaptureCombatSource(); live = new WeakReference<PNStat>(source); var loader = source.GetComponent<EveEntityLoader>(); if (loader != null && loader.LoadContext != null) { owner = new WeakReference<EveEntityLoader>(loader); generation = loader.Generation; loadId = loader.LoadContext.LoadId; } }
    public static PNBuffSource Capture(PNStat source)
    { if (source == null) throw new ArgumentNullException(nameof(source)); return new PNBuffSource(source); }
}

public class PNCombatSourceSnapshot
{
    private readonly PNStatSet stats;
    private readonly Func<bool> canCreate;
    private static readonly Random random = new Random();
    public Guid SessionId { get; }
    public EveEntityId SourceId { get; }
    internal PNCombatSourceSnapshot(Guid sessionId, EveEntityId id, PNStatSet stats, Func<bool> canCreate)
    { SessionId = sessionId; SourceId = id; this.stats = stats; this.canCreate = canCreate; }
    // Custom capture hooks can wrap immutable base capture with their own captured formula state.
    protected PNCombatSourceSnapshot(PNCombatSourceSnapshot original)
    {
        if (original == null) throw new ArgumentNullException(nameof(original));
        SessionId = original.SessionId; SourceId = original.SourceId; stats = original.stats; canCreate = original.canCreate;
    }
    public virtual double Evaluate(params EveId[] ids) => stats.Evaluate(ids);
    protected void RequireReady() { if (!canCreate()) throw new InvalidOperationException("Combat snapshot requires its active authoritative session."); }
    public virtual PNDamage CreateDamage(double amount, bool canCrit, double? iframeDuration, params EveId[] tags)
    {
        using (EveHotPathMetrics.Measure("combat.create-snapshot"))
        {
        RequireReady(); PNHeal.RequireAmount(amount);
        if (tags == null) throw new ArgumentNullException(nameof(tags));
        var canonical = stats.PrepareDamageTags(tags);
        double outgoing = stats.Calculate(PNContentIds.Scale, amount, stats.GetType() == typeof(PNStatSet) ? canonical.Values : (EveId[])canonical.Values.Clone());
        double rate = stats.TryGetBase(PNContentIds.CritRate, out _) ? stats.Evaluate(PNContentIds.CritRate) : 0;
        double critDamage = stats.TryGetBase(PNContentIds.CritDamage, out _) ? stats.Evaluate(PNContentIds.CritDamage) : 150;
        double chance = Math.Max(0, Math.Min(100, rate)) / 100;
        bool critical = canCrit && (chance >= 1 || chance > 0 && random.NextDouble() < chance);
        if (critical) outgoing *= critDamage / 100; PNHeal.RequireAmount(outgoing);
        return new PNDamage(SessionId, SourceId, outgoing, canonical, iframeDuration, canCrit, critical, rate, critDamage);
        }
    }
    public virtual PNHeal CreateHeal(double amount)
    { RequireReady(); PNHeal.RequireAmount(amount); return new PNHeal(SessionId, SourceId, stats.Calculate(PNContentIds.Scale, amount, PNContentIds.Heal), amount); }
}

public partial class PNStat
{
    private PNStatSet capturedCombatSet;
    private PNCombatSourceSnapshot capturedCombatSource;
    public virtual PNCombatSourceSnapshot CaptureCombatSource()
        => CaptureSource(false);
    public virtual PNCombatSourceSnapshot CaptureBuffCombatSource() => CaptureSource(true);
    private PNCombatSourceSnapshot CaptureSource(bool allowDead)
    {
        using (EveEngine.EveHotPathMetrics.Measure("combat.capture-source"))
        {
        var original = RequireCombatReady();
        if (!allowDead && IsDead) throw new InvalidOperationException("Capture source before death.");
        var copy = original.CaptureCopy();
        if (ReferenceEquals(copy, capturedCombatSet) && capturedCombatSource != null) return capturedCombatSource;
        var session = EveRegistrySession.Context;
        capturedCombatSet = copy;
        return capturedCombatSource = new PNCombatSourceSnapshot(SessionId, EntityId, copy, () => session.IsActive && session.HasAuthority);
    
        }
    }
}
