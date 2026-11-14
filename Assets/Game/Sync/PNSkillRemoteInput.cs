#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: A player's skill input over the wire (Docs/SkillNetworkingAPlan.md S2, no prediction yet). The OWNER's client does not run skills: its slot press becomes the command `project_n:skill_input`
//     (cast key, slot, phase, interaction, and the profile the client believes the slot holds). The AUTHORITY runs it as the owner's own input (`RequestSlot`), through the same decision and execution as an
//     offline press: it checks the sender owns the entity, that the slot still holds that profile (N4: matched per slot and profile, not by a global revision), then tries the press; a press that cannot start yet
//     (cooldown, still busy) waits in ONE short queue for at most `QueueSeconds` (N6) and is refused after that. The answer goes back to the owner only, as the owner-only block `project_n:skill_cast_result`
//     (latest acknowledged key plus the last refusals with a reason). Order within an owner is kept: a release never overtakes the press queued before it.
// VI: Input skill của người chơi qua mạng (S2, chưa dự đoán). Client chủ nhân không chạy skill: lệnh bấm ô thành lệnh `project_n:skill_input`; authority chạy như input của chính chủ, kiểm người gửi là chủ,
//     ô còn đúng profile (N4), thử bấm; chưa bắt đầu được thì xếp một hàng ngắn tối đa 250 ms (N6), quá hạn thì từ chối. Kết quả về chủ qua khối owner-only `project_n:skill_cast_result`.
[DisallowMultipleComponent]
public sealed class PNSkillRemoteInput : MonoBehaviour, IPNSyncEntityListener, IPNSkillInputForwarder
{
    public static readonly EveId Command = EveId.Parse("project_n:skill_input"), ResultState = EveId.Parse("project_n:skill_cast_result");
    public const int CommandBytes = 256, ResultBytes = 512, QueueLimit = 8, KeptRefusals = 4;
    public const double QueueSeconds = 0.25;

    public readonly struct Refusal
    {
        public readonly long Key; public readonly string Reason;
        public Refusal(long key, string reason) { Key = key; Reason = reason; }
    }
    private sealed class Pending { public long Key; public EveId Slot; public PNSkillInputEvent Input; public double Waited; }

    private sealed class Channel : IPNSyncStateSource, IPNSyncStateSink
    {
        private readonly PNSkillRemoteInput owner;
        public Channel(PNSkillRemoteInput owner) { this.owner = owner; }
        public EveId StateId => ResultState;
        public JToken Capture() => owner.CaptureResult();
        public void Apply(JToken value, ulong revision) => owner.ApplyResult(value);
    }

    private PNSyncService sync;
    private PNSyncTarget target;
    private PNSkill skill;
    private PNSkillOwnerView view;
    private Channel channel;
    private readonly List<IDisposable> leases = new List<IDisposable>();
    private readonly List<Pending> queue = new List<Pending>();
    private readonly List<Refusal> refusals = new List<Refusal>();
    private long nextKey;
    // Client: what this machine guessed for each press that has not been answered yet (S3). Keyed by the key of the press's Started input.
    private sealed class Press { public long Start, Release; public EveId Slot; public PNAbilityPrediction Undo; public bool Dashed; }
    private readonly Dictionary<long, Press> presses = new Dictionary<long, Press>();
    private readonly Dictionary<EveId, long> openPress = new Dictionary<EveId, long>();
    // Authority: for each slot, the key of the press being held and how many activations the skill had committed when it began.
    private readonly Dictionary<EveId, KeyValuePair<long, long>> track = new Dictionary<EveId, KeyValuePair<long, long>>();
    private bool pausing;
    // Client: the authority asked this owner not to guess new casts for now (it holds the entity, or it cannot cast).
    public bool PredictionPaused { get; private set; }
    public int PendingPredictions => presses.Count;
    public int PredictedCount { get; private set; }
    public int RolledBackCount { get; private set; }
    public bool IsBound { get; private set; }
    // Authority: the highest key accepted so far. Client: the same, as told by the authority.
    public long AckedKey { get; private set; }
    public IReadOnlyList<Refusal> Refusals => refusals;
    public int QueuedCount => queue.Count;
    public long LastSentKey => nextKey;

    public void OnBound(PNSyncService service, PNSyncTarget value)
    {
        sync = service; target = value; IsBound = true; AckedKey = 0; refusals.Clear(); queue.Clear(); nextKey = 0; presses.Clear(); openPress.Clear(); track.Clear(); pausing = false; PredictionPaused = false;
        skill = GetComponent<PNSkill>(); view = GetComponent<PNSkillOwnerView>(); channel = new Channel(this);
        if (service.IsAuthority) leases.Add(service.RegisterSource(value, channel));
        leases.Add(service.RegisterSink(value, channel));
        if (!service.IsAuthority && skill != null) skill.InputForwarder = this;
        if (service.IsAuthority && skill != null) skill.AcceptsRemoteInput = true;
    }

    public void OnUnbound()
    {
        (skill as PNPlayerSkill)?.CancelPredictedDash(); presses.Clear(); openPress.Clear(); track.Clear();
        if (skill != null && ReferenceEquals(skill.InputForwarder, this)) skill.InputForwarder = null;
        if (skill != null) skill.AcceptsRemoteInput = false;
        foreach (var lease in leases) lease.Dispose();
        leases.Clear(); queue.Clear(); IsBound = false; sync = null; skill = null; view = null; channel = null;
    }

    // ---- client: forward ----
    public bool IsActive => IsBound && sync != null && !sync.IsAuthority && view != null && view.Bindings.Count > 0;

    public bool Forward(EveId slot, PNSkillInputEvent input)
    {
        if (!IsActive || !view.Bindings.TryGetValue(slot, out var profile)) return false;
        var payload = new JObject { ["k"] = ++nextKey, ["s"] = slot.ToString(), ["p"] = (int)input.Phase, ["i"] = (int)input.Interaction, ["f"] = profile.ToString() };
        if (!sync.Send(target, Command, payload)) return false;
        Predict(slot, input, nextKey);
        return true;
    }

    // ---- client: guess ----
    // Called for every input event that was sent. A press (Started) of a slot the client can guess is run at once; its release marks when the answer is final.
    private void Predict(EveId slot, PNSkillInputEvent input, long key)
    {
        if (input.Phase == PNSkillInputPhase.Started)
        {
            if (openPress.TryGetValue(slot, out var stale) && presses.TryGetValue(stale, out var old) && old.Release == 0) old.Release = key - 1; // a new press supersedes one never released
            if (PredictionPaused || !(skill is PNPlayerSkill player)) return;
            var press = new Press { Start = key, Slot = slot }; bool guessed;
            if (slot.Equals(PNSkillSlots.Dash)) { guessed = player.TryPredictDash(out press.Undo); press.Dashed = guessed; }
            else guessed = skill.TryPredictSlotCooldown(slot, out press.Undo);
            if (!guessed) return;
            presses[key] = press; openPress[slot] = key; PredictedCount++;
        }
        else if (input.Phase == PNSkillInputPhase.Canceled && openPress.TryGetValue(slot, out var startKey) && presses.TryGetValue(startKey, out var open)) { open.Release = key; openPress.Remove(slot); }
    }

    private void RollBack(Press press)
    {
        presses.Remove(press.Start); if (openPress.TryGetValue(press.Slot, out var k) && k == press.Start) openPress.Remove(press.Slot);
        if (press.Dashed) (skill as PNPlayerSkill)?.CancelPredictedDash();
        press.Undo.Rollback(); RolledBackCount++;
    }

    // ---- authority: receive ----
    // Called by the command handler with the sender the transport authenticated. Malformed input throws (a protocol error); a stale or refused one is answered.
    public void Receive(long senderOwnerId, JObject payload)
    {
        if (!IsBound || sync == null || !sync.IsAuthority || skill == null) return;
        var loader = GetComponent<EveEntityLoader>();
        if (loader == null || loader.LoadContext == null || loader.LoadContext.Identity.OwnerId != senderOwnerId) return; // not this entity's owner: stale, not hostile
        long key = (long)Required(payload, "k"); var slot = EveId.Parse((string)payload["s"]);
        int phase = (int)Required(payload, "p"), interaction = (int)Required(payload, "i"); var profile = EveId.Parse((string)payload["f"]);
        if (key <= 0 || phase < 0 || phase > 2 || interaction < 0 || interaction > 3 || !PNSkillSlots.Player.Contains(slot)) throw new FormatException("Skill input.");
        if (GetComponent<PNStat>()?.IsDead == true) { Refuse(key, "dead"); return; }
        if (skill.Bindings == null || !skill.Bindings.TryGet(slot, out var current) || current != profile) { Refuse(key, "binding"); return; }
        if (queue.Count >= QueueLimit) { Refuse(key, "queue"); return; }
        queue.Add(new Pending { Key = key, Slot = slot, Input = new PNSkillInputEvent((PNSkillInputPhase)phase, (PNSkillInteraction)interaction) });
        Pump(0);
    }
    private static double Required(JObject o, string name)
    {
        var t = o[name]; if (t == null || t.Type != JTokenType.Integer) throw new FormatException("Skill input needs " + name + ".");
        return t.Value<double>();
    }

    private void Update()
    {
        if (!IsBound || sync == null || !sync.IsAuthority) return;
        RefreshPause();
        if (queue.Count > 0) Pump(Time.unscaledDeltaTime);
    }
    public bool IsPausing => pausing;
    public void RefreshPause() { bool now = ShouldPause(); if (now != pausing) { pausing = now; Touch(); } }
    // The owner must not guess new casts while the authority cannot take them: dead, held (world change, transfer), the network input gate closed, or every skill locked (N8).
    private bool ShouldPause()
    {
        if (skill == null) return false;
        var stat = GetComponent<PNStat>(); if (stat != null && stat.IsReady && stat.IsDead) return true;
        if (!PNLocalPlayerInput.AllowsRemote(GetComponent<EveEntityLoader>())) return true;
        var controls = GetComponent<PNControlManager>(); return controls != null && !controls.State.IsAllowed(PNControlIds.SkillAll);
    }

    // Runs the head of the queue in order. A press that cannot start waits (up to QueueSeconds in all); releases and holds follow their press.
    public void Pump(double delta)
    {
        if (delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        if (queue.Count > 0) queue[0].Waited += delta;
        while (queue.Count > 0)
        {
            var head = queue[0]; bool ok;
            if (!sync.IsAuthority || skill == null) { queue.Clear(); return; }
            long serial = skill.ActivationSerial;
            ok = skill.RequestSlot(head.Slot, head.Input);
            if (ok) { Observe(head, serial); Ack(head.Key); queue.RemoveAt(0); continue; }
            if (head.Input.Phase != PNSkillInputPhase.Started) { queue.RemoveAt(0); continue; } // no gesture to continue (its press was refused): nothing to do
            if (head.Waited < QueueSeconds) return;                                           // still hoping: the press may become valid
            Refuse(head.Key, "refused"); queue.RemoveAt(0);
        }
    }

    // A press that was delivered but began nothing (no cooldown, no charge, a blocked skill) is declined: the owner guessed a cast that never happened and must take it back (S3, N7).
    private void Observe(Pending head, long serialBefore)
    {
        if (head.Input.Phase == PNSkillInputPhase.Started) track[head.Slot] = new KeyValuePair<long, long>(head.Key, serialBefore);
        else if (head.Input.Phase == PNSkillInputPhase.Canceled && track.TryGetValue(head.Slot, out var open))
        {
            track.Remove(head.Slot);
            if (skill.ActivationSerial == open.Value) Refuse(open.Key, "declined");
        }
    }
    private void Ack(long key) { if (key > AckedKey) { AckedKey = key; Touch(); } }
    private void Refuse(long key, string reason)
    {
        refusals.Add(new Refusal(key, reason)); if (refusals.Count > KeptRefusals) refusals.RemoveAt(0); Touch();
    }
    private void Touch() { if (IsBound && sync != null && sync.IsAuthority) sync.MarkDirty(target, ResultState); }

    private JToken CaptureResult()
        => new JObject { ["ack"] = AckedKey, ["p"] = pausing ? 1 : 0, ["rej"] = new JArray(refusals.Select(r => new JArray(r.Key, r.Reason))) };

    private void ApplyResult(JToken value)
    {
        if (sync == null || sync.IsAuthority) return; // the authority already holds this
        if (!(value is JObject o) || o["ack"] == null || !(o["rej"] is JArray list) || list.Count > KeptRefusals) throw new FormatException("Skill cast result.");
        AckedKey = (long)o["ack"]; refusals.Clear(); PredictionPaused = o["p"] != null && o["p"].Type == JTokenType.Integer && (int)o["p"] != 0;
        foreach (var row in list) { if (!(row is JArray r) || r.Count != 2) throw new FormatException("Skill cast refusal."); refusals.Add(new Refusal((long)r[0], (string)r[1])); }
        // What the authority answered settles the guesses: a refused press is taken back, a released one that was acknowledged and not refused is confirmed.
        foreach (var refusal in refusals) if (presses.TryGetValue(refusal.Key, out var refused)) RollBack(refused);
        foreach (var done in presses.Values.Where(x => x.Release > 0 && x.Release <= AckedKey).ToArray()) presses.Remove(done.Start);
        if (PredictionPaused && skill is PNPlayerSkill paused) paused.CancelPredictedDash();
    }
}

// The authority side of `project_n:skill_input`: the sync layer hands over the authenticated sender; everything else is PNSkillRemoteInput.
public sealed class PNSkillInputCommandHandler : IPNSyncCommandHandler
{
    public void Execute(PNSyncCommandContext context)
    {
        var entity = context.TargetObject as Component; var remote = entity != null ? entity.GetComponent<PNSkillRemoteInput>() : null;
        if (remote == null) return;
        remote.Receive(context.SenderOwnerId, context.Payload as JObject ?? throw new FormatException("Skill input payload."));
    }
}
#endif
