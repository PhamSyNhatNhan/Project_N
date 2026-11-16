#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: What the OTHER players see of a cast (Docs/SkillNetworkingAPlan.md S6): the Entity block `project_n:skill_cast`, sent to every client. The authority keeps the last few pictures its skill drew
//     (`PNSkill.CastEvent`: a slash, a motion trail, Burst on / off) with a serial number; a client plays the ones it has not seen through `PNSkill.PlayCastEvent` and nothing else, so an observer never
//     runs a cast, moves a body or hits anything. The owner's own client drew the cast ahead and ignores the block. The first block a client gets only sets the serial (a late joiner sees from the next cast).
//     Display only: not a trust boundary.
// VI: Phần cast mà NGƯỜI KHÁC thấy: khối Entity `project_n:skill_cast` gửi cho mọi client. Authority giữ vài hình skill vừa vẽ (số thứ tự tăng dần); client phát những cái chưa thấy qua `PlayCastEvent`.
[DisallowMultipleComponent]
public sealed class PNSkillCastView : MonoBehaviour, IPNSyncEntityListener, IPNSyncStateSource, IPNSyncStateSink
{
    public static readonly EveId State = EveId.Parse("project_n:skill_cast");
    public const int StateBytes = PNActionRunStore.WireBytes, Kept = 6, MaxText = 48;
    private double nextRunCapture;
    private ulong receivedTransportRevision;
    public long AppliedRunRevision { get; private set; }
    public IReadOnlyList<PNActionRunRecord> ReceivedRuns { get; private set; } = Array.Empty<PNActionRunRecord>();
    private PNSyncService sync;
    private PNSyncTarget target;
    private PNSkill skill;
    private readonly List<(long Serial, PNSkillCastEvent Event)> recent = new List<(long, PNSkillCastEvent)>();
    private long serial, lastSeen = -1;
    private readonly List<IDisposable> leases = new List<IDisposable>();
    public bool IsBound { get; private set; }
    public EveId StateId => State;
    public int PlayedCount { get; private set; }
    public long LastSerial => lastSeen;

    public void OnBound(PNSyncService service, PNSyncTarget value)
    {
        sync = service; target = value; IsBound = true; PlayedCount = 0; lastSeen = -1; serial = 0; recent.Clear(); nextRunCapture = 0; receivedTransportRevision = 0; AppliedRunRevision = 0; ReceivedRuns = Array.Empty<PNActionRunRecord>();
        skill = GetComponent<PNSkill>();
        if (service.IsAuthority)
        {
            leases.Add(service.RegisterSource(value, this));
            if (skill != null) skill.CastEvent += OnCastEvent;
        }
        leases.Add(service.RegisterSink(value, this));
        if (service.IsAuthority) service.MarkDirty(value, State);
    }

    public void OnUnbound()
    {
        if (skill != null) { skill.CastEvent -= OnCastEvent; skill.EndObservedActions(); }
        foreach (var lease in leases) lease.Dispose();
        leases.Clear(); IsBound = false; sync = null; skill = null; recent.Clear(); ReceivedRuns = Array.Empty<PNActionRunRecord>(); AppliedRunRevision = 0;
    }

    private void OnCastEvent(PNSkillCastEvent value)
    {
        if (!IsBound || sync == null || !sync.IsAuthority) return;
        recent.Add((++serial, value)); if (recent.Count > Kept) recent.RemoveAt(0);
        sync.MarkDirty(target, State);
    }

    public JToken Capture()
    {
        var rows = new JArray();
        foreach (var item in recent) rows.Add(new JArray(item.Serial, item.Event.Kind, item.Event.Detail, item.Event.X, item.Event.Z, item.Event.Duration));
        // The old immediate observer remains temporarily in T5.1. Charge its UTF8 bytes
        // separately so it cannot consume the new admitted terminal reservation.
        while (rows.Count > 0 && PNActionRunWire.Bytes(rows) > PNActionRunStore.LegacyBytes) rows.RemoveAt(0);
        var sample = PNTimeService.For(sync.Session).Sample();
        var world = GetComponent<EveEntityLoader>()?.LoadContext?.Identity.World;
        var worldId = world?.InstanceId ?? sample.Epoch; var incarnation = world?.Incarnation ?? 1;
        var records = skill?.CaptureActionRuns(sample.Epoch, worldId, incarnation, target.Generation, sample.Local) ?? Array.Empty<PNActionRunRecord>();
        return new JObject { ["v"] = 2, ["n"] = serial, ["e"] = rows, ["a"] = new JObject {
            ["v"] = PNActionRunWire.Schema, ["e"] = sample.Epoch.ToString("N"), ["w"] = worldId.ToString("N"), ["n"] = incarnation,
            ["i"] = target.Entity.Value, ["g"] = target.Generation, ["t"] = sample.Local, ["r"] = skill?.ActionRuns?.Revision ?? 0,
            ["f"] = true, ["b"] = 0, ["d"] = skill?.ActionRuns?.Rejected ?? 0, ["a"] = PNActionRunWire.Capture(records) } };
    }

    // Reads one block; throws FormatException on a malformed value (also used by the checks).
    public static List<(long Serial, PNSkillCastEvent Event)> Parse(JToken value, out long latest)
    {
        if (!(value is JObject o) || !(o["e"] is JArray rows) || o["n"] == null || o["n"].Type != JTokenType.Integer) throw new FormatException("Skill cast block needs n and e.");
        latest = o["n"].Value<long>();
        if (rows.Count > Kept * 2) throw new FormatException("Skill cast block holds at most " + Kept * 2 + " events.");
        var list = new List<(long, PNSkillCastEvent)>(rows.Count); long previous = 0;
        foreach (var row in rows)
        {
            if (!(row is JArray r) || r.Count != 6 || r[0].Type != JTokenType.Integer || r[1].Type != JTokenType.String || r[2].Type != JTokenType.String) throw new FormatException("Skill cast event row.");
            string kind = (string)r[1], detail = (string)r[2]; long n = r[0].Value<long>();
            if (kind.Length > MaxText || detail.Length > MaxText || n <= previous) throw new FormatException("Skill cast event text or order.");
            previous = n; list.Add((n, new PNSkillCastEvent(kind, detail, Finite(r[3]), Finite(r[4]), Math.Max(0f, Math.Min(30f, Finite(r[5]))))));
        }
        return list;
    }
    private static float Finite(JToken token)
    {
        if (token == null || token.Type != JTokenType.Integer && token.Type != JTokenType.Float) throw new FormatException("Number required.");
        float v = token.Value<float>(); if (float.IsNaN(v) || float.IsInfinity(v)) throw new FormatException("Finite number required."); return v;
    }

    // Observed Actions advance by their own timelines (the skill has no per-frame hook of its own on an observer).
    private void Update()
    {
        if (skill != null && skill.ObservedActionCount > 0) skill.AdvanceObservedActions(Time.deltaTime);
        if (sync != null && sync.IsAuthority && skill?.ActionRuns != null)
        {
            var stamp = PNTimeService.For(sync.Session).Sample().Local;
            if (stamp >= nextRunCapture && skill.ActionRuns.NeedsCapture(stamp)) { nextRunCapture = stamp + .1; sync.MarkDirty(target, State); }
        }
    }
    private void LateUpdate()
    {
        if (sync != null && sync.IsAuthority && sync.Session.IsActive && skill?.ActionRuns != null)
            skill.SealActionRuns(PNTimeService.For(sync.Session).Sample().Local);
    }

    public static List<PNActionRunRecord> ParseRuns(JToken value, out JObject envelope)
    {
        if (!(value is JObject root) || PNActionRunWire.Long(root["v"]) != 2 || !(root["a"] is JObject a) || PNActionRunWire.Bytes(value) > StateBytes) throw new FormatException("Skill cast schema2/bytes.");
        if (root.Count != 4 || a.Count != 12 || PNActionRunWire.Long(a["v"]) != PNActionRunWire.Schema || a["f"]?.Type != JTokenType.Boolean || !(bool)a["f"] || PNActionRunWire.Long(a["b"]) != 0) throw new FormatException("Action envelope/full set.");
        GuidField(a["e"]); GuidField(a["w"]);
        if (PNActionRunWire.Long(a["n"]) < 1 || PNActionRunWire.Long(a["i"]) < 1 || PNActionRunWire.Long(a["g"]) < 1 || PNActionRunWire.Long(a["r"]) < 0 || PNActionRunWire.Long(a["d"]) < 0) throw new FormatException("Action scope/revision.");
        envelope = a; return PNActionRunWire.Parse(a["a"], PNActionRunWire.Nonnegative(a["t"]));
    }
    private static Guid GuidField(JToken token)
    { if (token?.Type != JTokenType.String || !Guid.TryParseExact((string)token, "N", out var id) || id == Guid.Empty) throw new FormatException("Action epoch/world."); return id; }

    public void Apply(JToken value, ulong revision)
    {
        if (sync == null || sync.IsAuthority || revision <= receivedTransportRevision) return; // the authority drew the cast itself
        var runs = ParseRuns(value, out var envelope);
        var sample = PNTimeService.For(sync.Session).Sample(); var world = GetComponent<EveEntityLoader>()?.LoadContext?.Identity.World;
        if (GuidField(envelope["e"]) != sample.Epoch || GuidField(envelope["w"]) != (world?.InstanceId ?? sample.Epoch) || PNActionRunWire.Long(envelope["n"]) != (long)(world?.Incarnation ?? 1)
            || PNActionRunWire.Long(envelope["i"]) != target.Entity.Value || PNActionRunWire.Long(envelope["g"]) != target.Generation) return;
        // Validated shadow state; T5.2 supplies delivery/recovery, T5.3 consumes it.
        long runRevision = PNActionRunWire.Long(envelope["r"]);
        if (runRevision < AppliedRunRevision) throw new FormatException("Action stream revision regressed.");
        if (receivedTransportRevision != 0 && runRevision == AppliedRunRevision && !JToken.DeepEquals(PNActionRunWire.Capture(ReceivedRuns), envelope["a"])) throw new FormatException("Changed admitted set without revision.");
        PNActionRunWire.ValidateSuccessor(ReceivedRuns, runs);
        ReceivedRuns = runs.AsReadOnly(); AppliedRunRevision = runRevision; receivedTransportRevision = revision;
        var events = Parse(value, out long latest);
        if (lastSeen < 0 || latest < lastSeen) { lastSeen = latest; return; } // first image (or a new incarnation): nothing to replay
        bool owner = skill != null && skill.IsOwnerClient;
        foreach (var item in events)
        {
            if (item.Serial <= lastSeen) continue;
            lastSeen = item.Serial;
            if (owner || skill == null) continue;
            try { skill.PlayObserved(item.Event); PlayedCount++; } catch (Exception error) { Debug.LogException(error, this); }
        }
        if (latest > lastSeen) lastSeen = latest;
    }
}
#endif
