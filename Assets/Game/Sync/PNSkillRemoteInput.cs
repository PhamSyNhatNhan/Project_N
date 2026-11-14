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
public sealed class PNSkillRemoteInput : MonoBehaviour, IPNSyncEntityListener, IPNSkillInputForwarder, IPNSkillDecisionSource
{
    public static readonly EveId Command = EveId.Parse("project_n:skill_input"), ResultState = EveId.Parse("project_n:skill_cast_result");
    public const int CommandBytes = 256, ResultBytes = 512, QueueLimit = 8, KeptRefusals = 4;
    public const double QueueSeconds = 0.25;
    // S4-S5: the owner's client reports the hits it saw and the choices it made at decision points inside a cast.
    public static readonly EveId HitCommand = EveId.Parse("project_n:skill_hit"), DecisionCommand = EveId.Parse("project_n:skill_decision");
    public const int HitCommandBytes = 512, DecisionCommandBytes = 256, MaxTargetsPerReport = 16, MaxHitsPerCast = 64, MaxDecisionsPerPoint = 4, KeyWindow = 64;
    public const float HitTolerance = 1.5f; public const double DecisionSeconds = 3;
    public static readonly string[] DecisionPoints = { "ult_retarget", "empowered_strike", "repeat_continue" };
    // Client: a guess nobody answered is taken back (seconds since its release, or since its press while still held).
    public const float GuessTimeoutReleased = 3f, GuessTimeoutHeld = 10f;

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
    private sealed class Press { public long Start, Release; public EveId Slot; public PNAbilityPrediction Undo; public bool Dashed; public float Stamp; }
    private readonly Dictionary<long, Press> presses = new Dictionary<long, Press>();
    private readonly Dictionary<EveId, long> openPress = new Dictionary<EveId, long>();
    // Authority: for each slot, the key of the press being held and how many activations the skill had committed when it began.
    private readonly Dictionary<EveId, KeyValuePair<long, long>> track = new Dictionary<EveId, KeyValuePair<long, long>>();
    private bool pausing;
    // Authority: hits already taken ((cast key, step, target) once each) and decisions waiting for their point.
    private readonly HashSet<(long, int, long)> takenHits = new HashSet<(long, int, long)>();
    private readonly Dictionary<long, int> hitsPerCast = new Dictionary<long, int>();
    private sealed class Decision { public string Point; public JToken Value; public long Key; public double Age; }
    private readonly List<Decision> decisions = new List<Decision>();
    private long highestKey;
    public int HitsApplied { get; private set; }
    public int HitsRejected { get; private set; }
    public int DecisionsTaken { get; private set; }
    public int DecisionsExpired { get; private set; }
    public int DecisionsWaiting => decisions.Count;
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
        sync = service; target = value; IsBound = true; AckedKey = 0; refusals.Clear(); queue.Clear(); nextKey = 0; presses.Clear(); openPress.Clear(); track.Clear(); pausing = false; PredictionPaused = false; takenHits.Clear(); hitsPerCast.Clear(); decisions.Clear(); highestKey = 0; HitsApplied = HitsRejected = DecisionsTaken = DecisionsExpired = 0;
        skill = GetComponent<PNSkill>(); view = GetComponent<PNSkillOwnerView>(); channel = new Channel(this);
        if (service.IsAuthority) leases.Add(service.RegisterSource(value, channel));
        leases.Add(service.RegisterSink(value, channel));
        if (!service.IsAuthority && skill != null) skill.InputForwarder = this;
        if (!service.IsAuthority && view != null) view.Applied += Reapply;
        if (service.IsAuthority && skill != null) { skill.AcceptsRemoteInput = true; skill.DecisionSource = this; }
    }

    public void OnUnbound()
    {
        if (view != null) view.Applied -= Reapply;
        (skill as PNPlayerSkill)?.CancelPredictedDash(); presses.Clear(); openPress.Clear(); track.Clear();
        if (skill != null && ReferenceEquals(skill.InputForwarder, this)) skill.InputForwarder = null;
        if (skill != null) { skill.AcceptsRemoteInput = false; if (ReferenceEquals(skill.DecisionSource, this)) skill.DecisionSource = null; }
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
            var press = new Press { Start = key, Slot = slot, Stamp = Time.unscaledTime }; bool guessed;
            if (slot.Equals(PNSkillSlots.Dash)) { guessed = player.TryPredictDash(out press.Undo); press.Dashed = guessed; }
            else guessed = skill.TryPredictSlotCooldown(slot, out press.Undo);
            if (!guessed) return;
            presses[key] = press; openPress[slot] = key; PredictedCount++;
        }
        else if (input.Phase == PNSkillInputPhase.Canceled && openPress.TryGetValue(slot, out var startKey) && presses.TryGetValue(startKey, out var open)) { open.Release = key; open.Stamp = Time.unscaledTime; openPress.Remove(slot); }
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
        if (key > highestKey) highestKey = key;
        if (key <= 0 || phase < 0 || phase > 2 || interaction < 0 || interaction > 3 || !PNSkillSlots.Player.Contains(slot)) throw new FormatException("Skill input.");
        if (GetComponent<PNStat>()?.IsDead == true) { Refuse(key, "dead"); return; }
        if (skill.Bindings == null || !skill.Bindings.TryGet(slot, out var current) || current != profile) { Refuse(key, "binding"); return; }
        if (queue.Count >= QueueLimit) { Refuse(key, "queue"); return; }
        queue.Add(new Pending { Key = key, Slot = slot, Input = new PNSkillInputEvent((PNSkillInputPhase)phase, (PNSkillInteraction)interaction) });
        Pump(0);
    }
    // ---- client: report ----
    // The owner's client says which entities its own copy of the cast hit at `step`. The authority applies them only through the running action (ReceiveHits).
    public bool SendHits(long castKey, int step, IReadOnlyList<EveEntityId> targets)
    {
        if (!IsActive || targets == null || targets.Count == 0 || targets.Count > MaxTargetsPerReport || castKey <= 0 || step < 0) return false;
        return sync.Send(target, HitCommand, new JObject { ["k"] = castKey, ["st"] = step, ["h"] = new JArray(targets.Select(x => (object)x.Value)) });
    }
    // The owner's client says what it chose at a decision point of the cast with this key.
    public bool SendDecision(long castKey, string point, JToken value)
    {
        if (!IsActive || castKey <= 0 || Array.IndexOf(DecisionPoints, point) < 0 || value == null) return false;
        return sync.Send(target, DecisionCommand, new JObject { ["k"] = castKey, ["n"] = point, ["v"] = value });
    }

    // ---- authority: hits and decisions ----
    public void ReceiveHits(long senderOwnerId, JObject payload)
    {
        if (!IsBound || sync == null || !sync.IsAuthority || skill == null) return;
        var loader = GetComponent<EveEntityLoader>();
        if (loader == null || loader.LoadContext == null || loader.LoadContext.Identity.OwnerId != senderOwnerId) return;
        long key = (long)Required(payload, "k"); int step = (int)Required(payload, "st");
        if (!(payload["h"] is JArray list) || list.Count == 0 || list.Count > MaxTargetsPerReport || key <= 0 || step < 0 || step > 64) throw new FormatException("Skill hit report.");
        var ids = new long[list.Count]; for (int i = 0; i < ids.Length; i++) { if (list[i].Type != JTokenType.Integer) throw new FormatException("Skill hit target."); ids[i] = (long)list[i]; }
        // A cast the owner could not have started (a key beyond what this machine received, or long gone) is stale, not hostile.
        if (key > highestKey || key <= highestKey - KeyWindow) { HitsRejected += ids.Length; return; }
        var ownStat = GetComponent<PNStat>(); if (ownStat == null || !ownStat.IsReady || ownStat.IsDead) { HitsRejected += ids.Length; return; }
        foreach (var id in ids)
        {
            if (id == 0) { HitsRejected++; continue; }
            hitsPerCast.TryGetValue(key, out int count);
            if (count >= MaxHitsPerCast || !takenHits.Add((key, step, id))) { HitsRejected++; continue; }
            hitsPerCast[key] = count + 1;
            PNStat victim = EveRegistrySession.Context.Entities.TryGet(new EveEntityId(id), out var registered) && registered != null ? registered.GetComponent<PNStat>() : null;
            if (victim != null && skill.DeliverReportedHit(victim, step, HitTolerance)) HitsApplied++; else HitsRejected++;
        }
        if (takenHits.Count > 1024) { takenHits.Clear(); hitsPerCast.Clear(); }
    }

    public void ReceiveDecision(long senderOwnerId, JObject payload)
    {
        if (!IsBound || sync == null || !sync.IsAuthority || skill == null) return;
        var loader = GetComponent<EveEntityLoader>();
        if (loader == null || loader.LoadContext == null || loader.LoadContext.Identity.OwnerId != senderOwnerId) return;
        long key = (long)Required(payload, "k"); string point = payload["n"]?.Type == JTokenType.String ? (string)payload["n"] : null; var value = payload["v"];
        if (key <= 0 || point == null || Array.IndexOf(DecisionPoints, point) < 0 || value == null) throw new FormatException("Skill decision.");
        if (key > highestKey || key <= highestKey - KeyWindow) return;
        int same = 0; for (int i = 0; i < decisions.Count; i++) if (decisions[i].Point == point) same++;
        if (same >= MaxDecisionsPerPoint) { for (int i = 0; i < decisions.Count; i++) if (decisions[i].Point == point) { decisions.RemoveAt(i); DecisionsExpired++; break; } }
        decisions.Add(new Decision { Point = point, Value = value.DeepClone(), Key = key });
    }

    // IPNSkillDecisionSource: the skill reaches a decision point and asks for what the owner's client chose, oldest first.
    public bool TryTake(string point, out JToken value)
    {
        for (int i = 0; i < decisions.Count; i++)
            if (decisions[i].Point == point) { value = decisions[i].Value; decisions.RemoveAt(i); DecisionsTaken++; return true; }
        value = null; return false;
    }
    public void ExpireDecisions(double delta)
    {
        for (int i = decisions.Count - 1; i >= 0; i--) { decisions[i].Age += delta; if (decisions[i].Age > DecisionSeconds) { decisions.RemoveAt(i); DecisionsExpired++; } }
    }

    private static double Required(JObject o, string name)
    {
        var t = o[name]; if (t == null || t.Type != JTokenType.Integer) throw new FormatException("Skill input needs " + name + ".");
        return t.Value<double>();
    }

    // A new image of the owner's Abilities came in: every guess still waiting is spent again on top of it, with a fresh undo.
    private void Reapply()
    {
        foreach (var press in presses.Values)
        {
            if (press.Release > 0 && press.Release <= AckedKey) continue;
            PNAbilityPrediction undo = default; bool again = press.Dashed ? (skill as PNPlayerSkill)?.RespendDash(out undo) == true : skill.TryPredictSlotCooldown(press.Slot, out undo);
            if (again) press.Undo = undo;
        }
    }

    public void ExpireGuesses(float now)
    {
        if (presses.Count > 0) foreach (var late in presses.Values.Where(x => now - x.Stamp > (x.Release > 0 ? GuessTimeoutReleased : GuessTimeoutHeld)).ToArray()) RollBack(late);
    }

    private void Update()
    {
        if (IsBound && sync != null && !sync.IsAuthority)
        {
            ExpireGuesses(Time.unscaledTime);
            return;
        }
        if (!IsBound || sync == null || !sync.IsAuthority) return;
        RefreshPause(); if (decisions.Count > 0) ExpireDecisions(Time.unscaledDeltaTime);
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
        view?.Resync(); // the owner may have guessed a cooldown the authority does not hold: send the true Abilities again
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

public sealed class PNSkillHitCommandHandler : IPNSyncCommandHandler
{
    public void Execute(PNSyncCommandContext context)
    {
        var entity = context.TargetObject as Component; var remote = entity != null ? entity.GetComponent<PNSkillRemoteInput>() : null;
        if (remote == null) return;
        remote.ReceiveHits(context.SenderOwnerId, context.Payload as JObject ?? throw new FormatException("Skill hit payload."));
    }
}

public sealed class PNSkillDecisionCommandHandler : IPNSyncCommandHandler
{
    public void Execute(PNSyncCommandContext context)
    {
        var entity = context.TargetObject as Component; var remote = entity != null ? entity.GetComponent<PNSkillRemoteInput>() : null;
        if (remote == null) return;
        remote.ReceiveDecision(context.SenderOwnerId, context.Payload as JObject ?? throw new FormatException("Skill decision payload."));
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
