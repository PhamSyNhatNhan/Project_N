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
public sealed class PNSkillRemoteInput : MonoBehaviour, IPNSyncEntityListener, IPNSkillInputForwarder, IPNSkillDecisionSource, IPNSkillReportSink, IPNHitReportSink
{
    public static readonly EveId Command = EveId.Parse("project_n:skill_input"), ResultState = EveId.Parse("project_n:skill_cast_result");
    public const int CommandBytes = 256, ResultBytes = 512, QueueLimit = 8, KeptRefusals = 4;
    public const double QueueSeconds = 0.25;
    // S4-S5 and 4.4c-4.4d: the owner's machine reports the hits it decided (one command both ways, see PNHitReport: Dealt = my cast hit these, Taken = this object hit me) and the choices it made at decision points inside a cast.
    public static readonly EveId HitReportCommand = EveId.Parse("project_n:hit_report"), DecisionCommand = EveId.Parse("project_n:skill_decision");
    public const int HitReportCommandBytes = 512, DecisionCommandBytes = 256, MaxTargetsPerReport = 16, MaxHitsPerCast = 64, MaxDecisionsPerPoint = 4, KeyWindow = 64;
    public const float HitTolerance = 1.5f; public const double DecisionSeconds = 3;
    // G4: a skill names its decision points at the `Choose` call, so the host keeps no list; it only bounds how many answers wait (per point and in all) and for how long.
    public const int MaxDecisionsWaiting = 16;
    // Client: a guess nobody answered is taken back (seconds since its release, or since its press while still held).
    public const float GuessTimeoutReleased = 3f, GuessTimeoutHeld = 10f;

    public readonly struct Refusal
    {
        public readonly long Key; public readonly string Reason;
        public Refusal(long key, string reason) { Key = key; Reason = reason; }
    }
    private sealed class Pending { public long Key; public EveId Slot; public PNSkillInputEvent Input; public double Waited; public bool Guessed; }

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
    private long resultSequence, appliedResultSequence = -1;
    // Client: what this machine guessed for each press that has not been answered yet (S3). Keyed by the key of the press's Started input.
    private sealed class Press { public long Start, Release; public EveId Slot; public float Stamp; public readonly List<PNSkillInvocation> Casts = new List<PNSkillInvocation>(); public readonly List<long> CastKeys = new List<long>(); }
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
    // Test hook (Player fixtures): false = this machine does not guess casts, as when its guess is blocked; the input still goes to the authority.
    public bool GuessEnabled { get; set; } = true;
    // Test hook: true = guess even while the authority says it cannot take casts (the pause), so a cast the authority then refuses can be seen on a real wire.
    public bool IgnorePause { get; set; }
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
        sync = service; target = value; IsBound = true; AckedKey = 0; refusals.Clear(); queue.Clear(); presses.Clear(); openPress.Clear(); lastStart.Clear(); lastGuessSlot = default; dispatchSlot = default; track.Clear(); pausing = false; PredictionPaused = false; takenHits.Clear(); hitsPerCast.Clear(); decisions.Clear(); HitsApplied = HitsRejected = DecisionsTaken = DecisionsExpired = 0;
        resultSequence = checked(resultSequence + 1); appliedResultSequence = -1;
        skill = GetComponent<PNSkill>(); view = GetComponent<PNSkillOwnerView>(); channel = new Channel(this);
        // Keep transfers rebind the stream while the skill's cast counter lives on.
        // Never reuse lower input keys under that retained cast watermark.
        long floor = PNSkillCastKey.InputOf(skill?.LastCastKey ?? 0);
        highestKey = Math.Max(highestKey, floor); nextKey = Math.Max(nextKey, floor);
        if (service.IsAuthority) leases.Add(service.RegisterSource(value, channel));
        leases.Add(service.RegisterSink(value, channel));
        if (!service.IsAuthority && skill != null) { skill.InputForwarder = this; skill.ReportSink = this; skill.PredictedCastBegan += OnPredictedCast; }
        if (!service.IsAuthority && skill is PNPlayerSkill player) player.RefreshInput();
        if (!service.IsAuthority && view != null) { view.Applying += BeforeImage; view.Applied += Reapply; }
        if (service.IsAuthority && skill != null) { skill.AcceptsRemoteInput = true; skill.DecisionSource = this; }
        if (!service.IsAuthority) RegisterClaimSink();
    }

    public void OnUnbound()
    {
        UnregisterClaimSink();
        if (view != null) { view.Applying -= BeforeImage; view.Applied -= Reapply; }
        foreach (var guessed in presses.Values.Where(x => x.Casts.Count > 0).ToArray()) { try { RollBackCasts(guessed); } catch (Exception) { } }
        if (skill != null) skill.PredictedCastBegan -= OnPredictedCast;
        if (skill != null && ReferenceEquals(skill.ReportSink, this)) skill.ReportSink = null;
        presses.Clear(); openPress.Clear(); track.Clear();
        if (skill != null && ReferenceEquals(skill.InputForwarder, this)) skill.InputForwarder = null;
        if (skill != null) { skill.AcceptsRemoteInput = false; if (ReferenceEquals(skill.DecisionSource, this)) skill.DecisionSource = null; }
        foreach (var lease in leases) lease.Dispose();
        leases.Clear(); queue.Clear(); IsBound = false; sync = null; skill = null; view = null; channel = null;
    }

    // ---- client: forward ----
    public bool IsActive => IsBound && sync != null && !sync.IsAuthority && view != null && view.Prepared && view.Bindings.Count > 0;

    public bool Forward(EveId slot, PNSkillInputEvent input, Action predict)
    {
        if (!IsActive || !view.Bindings.TryGetValue(slot, out var profile)) return false;
        var payload = new JObject { ["k"] = ++nextKey, ["s"] = slot.ToString(), ["p"] = (int)input.Phase, ["i"] = (int)input.Interaction, ["f"] = profile.ToString() };
        // G7: the guess runs BEFORE the input is sent, so a decision it makes (`Choose`) reaches the authority ahead of the input and is waiting when the authority casts (an instant cast ends inside the input's own
        // dispatch). Hits it reports are held and follow the input (the authority takes a hit only for a cast it has seen).
        holdingHits = true; heldHits.Clear(); guessedNow = false;
        try { Predict(slot, input, nextKey, predict); } finally { holdingHits = false; }
        // Plan 4.4a (item 6): the authority learns that this machine guessed the cast, so its real projectiles leave target hits to this machine's reports; no "g" and the host sweeps for targets itself.
        if (guessedNow) payload["g"] = 1;
        bool sent = sync.Send(target, Command, payload);
        if (sent) foreach (var held in heldHits) SendHitReport(PNHitReport.DealtByCast(held.Key, held.Step), new[] { held.Target });
        heldHits.Clear();
        return sent;
    }

    // ---- client: guess ----
    // Called for every input event that was sent. The event runs through the skill's own binding path as a Predicted cast (G6a): whatever cast it begins is kept with the press (the slot's latest Started key),
    // and the authority's answer to that key settles it. The shared Dash still has its own guess (G6b).
    private readonly Dictionary<EveId, long> lastStart = new Dictionary<EveId, long>();
    private EveId dispatchSlot; private EveId lastGuessSlot;
    private void Predict(EveId slot, PNSkillInputEvent input, long key, Action run)
    {
        if (input.Phase == PNSkillInputPhase.Started)
        {
            lastStart[slot] = key;
            if (openPress.TryGetValue(slot, out var stale) && presses.TryGetValue(stale, out var old) && old.Release == 0) old.Release = key - 1; // a new press supersedes one never released
            if (PredictionPaused && !IgnorePause || !GuessEnabled || !(skill is PNPlayerSkill)) return;
        }
        else if (input.Phase == PNSkillInputPhase.Canceled && openPress.TryGetValue(slot, out var startKey) && presses.TryGetValue(startKey, out var open)) { open.Release = key; open.Stamp = Time.unscaledTime; openPress.Remove(slot); }
        if (run == null || !GuessEnabled || PredictionPaused && !IgnorePause && input.Phase != PNSkillInputPhase.Canceled) return;
        dispatchSlot = slot;
        if (skill != null) skill.PendingCastKey = key;
        try { run(); } finally { dispatchSlot = default; if (skill != null) skill.PendingCastKey = 0; }
    }

    // A guessed cast began (during an input's dispatch, or by itself: the next stroke of a held Repeat). It belongs to the press of the slot being dispatched, else to the press that began the last guess.
    private bool guessedNow;
    private void OnPredictedCast(PNSkillInvocation cast)
    {
        guessedNow = true;
        var slot = dispatchSlot.IsValid ? dispatchSlot : lastGuessSlot;
        if (!slot.IsValid || !lastStart.TryGetValue(slot, out var start) || start <= 0) return;
        if (!presses.TryGetValue(start, out var press)) { press = new Press { Start = start, Slot = slot, Stamp = Time.unscaledTime }; presses[start] = press; }
        press.Casts.Add(cast); press.CastKeys.Add(nextKey); if (press.Release == 0) openPress[slot] = start;
        lastGuessSlot = slot; PredictedCount++;
    }

    private void RollBack(Press press)
    {
        presses.Remove(press.Start); if (openPress.TryGetValue(press.Slot, out var k) && k == press.Start) openPress.Remove(press.Slot);
        RollBackCasts(press); RolledBackCount++;
    }
    // The guessed casts of a press, newest first: each is stopped if it still runs and its Ability writes are put back.
    private void RollBackCasts(Press press)
    {
        for (int i = press.Casts.Count - 1; i >= 0; i--) { try { skill?.RollbackPredictedCast(press.Casts[i]); } catch (Exception error) { Debug.LogException(error, this); } }
        press.Casts.Clear(); press.CastKeys.Clear();
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
        queue.Add(new Pending { Key = key, Slot = slot, Input = new PNSkillInputEvent((PNSkillInputPhase)phase, (PNSkillInteraction)interaction), Guessed = payload["g"] != null && (int)payload["g"] == 1 });
        Pump(0);
    }
    // ---- client: what the guessed cast reports ----
    // G6c: the framework names the cast (`PNSkillInvocation.CastKey`); the report travels under it.
    private bool holdingHits; private readonly List<(long Key, int Step, EveEntityId Target)> heldHits = new List<(long, int, EveEntityId)>();
    public void ReportHit(long castKey, PNStat victim, int step) { if (victim == null) return; SendHitReport(PNHitReport.DealtByCast(castKey, step), new[] { victim.EntityId }); }
    public void ReportDecision(long castKey, string point, JToken value) => SendDecision(castKey, point, value);

    // ---- client: report ----
    // The owner's machine sends one hit report on its own player (the command `project_n:hit_report`, plan 4.4d). Dealt: which entities its own copy of the cast hit at the step (held while a guess is being made: the
    // authority takes a hit only for a cast it has seen). Taken: which object hit this machine's player (`victims` is null: the victim is the sender's own player). False when it could not go out.
    public bool SendHitReport(in PNHitReport report, IReadOnlyList<EveEntityId> victims)
    {
        if (!IsActive || !report.IsValid((int)PNSkillCastKey.MaxStep)) return false;
        if (report.Role == PNHitReportRole.Dealt)
        {
            if (victims == null || victims.Count == 0 || victims.Count > MaxTargetsPerReport) return false;
            if (holdingHits) { foreach (var victim in victims) heldHits.Add((report.CastKey, report.Step, victim)); return true; }
            return sync.Send(target, HitReportCommand, new JObject { ["w"] = (int)PNHitReportRole.Dealt, ["k"] = report.CastKey, ["st"] = report.Step, ["h"] = new JArray(victims.Select(x => (object)x.Value)) });
        }
        // Taken by cast (V4b, a melee hit): the cast, the Action's ordinal in it (`st`) and the hit (`n`); Taken by object (V4a): the kind, the id and the detail.
        if (report.ByCast) return sync.Send(target, HitReportCommand, new JObject { ["w"] = (int)PNHitReportRole.Taken, ["k"] = report.CastKey, ["st"] = report.Step, ["n"] = report.Detail });
        return sync.Send(target, HitReportCommand, new JObject { ["w"] = (int)PNHitReportRole.Taken, ["t"] = (int)report.Kind, ["o"] = report.ObjectId, ["n"] = report.Detail });
    }
    // The Dealt report of a cast's hits (a convenience of fixtures and the Player scenarios).
    public bool SendHits(long castKey, int step, IReadOnlyList<EveEntityId> targets) => castKey > 0 && SendHitReport(PNHitReport.DealtByCast(castKey, step), targets);
    // The owner's client says what it chose at a decision point of the cast with this key.
    public bool SendDecision(long castKey, string point, JToken value)
    {
        if (!IsActive || castKey <= 0 || !PNSkillDecisions.IsValidPoint(point) || value == null) return false;
        return sync.Send(target, DecisionCommand, new JObject { ["k"] = castKey, ["n"] = point, ["v"] = value });
    }

    // ---- hit claims (plan 4.4c) ----
    private PNProjectileManager claimManager;
    public int ClaimsApplied { get; private set; }
    public int ClaimsRejected { get; private set; }
    // The melee part of the two counters above (hit reports that name a cast, V4b/V4c): evidence for the Player checks.
    public int MeleeClaimsApplied { get; private set; }
    public int MeleeClaimsRejected { get; private set; }
    // Of the Dealt reports no receiver took (plan 4.4d "late Dealt"): nothing of this cast was listening any more (its hitbox had ended: the report came too late) vs a hitbox was still listening and refused it (out of reach, already hit, not a legal target).
    public int DealtLate { get; private set; }
    public int DealtRefused { get; private set; }
    private void RegisterClaimSink()
    {
        try { claimManager = PNProjectileManager.Resolve(EveRegistrySession.Context, gameObject.scene); claimManager.RegisterClaimSink(this); }
        catch (InvalidOperationException) { claimManager = null; }
    }
    private void UnregisterClaimSink() { claimManager?.UnregisterClaimSink(this); claimManager = null; }
    // The player of this machine, only on the component of the entity this machine owns (the copies of other players answer null).
    public PNStat Claimant { get { var stat = GetComponent<PNStat>(); return IsActive && skill != null && skill.IsOwnerClient && stat != null && stat.IsReady && !stat.IsDead ? stat : null; } }
    // ---- authority: hit reports (one command, both ways) and decisions ----
    // A hit report from the machine of this entity's owner. Only that connection may send it (the entity is the reporter's own player: the caster of a Dealt report, the victim of a Taken one); the report is then routed to
    // whoever owns what it names: the running action's hitbox or the projectile manager (Dealt), the manager of the object's kind (Taken).
    public void ReceiveHitReport(long senderOwnerId, JObject payload)
    {
        if (!IsBound || sync == null || !sync.IsAuthority) return;
        var loader = GetComponent<EveEntityLoader>();
        if (loader == null || loader.LoadContext == null || loader.LoadContext.Identity.OwnerId != senderOwnerId) return;
        int role = (int)Required(payload, "w");
        if (role == (int)PNHitReportRole.Dealt) ReceiveDealt(payload);
        else if (role == (int)PNHitReportRole.Taken) ReceiveTaken(payload);
        else throw new FormatException("Hit report role.");
    }

    // Taken: the machine of this player says which object hit him. The kind says which manager owns the object: a projectile and a blast take one hit, an area takes "inside" (detail 1) or "outside" (detail 0).
    private void ReceiveTaken(JObject payload)
    {
        PNHitReport report;
        if (payload["k"] != null)
        {
            // By cast (V4b/V4c): a melee hit of an authority Action; the ticket its hitbox service left in the play's claim book decides.
            report = PNHitReport.FromWire(PNHitReportRole.Taken, (long)Required(payload, "k"), (int)Required(payload, "st"), 0, 0L, (int)Required(payload, "n"));
        }
        else
        {
            long objectId = (long)Required(payload, "o"); int kind = (int)Required(payload, "t"), detail = (int)Required(payload, "n");
            report = PNHitReport.FromWire(PNHitReportRole.Taken, 0L, 0, kind, objectId, detail);
        }
        if (!report.IsValid((int)PNSkillCastKey.MaxStep)) throw new FormatException("Hit report.");
        var stat = GetComponent<PNStat>(); if (stat == null || !stat.IsReady || stat.IsDead) { ClaimsRejected++; return; }
        bool applied = false;
        try
        {
            var scene = stat.gameObject.scene;
            IPNHitReportReceiver receiver;
            if (report.ByCast)
            {
                // A melee hit (V4b): the ticket the hitbox service left in the play's claim book decides (no book: no play to claim in).
                var book = PNHurtClaimBooks.Find(EveRegistrySession.Context);
                applied = book != null && book.AcceptTicket(PNClaimTicketKey.Melee(report.CastKey, report.Step, report.Detail), stat, HitTolerance);
                if (applied) { ClaimsApplied++; MeleeClaimsApplied++; } else { ClaimsRejected++; MeleeClaimsRejected++; }
                return;
            }
            if (report.Kind == PNObjectKind.Projectile) receiver = PNProjectileManager.Resolve(EveRegistrySession.Context, scene);
            else if (report.Kind == PNObjectKind.Blast) receiver = PNBlastManager.Resolve(EveRegistrySession.Context, scene);
            else receiver = PNAreaManager.Resolve(EveRegistrySession.Context, scene);
            applied = receiver.AcceptHitReport(report, stat, HitTolerance);
        }
        catch (InvalidOperationException) { }
        if (applied) ClaimsApplied++; else ClaimsRejected++;
    }

    // Dealt: the machine of this player says which entities its own copy of his cast hit.
    private void ReceiveDealt(JObject payload)
    {
        if (skill == null) return;
        long key = (long)Required(payload, "k"); int step = (int)Required(payload, "st");
        if (!(payload["h"] is JArray list) || list.Count == 0 || list.Count > MaxTargetsPerReport || key <= 0 || step < 0 || step > PNSkillCastKey.MaxStep) throw new FormatException("Skill hit report.");
        var ids = new long[list.Count]; for (int i = 0; i < ids.Length; i++) { if (list[i].Type != JTokenType.Integer) throw new FormatException("Skill hit target."); ids[i] = (long)list[i]; }
        // A cast the owner could not have started (a key beyond what this machine received, or long gone) is stale, not hostile.
        if (!InWindow(key)) { HitsRejected += ids.Length; return; }
        var ownStat = GetComponent<PNStat>(); if (ownStat == null || !ownStat.IsReady || ownStat.IsDead) { HitsRejected += ids.Length; return; }
        foreach (var id in ids)
        {
            if (id == 0) { HitsRejected++; continue; }
            hitsPerCast.TryGetValue(key, out int count);
            if (count >= MaxHitsPerCast || !takenHits.Add((key, step, id))) { HitsRejected++; continue; }
            hitsPerCast[key] = count + 1;
            PNStat victim = EveRegistrySession.Context.Entities.TryGet(new EveEntityId(id), out var registered) && registered != null ? registered.GetComponent<PNStat>() : null;
            bool applied = false;
            if (victim != null)
            {
                // Whoever owns what the step names takes it, each by its own rule: a running action's hitbox for an Action step; for a projectile's step (the place in its volley) the projectile manager, because the real
                // projectile outlives the cast and is found by the cast key. Each answers false for a report that is not its own.
                var report = PNHitReport.DealtByCast(key, step);
                applied = skill.DeliverHitReport(report, victim, HitTolerance);
                if (!applied)
                {
                    try { applied = PNProjectileManager.Resolve(EveRegistrySession.Context, ownStat.gameObject.scene).AcceptHitReport(report, victim, HitTolerance); }
                    catch (InvalidOperationException) { }
                }
            }
            if (applied) HitsApplied++;
            else { HitsRejected++; if (victim != null) { if (skill.HitReceiverCount == 0) DealtLate++; else DealtRefused++; } }
        }
        if (takenHits.Count > 1024) { takenHits.Clear(); hitsPerCast.Clear(); }
    }

    public void ReceiveDecision(long senderOwnerId, JObject payload)
    {
        if (!IsBound || sync == null || !sync.IsAuthority || skill == null) return;
        var loader = GetComponent<EveEntityLoader>();
        if (loader == null || loader.LoadContext == null || loader.LoadContext.Identity.OwnerId != senderOwnerId) return;
        long key = (long)Required(payload, "k"); string point = payload["n"]?.Type == JTokenType.String ? (string)payload["n"] : null; var value = payload["v"];
        if (key <= 0 || !PNSkillDecisions.IsValidPoint(point) || value == null) throw new FormatException("Skill decision.");
        if (!DecisionInWindow(key)) return;
        int same = 0; for (int i = 0; i < decisions.Count; i++) if (decisions[i].Point == point) same++;
        if (same >= MaxDecisionsPerPoint) { for (int i = 0; i < decisions.Count; i++) if (decisions[i].Point == point) { decisions.RemoveAt(i); DecisionsExpired++; break; } }
        if (decisions.Count >= MaxDecisionsWaiting) { decisions.RemoveAt(0); DecisionsExpired++; }
        decisions.Add(new Decision { Point = point, Value = value.DeepClone(), Key = key });
    }

    public float ReachSlack => HitTolerance;
    // A cast key belongs to an input this machine received recently (the cast key is the input's key shifted, see PNSkillCastKey).
    private bool InWindow(long castKey) { long input = PNSkillCastKey.InputOf(castKey); return input <= highestKey && input > highestKey - KeyWindow; }
    // A decision is sent ahead of the input it belongs to (the guess runs first, see Forward), so it may name the next input or two.
    private bool DecisionInWindow(long castKey) { long input = PNSkillCastKey.InputOf(castKey); return input <= highestKey + DecisionLead && input > highestKey - KeyWindow; }
    public const int DecisionLead = 2;
    // IPNSkillDecisionSource: the skill reaches a decision point of the cast with this key and asks for what the owner's client chose there (oldest first when it chose the same point more than once).
    public bool TryTake(long castKey, string point, out JToken value)
    {
        for (int i = 0; i < decisions.Count; i++)
            if (decisions[i].Key == castKey && decisions[i].Point == point) { value = decisions[i].Value; decisions.RemoveAt(i); DecisionsTaken++; return true; }
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
        // The image is the new truth under every guess: a refusal now restores what the image holds, and a guess still waiting is shown on top of it again.
        foreach (var press in presses.Values) foreach (var cast in press.Casts) cast.PredictionScope?.Rebase();
    }
    // G6b2: an image is about to replace the replica. A guess the authority has not taken yet (it began with an input the authority has not acknowledged) remembers what it wrote.
    private void BeforeImage()
    {
        foreach (var press in presses.Values)
            for (int i = 0; i < press.Casts.Count; i++)
                if (press.CastKeys[i] > AckedKey) press.Casts[i].PredictionScope?.CaptureOverlay();
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
            skill.PendingCastKey = head.Key; skill.PendingGuessed = head.Guessed;
            try { ok = skill.RequestSlot(head.Slot, head.Input); } finally { skill.PendingCastKey = 0; skill.PendingGuessed = false; }
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
    private void Touch() { if (IsBound && sync != null && sync.IsAuthority) { resultSequence = checked(resultSequence + 1); sync.MarkDirty(target, ResultState); view?.Resync(); } }

    private JToken CaptureResult()
        => new JObject { ["ack"] = AckedKey, ["p"] = pausing ? 1 : 0, ["n"] = resultSequence, ["h"] = Math.Max(highestKey,PNSkillCastKey.InputOf(skill?.LastCastKey ?? 0)), ["rej"] = new JArray(refusals.Select(r => new JArray(r.Key, r.Reason))) };

    public JObject CaptureOwnerResult() => (JObject)CaptureResult();
    public static void ValidateOwnerResult(JToken value)
    {
        if (!(value is JObject o) || PNAbilityReplicaWire.Long(o["ack"]) < 0 || !(o["rej"] is JArray rows) || rows.Count > KeptRefusals) throw new FormatException("Skill result.");
        if (o["p"] != null) PNAbilityReplicaWire.Flag(o["p"]);
        if (o["n"] != null && PNAbilityReplicaWire.Long(o["n"]) < 0) throw new FormatException("Skill result sequence.");
        if (o["h"] != null && PNAbilityReplicaWire.Long(o["h"]) < 0) throw new FormatException("Skill input floor.");
        foreach (var row in rows)
            if (!(row is JArray r) || r.Count != 2 || PNAbilityReplicaWire.Long(r[0]) < 1 || r[1].Type != JTokenType.String || ((string)r[1]).Length > 64) throw new FormatException("Skill refusal.");
    }
    private long imageAck;
    // This result was captured together with the Ability image.
    public void PrepareOwnerImage(JObject result, long capturedCastKey)
    {
        ValidateOwnerResult(result); imageAck = PNAbilityReplicaWire.Long(result["ack"]); ApplyResult(result);
        nextKey = Math.Max(nextKey,Math.Max(PNSkillCastKey.InputOf(capturedCastKey),PNAbilityReplicaWire.Long(result["h"] ?? new JValue(0))));
        // The host has processed release but did not run an extra predicted Repeat
        // stroke. Its coherent image closes that speculation too.
        foreach (var press in presses.Values.Where(x => x.Release > 0 && x.Release <= imageAck).ToArray())
            for (int i = press.Casts.Count - 1; i >= 0; i--)
                if (press.Casts[i].CastKey > capturedCastKey) skill?.RollbackPredictedCast(press.Casts[i]);
    }
    public void CompleteOwnerImage()
    {
        foreach (var done in presses.Values.Where(x => x.Release > 0 && x.Release <= imageAck).ToArray()) presses.Remove(done.Start);
    }

    private void ApplyResult(JToken value)
    {
        if (sync == null || sync.IsAuthority) return; // the authority already holds this
        ValidateOwnerResult(value); var o = (JObject)value; var list = (JArray)o["rej"];
        // Pause/refusal can change without advancing ACK. Both channels carry the
        // same logic sequence, so an older image cannot undo a newer fast result.
        if (o["n"] != null)
        {
            long sequence = PNAbilityReplicaWire.Long(o["n"]);
            if (sequence < appliedResultSequence) return;
            appliedResultSequence = sequence;
        }
        if ((long)o["ack"] < AckedKey) return;
        AckedKey = (long)o["ack"]; refusals.Clear(); PredictionPaused = o["p"] != null && (int)o["p"] != 0;
        foreach (var row in list) { if (!(row is JArray r) || r.Count != 2) throw new FormatException("Skill cast refusal."); refusals.Add(new Refusal((long)r[0], (string)r[1])); }
        // What the authority answered settles the guesses: a refused press is taken back, a released one that was acknowledged and not refused is confirmed.
        foreach (var refusal in refusals) if (presses.TryGetValue(refusal.Key, out var refused)) RollBack(refused);
        // An independent ACK can overtake skill_owner; only its image settles overlays.
        if (PredictionPaused && !IgnorePause && skill is PNPlayerSkill) { foreach (var cast in presses.Values.Where(x => x.Casts.Count > 0).ToArray()) RollBack(cast); }
    }
}

// The authority side of `project_n:hit_report`: the sync layer hands over the authenticated sender; everything else is PNSkillRemoteInput.ReceiveHitReport.
public sealed class PNHitReportCommandHandler : IPNSyncCommandHandler
{
    public void Execute(PNSyncCommandContext context)
    {
        var entity = context.TargetObject as Component; var remote = entity != null ? entity.GetComponent<PNSkillRemoteInput>() : null;
        if (remote == null) return;
        remote.ReceiveHitReport(context.SenderOwnerId, context.Payload as JObject ?? throw new FormatException("Hit report payload."));
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
