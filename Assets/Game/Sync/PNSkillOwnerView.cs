#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: What the OWNER of a player entity needs to see of its skill state (Docs/SkillNetworkingAPlan.md S1, N10): the Entity block `project_n:skill_owner`, sent only to the owner. It carries the entity's
//     Abilities (cooldowns, charges, gauge, stacks, states: one compact row each, time as "remaining at capture") and the view binding (slot -> profile). The authority writes it when an Ability or the
//     binding changed; the owner's client applies the Abilities into its own collection with `PNAbilityCollection.ApplyReplica` (the client keeps ticking them locally between updates, so a cooldown runs
//     smoothly) and keeps the binding as a read-only view. The authority itself never applies (it already has the truth). Display and prediction input only: not a trust boundary.
// VI: Phần trạng thái skill mà CHỦ NHÂN thấy: khối Entity `project_n:skill_owner`, chỉ gửi cho chủ. Chứa Ability (một dòng gọn mỗi cái) và binding ô -> profile. Authority ghi khi đổi; client chủ nhân áp vào
//     collection của mình bằng `ApplyReplica` rồi tự tick giữa hai lần cập nhật.
[DisallowMultipleComponent]
public sealed class PNSkillOwnerView : MonoBehaviour, IPNSyncEntityListener, IPNSyncStateSource, IPNSyncStateSink
{
    public static readonly EveId State = EveId.Parse("project_n:skill_owner");
    public const int StateBytes = 4096;
    private static readonly IReadOnlyDictionary<EveId, EveId> NoBindings = new Dictionary<EveId, EveId>();
    private PNSyncService sync;
    private PNSyncTarget target;
    private PNAbilityCollection abilities;
    private PNSkill skill;
    private readonly List<IDisposable> leases = new List<IDisposable>();
    public bool IsBound { get; private set; }
    public EveId StateId => State;
    // The view binding as the authority last told this machine (empty before the first image, and on the authority itself).
    public IReadOnlyDictionary<EveId, EveId> Bindings { get; private set; } = NoBindings;
    public ulong AppliedRevision { get; private set; }
    public int AppliedCount { get; private set; }

    public void OnBound(PNSyncService service, PNSyncTarget value)
    {
        sync = service; target = value; IsBound = true; Bindings = NoBindings; AppliedRevision = 0; AppliedCount = 0;
        var manager = GetComponent<PNAbilityManager>(); skill = GetComponent<PNSkill>();
        abilities = manager != null ? manager.Abilities : null;
        if (service.IsAuthority)
        {
            leases.Add(service.RegisterSource(value, this));
            if (abilities != null) abilities.Changed += OnAbilityChanged;
            if (skill != null) skill.BindingChanged += OnBindingChanged;
        }
        leases.Add(service.RegisterSink(value, this));
        if (service.IsAuthority) service.MarkDirty(value, State);
    }

    public void OnUnbound()
    {
        if (abilities != null) abilities.Changed -= OnAbilityChanged;
        if (skill != null) skill.BindingChanged -= OnBindingChanged;
        foreach (var lease in leases) lease.Dispose();
        leases.Clear(); IsBound = false; sync = null; abilities = null; skill = null; Bindings = NoBindings;
    }

    private void OnAbilityChanged(PNAbilityChange change) => Touch();
    private void OnBindingChanged(PNSkillBindingChange change) => Touch();
    private void Touch() { if (IsBound && sync != null && sync.IsAuthority) sync.MarkDirty(target, State); }

    public JToken Capture()
    {
        var rows = new JArray();
        if (abilities != null)
            foreach (var item in abilities.CaptureReplica())
                rows.Add(new JArray(item.Id.ToString(), item.Index, item.Amount, item.Maximum, item.Active ? 1 : 0, item.CooldownRemaining, item.HasDuration ? item.DurationRemaining : -1d));
        var bindings = new JObject();
        var state = skill != null ? skill.Bindings : null;
        if (state != null) foreach (var pair in state.Snapshot().OrderBy(x => x.Key.ToString(), StringComparer.Ordinal)) bindings[pair.Key.ToString()] = pair.Value.ToString();
        return new JObject { ["a"] = rows, ["b"] = bindings };
    }

    // Reads one captured block into replica rows (also used by the checks). Throws FormatException on a malformed value.
    public static List<PNAbilityReplicaItem> Parse(JToken value, out IReadOnlyDictionary<EveId, EveId> bindings)
    {
        if (!(value is JObject o) || !(o["a"] is JArray rows) || !(o["b"] is JObject b)) throw new FormatException("Skill owner state needs a and b.");
        if (rows.Count > 256) throw new FormatException("Skill owner state holds at most 256 Abilities.");
        var items = new List<PNAbilityReplicaItem>(rows.Count);
        foreach (var row in rows)
        {
            if (!(row is JArray r) || r.Count != 7) throw new FormatException("Skill owner Ability row.");
            double duration = Num(r[6]);
            items.Add(new PNAbilityReplicaItem(EveId.Parse((string)r[0]), (int)Num(r[1]), (int)Num(r[2]), (int)Num(r[3]), Num(r[4]) != 0, Num(r[5]), duration >= 0, Math.Max(0, duration)));
        }
        var map = new Dictionary<EveId, EveId>();
        foreach (var p in b.Properties()) { if (p.Value.Type != JTokenType.String) throw new FormatException("Skill owner binding."); map[EveId.Parse(p.Name)] = EveId.Parse((string)p.Value); }
        bindings = map; return items;
    }
    private static double Num(JToken token)
    {
        if (token == null || token.Type != JTokenType.Integer && token.Type != JTokenType.Float) throw new FormatException("Number required.");
        double v = token.Value<double>(); if (double.IsNaN(v) || double.IsInfinity(v)) throw new FormatException("Finite number required."); return v;
    }

    // A block can arrive before the entity's Abilities are ready (the sync layer hands a late reader the latest block while the entity is still being committed): it waits here and is applied as soon as
    // they are, so a block that comes early never fails the entity's own commit.
    private List<PNAbilityReplicaItem> pending; private ulong pendingRevision;
    public bool HasPending => pending != null;

    public void Apply(JToken value, ulong revision)
    {
        if (sync == null || sync.IsAuthority) return; // the authority holds the truth; only the owner's client applies
        var items = Parse(value, out var bindings);
        Bindings = bindings; pending = items; pendingRevision = revision; TryApplyPending();
    }

    private void Update() { if (pending != null) TryApplyPending(); }

    private void TryApplyPending()
    {
        var manager = GetComponent<PNAbilityManager>();
        if (pending == null || sync == null || manager == null || !manager.IsReady) return;
        AppliedCount = manager.Abilities.ApplyReplica(pending); AppliedRevision = pendingRevision; pending = null;
    }
}
#endif
