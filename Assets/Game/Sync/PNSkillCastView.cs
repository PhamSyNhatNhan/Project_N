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
    public const int StateBytes = 1024, Kept = 6, MaxText = 48;
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
        sync = service; target = value; IsBound = true; PlayedCount = 0; lastSeen = -1; serial = 0; recent.Clear();
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
        leases.Clear(); IsBound = false; sync = null; skill = null; recent.Clear();
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
        return new JObject { ["n"] = serial, ["e"] = rows };
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
    private void Update() { if (skill != null && skill.ObservedActionCount > 0) skill.AdvanceObservedActions(Time.deltaTime); }

    public void Apply(JToken value, ulong revision)
    {
        if (sync == null || sync.IsAuthority) return; // the authority drew the cast itself
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
