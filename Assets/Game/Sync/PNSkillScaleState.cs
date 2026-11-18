#if UNITY_5_3_OR_NEWER
using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PNSkillScaleState : MonoBehaviour, IPNSyncEntityListener, IPNSyncStateSource, IPNSyncStateSink
{
    public static readonly EveId State = EveId.Parse("project_n:skill_scales");
    public const int StateBytes = 1024;
    private const int MaxFactors = 16;
    private const double SampleSeconds = .125;

    private PNSyncService sync;
    private PNSyncTarget target;
    private EveEntityIdentity identity;
    private PNStat stat;
    private EveEntityLoader loader;
    private IDisposable lease;
    private EveId[] tags;
    private double[] factors;
    private double nextSample;
    private (Guid World, ulong Incarnation, ulong Membership) scope;
    private ulong appliedRevision;

    public EveId StateId => State;

    // The selected registry is part of admission compatibility. Columns are
    // stable across peers, including presentation tags declared by older mods.
    internal static EveId[] SelectFactors(EveRegistryHub registry)
    {
        var tags = registry.Tags().Snapshot().Values.Where(tag => tag.SkillReplicaFactor).Select(tag => tag.Id)
            .Concat(registry.Actions().Snapshot().Values.Where(action => action.PresentationScaleTag.HasValue)
                .Select(action => registry.Tags().ResolveIdentity(action.PresentationScaleTag.Value)))
            .Distinct().OrderBy(id => id.ToString(), StringComparer.Ordinal).ToArray();
        if (tags.Length > MaxFactors) throw new FormatException("Too many replicated skill factors.");
        return tags;
    }

    private (Guid World, ulong Incarnation, ulong Membership) CurrentScope()
        => (identity.World?.InstanceId ?? Guid.Empty, identity.World?.Incarnation ?? 0, identity.MembershipRevision);

    public double ReadScale(EveId tag)
    {
        if (sync == null || (!sync.IsAuthority && appliedRevision == 0) || CurrentScope() != scope) return 1;
        int index = Array.IndexOf(tags, sync.Session.Registry.Tags().ResolveIdentity(tag));
        return index >= 0 ? factors[index] : 1;
    }

    public void OnBound(PNSyncService service, PNSyncTarget value)
    {
        sync = service;
        target = value;
        identity = GetComponent<EveEntityIdentity>();
        stat = GetComponent<PNStat>();
        loader = GetComponent<EveEntityLoader>();
        tags = SelectFactors(service.Session.Registry);
        factors = new double[tags.Length];
        Array.Fill(factors, 1d);
        if (service.IsAuthority)
        {
            lease = service.RegisterSource(target, this);
            Sample(Time.unscaledTimeAsDouble, initial: true);
        }
        else lease = service.RegisterSink(target, this);
    }

    public void OnUnbound()
    {
        lease?.Dispose();
        lease = null;
        sync = null;
        identity = null;
        stat = null;
        loader = null;
        tags = null;
        factors = null;
        scope = default;
        nextSample = 0;
        appliedRevision = 0;
    }

    private void Update()
    {
        double now = Time.unscaledTimeAsDouble;
        if (sync?.IsAuthority == true && sync.Session.IsActive && (now >= nextSample || CurrentScope() != scope))
            Sample(now);
    }

    private void Sample(double now, bool initial = false)
    {
        nextSample = now + SampleSeconds;
        var current = CurrentScope();
        bool changed = initial || current != scope;
        scope = current;
        bool runnable = loader?.LoadContext?.CanSimulate == true;
        if (!runnable && !changed) return;
        var snapshot = runnable && stat != null && stat.IsReady && !stat.IsDead ? stat.CaptureCombatSource() : null;
        for (int i = 0; i < tags.Length; i++)
        {
            double factor = snapshot?.Calculate(1d, tags[i]) ?? 1;
            if (!IsFactor(factor) || (tags[i] == PNContentIds.SummonScale && factor == 0)) factor = 1;
            changed |= factors[i] != factor;
            factors[i] = factor;
        }
        if (changed) sync.MarkDirty(target, State);
    }

    public JToken Capture()
    {
        var values = new JArray();
        foreach (var factor in factors) values.Add(factor);
        return new JObject { ["w"] = scope.World.ToString("N"), ["n"] = scope.Incarnation,
            ["b"] = scope.Membership, ["f"] = values };
    }

    public void Apply(JToken value, ulong revision)
    {
        if (sync == null || sync.IsAuthority || revision <= appliedRevision) return;
        if (!(value is JObject row) || row.Count != 4 || row["w"]?.Type != JTokenType.String ||
            !Guid.TryParseExact((string)row["w"], "N", out var world) ||
            !(row["f"] is JArray values) || values.Count != tags.Length)
            throw new FormatException("Skill factor image.");
        var incoming = (world, Unsigned(row["n"]), Unsigned(row["b"]));
        if (incoming != CurrentScope()) return;
        var parsed = new double[tags.Length];
        for (int i = 0; i < parsed.Length; i++)
        {
            if (values[i].Type != JTokenType.Float && values[i].Type != JTokenType.Integer)
                throw new FormatException("Skill factor must be numeric.");
            double factor = (double)values[i];
            if (!IsFactor(factor) || (tags[i] == PNContentIds.SummonScale && factor == 0))
                throw new FormatException("Invalid skill factor.");
            parsed[i] = factor;
        }
        factors = parsed;
        scope = incoming;
        appliedRevision = revision;
    }

    private static bool IsFactor(double factor) => factor >= 0 && !double.IsInfinity(factor);

    private static ulong Unsigned(JToken value)
    {
        if (value?.Type != JTokenType.Integer) throw new FormatException("Skill factor scope.");
        try { return (ulong)value; }
        catch (OverflowException error) { throw new FormatException("Skill factor scope.", error); }
    }
}
#endif
