#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Owner-only coherent Ability image, clock baseline and input/cast watermark.
// The owner projects this image once, then advances/predicts by local gameplay delta.
[DisallowMultipleComponent]
public sealed class PNSkillOwnerView : MonoBehaviour, IPNSyncEntityListener, IPNSyncStateSource, IPNSyncStateSink
{
    public static readonly EveId State = EveId.Parse("project_n:skill_owner");
    public static readonly EveId BaselineCommand = EveId.Parse("project_n:skill_owner_baseline");
    public const int StateBytes = 4096;
    private static readonly IReadOnlyDictionary<EveId, EveId> NoBindings = new Dictionary<EveId, EveId>();
    private PNSyncService sync;
    private PNSyncTarget target;
    private PNAbilityManager manager;
    private PNAbilityCollection abilities;
    private PNSkill skill;
    private readonly List<IDisposable> leases = new List<IDisposable>();
    private readonly List<PNClockProjection> clocks = new List<PNClockProjection>();
    private PNAbilityClockValues lastRates;
    private PNAbilityClockValues replicaRates;
    private double nextCapture, nextRequest, deadline, lastImageLocal;
    private bool sawUnready;
    private JObject pending;
    private List<PNAbilityReplicaItem> pendingItems;
    private IReadOnlyDictionary<EveId, EveId> pendingBindings;
    private ulong pendingRevision;
    public bool IsBound { get; private set; }
    public EveId StateId => State;
    public IReadOnlyDictionary<EveId, EveId> Bindings { get; private set; } = NoBindings;
    public ulong AppliedRevision { get; private set; }
    public int AppliedCount { get; private set; }
    private bool prepared;
    public bool Prepared
    {
        get => prepared;
        private set
        {
            if (prepared == value) return;
            prepared = value;
            (skill as PNPlayerSkill)?.RefreshInput();
        }
    }
    public bool HasPending => pending != null;
    public double AppliedAge { get; private set; }
    public int BaselineRequests { get; private set; }
    public PNAbilityClockValues CapturedRates => replicaRates;
    public event Action Applied;
    public event Action Applying;

    public void OnBound(PNSyncService service, PNSyncTarget value)
    {
        sync = service; target = value; IsBound = true; Bindings = NoBindings; AppliedRevision = 0; AppliedCount = 0;
        manager = GetComponent<PNAbilityManager>(); skill = GetComponent<PNSkill>(); abilities = manager != null ? manager.Abilities : null;
        if (service.IsAuthority)
        {
            leases.Add(service.RegisterSource(value, this));
            if (abilities != null) abilities.Changed += OnAbilityChanged;
            if (skill != null) skill.BindingChanged += OnBindingChanged;
        }
        else if (manager != null) { manager.ReplicaCanAdvance = CanAdvanceReplica; manager.ReplicaAuthorityDelta = AuthorityDelta; }
        leases.Add(service.RegisterSink(value, this));
        if (service.IsAuthority) service.MarkDirty(value, State);
    }
    public void OnUnbound()
    {
        if (abilities != null) abilities.Changed -= OnAbilityChanged;
        if (skill != null) skill.BindingChanged -= OnBindingChanged;
        if (manager != null) { manager.ReplicaCanAdvance = null; manager.ReplicaAuthorityDelta = null; }
        foreach (var lease in leases) lease.Dispose(); leases.Clear();
        foreach (var clock in clocks) clock.Dispose(); clocks.Clear();
        pending = null; pendingItems = null; pendingBindings = null;
        Prepared = false; deadline = nextRequest = nextCapture = 0; sawUnready = false;
        IsBound = false; sync = null; manager = null; abilities = null; skill = null; Bindings = NoBindings;
    }
    private bool CanAdvanceReplica() => IsBound && Prepared && sync.Session.IsActive && PNTimeService.For(sync.Session).Sample().Ready;
    private PNAbilityClockValues AuthorityDelta() => new PNAbilityClockValues(Time.unscaledDeltaTime * replicaRates.Game, Time.unscaledDeltaTime * replicaRates.Entity, Time.unscaledDeltaTime * replicaRates.Raw);
    private void OnAbilityChanged(PNAbilityChange change) => Resync();
    private void OnBindingChanged(PNSkillBindingChange change) => Resync();
    public void Resync() { if (IsBound && sync != null && sync.IsAuthority) sync.MarkDirty(target, State); }
    private void OnEnable() => Resync();
    private void OnDisable() => Resync();
    public JToken Capture()
    {
        var sample = PNTimeService.For(sync.Session).Sample();
        var world = GetComponent<EveEntityLoader>().LoadContext.Identity.World;
        var values = abilities.ClockValues; var rates = manager.ClockRates;
        var bindings = new JObject();
        if (skill?.Bindings != null) foreach (var pair in skill.Bindings.Snapshot().OrderBy(p => p.Key.ToString(), StringComparer.Ordinal)) bindings[pair.Key.ToString()] = pair.Value.ToString();
        return new JObject { ["v"] = 2, ["e"] = sample.Epoch.ToString("N"), ["w"] = (world?.InstanceId ?? sample.Epoch).ToString("N"),
            ["n"] = world?.Incarnation ?? 1, ["i"] = target.Entity.Value, ["g"] = target.Generation, ["t"] = sample.Local,
            ["c"] = new JArray(values.Game, values.Entity, values.Raw), ["q"] = new JArray(rates.Game, rates.Entity, rates.Raw),
            ["k"] = skill?.LastCastKey ?? 0, ["r"] = GetComponent<PNSkillRemoteInput>()?.CaptureOwnerResult() ?? new JObject { ["ack"] = 0, ["p"] = 0, ["rej"] = new JArray() },
            ["a"] = PNAbilityReplicaWire.Capture(abilities.CaptureReplica()), ["b"] = bindings };
    }
    public static List<PNAbilityReplicaItem> Parse(JToken value, out IReadOnlyDictionary<EveId, EveId> bindings)
    {
        if (!(value is JObject o) || PNAbilityReplicaWire.Integer(o["v"]) != 2 || !(o["b"] is JObject b) || b.Count > 32) throw new FormatException("Ability owner schema2.");
        GuidId(o["e"]); GuidId(o["w"]);
        if (PNAbilityReplicaWire.Long(o["n"]) < 1 || PNAbilityReplicaWire.Long(o["i"]) < 1 || PNAbilityReplicaWire.Long(o["g"]) < 1 || PNAbilityReplicaWire.Long(o["k"]) < 0) throw new FormatException("Ability owner identity.");
        PNAbilityReplicaWire.Nonnegative(o["t"]); Vector(o["c"]); Vector(o["q"]); PNSkillRemoteInput.ValidateOwnerResult(o["r"]);
        var map = new Dictionary<EveId, EveId>();
        foreach (var p in b.Properties()) { if (p.Value.Type != JTokenType.String) throw new FormatException("Ability binding."); map[EveId.Parse(p.Name)] = EveId.Parse((string)p.Value); }
        bindings = map; return PNAbilityReplicaWire.Parse(o["a"] as JArray);
    }
    private static Guid GuidId(JToken token)
    { if (token == null || token.Type != JTokenType.String || !Guid.TryParseExact((string)token, "N", out var id) || id == Guid.Empty) throw new FormatException("Ability epoch/world."); return id; }
    private static PNAbilityClockValues Vector(JToken token)
    { if (!(token is JArray a) || a.Count != 3) throw new FormatException("Ability clocks."); return new PNAbilityClockValues(PNAbilityReplicaWire.Nonnegative(a[0]), PNAbilityReplicaWire.Nonnegative(a[1]), PNAbilityReplicaWire.Nonnegative(a[2])); }
    public void Apply(JToken value, ulong revision)
    {
        if (sync == null || sync.IsAuthority || revision <= AppliedRevision || revision <= pendingRevision && pending != null) return;
        var items = Parse(value, out var bindings); var o = (JObject)value;
        var sample = PNTimeService.For(sync.Session).Sample(); var world = GetComponent<EveEntityLoader>().LoadContext.Identity.World;
        if (GuidId(o["e"]) != sample.Epoch || GuidId(o["w"]) != (world?.InstanceId ?? sample.Epoch) || PNAbilityReplicaWire.Long(o["n"]) != (long)(world?.Incarnation ?? 1)
            || PNAbilityReplicaWire.Long(o["i"]) != target.Entity.Value || PNAbilityReplicaWire.Long(o["g"]) != target.Generation) return;
        pending = (JObject)o.DeepClone(); pendingItems = items; pendingBindings = bindings; pendingRevision = revision; TryApplyPending(sample);
    }
    private void Update()
    {
        if (!IsBound || sync == null || !sync.Session.IsActive) return;
        var sample = PNTimeService.For(sync.Session).Sample();
        if (sync.IsAuthority)
        {
            var rates = manager.ClockRates;
            if (sample.Local >= nextCapture || rates.Game != lastRates.Game || rates.Entity != lastRates.Entity || rates.Raw != lastRates.Raw)
            { lastRates = rates; nextCapture = sample.Local + .5; Resync(); }
            return;
        }
        if (GetComponent<EveEntityLoader>().LoadContext.Identity.OwnerId != sync.Session.LocalOwnerId) return;
        if (!sample.Ready)
        {
            sawUnready = true; Prepared = false; BeginRecovery(sample.Local);
            if (sample.Local >= deadline) sync.Session.FailConnection("ability_clock_timeout", "Owner Ability readiness did not recover.");
            return;
        }
        if (sawUnready) { sawUnready = false; Prepared = false; RequestBaseline(sample.Local); }
        if (Prepared && sample.Local - lastImageLocal > 2) { Prepared = false; BeginRecovery(sample.Local); RequestBaseline(sample.Local); }
        if (pending != null) TryApplyPending(sample);
        if (!Prepared)
        {
            BeginRecovery(sample.Local); RequestBaseline(sample.Local);
            if (sample.Local >= deadline) sync.Session.FailConnection("ability_clock_timeout", "Owner Ability baseline did not become ready.");
        }
    }
    private void BeginRecovery(double now)
    { if (deadline == 0) deadline = now + PNTimeService.For(sync.Session).RemainingRecoverySeconds; }
    private void RequestBaseline(double now)
    {
        if (now < nextRequest) return;
        nextRequest = now + .5; BaselineRequests++;
        sync.Send(target, BaselineCommand, new JObject());
    }
    private void TryApplyPending(PNTimeSample sample)
    {
        if (pending == null || manager == null || !manager.IsReady || !sample.Ready) return;
        double stamp = PNAbilityReplicaWire.Nonnegative(pending["t"]);
        if (!PNOwnerImageAge.TryRead(sample, GuidId(pending["e"]), stamp, out double age))
        { Prepared = false; BeginRecovery(sample.Local); RequestBaseline(sample.Local); return; }
        var captured = Vector(pending["c"]); var rates = Vector(pending["q"]);
        var history = PNTimeService.For(sync.Session).History;
        var domains = new[] { PNClockDomain.AbilityGame, PNClockDomain.AbilityEntity, PNClockDomain.AbilityRaw };
        if (clocks.Count == 0)
            foreach (var domain in domains) clocks.Add(new PNClockProjection(history, new PNClockKey(sample.Epoch, GuidId(pending["w"]), PNAbilityReplicaWire.Long(pending["n"]), domain, target.Entity.Value, target.Generation)));
        var elapsed = new double[3];
        for (int i = 0; i < 3; i++)
        {
            var clock = clocks[i]; var kind = (PNAbilityClock)i;
            clock.Prepare(new PNClockAnchor(clock.Key, checked((long)pendingRevision), stamp, captured.Read(kind), rates.Read(kind)), Math.Max(stamp, sample.Host), sample.Uncertainty, stamp, true);
            elapsed[i] = Math.Max(0, clock.ReadAt(sample, Math.Max(stamp, sample.Host)) - captured.Read(kind));
            if (!clock.Prepared || clock.LastRead != PNClockRead.Ready) { Prepared = false; BeginRecovery(sample.Local); RequestBaseline(sample.Local); return; }
        }
        // All validation/projection precedes callbacks and publication, including pending images.
        var projected = pendingItems.Select(item =>
        {
            var definition = abilities.Definitions.FirstOrDefault(d => d.Id == item.Id);
            if (definition == null) return item;
            PNAbilityReplicaProjection.Validate(item, definition);
            return PNAbilityReplicaProjection.Advance(item, definition, elapsed[(int)definition.Clock]);
        }).ToArray();
        GetComponent<PNSkillRemoteInput>()?.PrepareOwnerImage((JObject)pending["r"], PNAbilityReplicaWire.Long(pending["k"]));
        Applying?.Invoke();
        AppliedCount = abilities.ApplyAuthorityReplica(projected, PNAbilityReplicaWire.Long(pending["k"]));
        manager.MarkReplicaReconciled();
        replicaRates = rates;
        AppliedRevision = pendingRevision; AppliedAge = age; Bindings = pendingBindings; Prepared = true; deadline = 0; lastImageLocal = sample.Local;
        pending = null; pendingItems = null; pendingBindings = null;
        Applied?.Invoke(); GetComponent<PNSkillRemoteInput>()?.CompleteOwnerImage();
    }
}

public sealed class PNSkillOwnerBaselineHandler : IPNSyncCommandHandler
{
    public void Execute(PNSyncCommandContext context)
    {
        var component = context.TargetObject as Component;
        var loader = component != null ? component.GetComponent<EveEntityLoader>() : null;
        if (loader?.LoadContext?.Identity.OwnerId == context.SenderOwnerId) component.GetComponent<PNSkillOwnerView>()?.Resync();
    }
}
#endif
