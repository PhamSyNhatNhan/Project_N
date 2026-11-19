using System;
using System.Collections.Generic;
using EveEngine;

// EN: The combat event channel (Docs/SpawnedObjectSyncPlan.md 4.4b part 3). The authority (host, or the one player offline) turns every damage / heal result it applies into one PNCombatEvent; every machine that shows
//     the world is told once (the host and an offline player directly, a client over the wire), and a hurt signal on the target entity (PNHurtSignal) hands it to the implementations that react. Health itself still
//     reaches a client through the stat image as before: nothing may change state because of an event, it only feeds presentation and UI (damage numbers, a flash, a sound, a log).
//     The shooter's own machine may know a hit before the host answers (its guess hit); that early phase is matched to the host's event by the hit key, and an early hit nobody confirms is cancelled.
// VI: Kenh su kien chien dau. Authority bien moi ket qua sat thuong / hoi mau da ap thanh mot PNCombatEvent; moi may hien world nhan mot lan; tin hieu bi trung (PNHurtSignal) tren entity chuyen cho cac lop cai dat phan ung.
//     May nguoi ban co the biet truoc cu trung (pha som), duoc ghep voi su kien cua host theo khoa cu trung; cu som khong duoc xac nhan thi bi huy. Su kien khong bao gio doi trang thai, chi phuc vu trinh bay va UI.
public enum PNCombatEventKind : byte { Damage = 1, Heal = 2, Blocked = 3 }

public sealed class PNCombatEvent
{
    public const int MaxTags = 8, MaxText = 64;
    public long Serial { get; set; }
    public PNCombatEventKind Kind { get; set; }
    public PNObjectWorld World { get; set; }
    public long TargetId { get; set; }
    public long SourceId { get; set; }
    // Damage: health taken / calculated amount before Lorica and the health floor / Lorica taken. Heal: health given / calculated amount / 0.
    public float Applied { get; set; }
    public float Calculated { get; set; }
    public float LoricaAbsorbed { get; set; }
    public float HealthBefore { get; set; }
    public float HealthAfter { get; set; }
    public bool Critical { get; set; }
    public bool Killed { get; set; }
    public bool LoricaBroke { get; set; }
    // The hit fell inside the target's iframe (it hurt less or not at all).
    public bool InIFrame { get; set; }
    public bool HasPoint { get; set; }
    public float PointX { get; set; }
    public float PointY { get; set; }
    public float PointZ { get; set; }
    // Names the hit across machines (projectile: cast key, index in the volley and hit index) so the shooter's early phase can be matched; null when the hit has no key.
    public string HitKey { get; set; }
    public string[] Tags { get; set; } = NoTags;
    // The host's clock when the result was applied (seconds, the same clock PNCombatEvents.Clock reads).
    public double Clock { get; set; }
    public static readonly string[] NoTags = new string[0];

    public bool TryValidate(out string error)
    {
        error = null;
        if (!Enum.IsDefined(typeof(PNCombatEventKind), Kind)) error = "Unknown combat event kind.";
        else if (TargetId <= 0) error = "A combat event names its target.";
        else if (SourceId < 0) error = "Source id cannot be negative.";
        else if (!Finite(Applied) || !Finite(Calculated) || !Finite(LoricaAbsorbed) || !Finite(HealthBefore) || !Finite(HealthAfter) || !Finite(PointX) || !Finite(PointY) || !Finite(PointZ) || double.IsNaN(Clock) || double.IsInfinity(Clock)) error = "Combat event numbers must be finite.";
        else if (Applied < 0f || Calculated < 0f || LoricaAbsorbed < 0f || HealthBefore < 0f || HealthAfter < 0f) error = "Combat event amounts cannot be negative.";
        else if (HitKey != null && HitKey.Length > MaxText) error = "Hit key too long.";
        else if (Tags == null || Tags.Length > MaxTags) error = "Too many tags.";
        else foreach (var tag in Tags) if (string.IsNullOrEmpty(tag) || tag.Length > MaxText) { error = "Bad tag."; break; }
        return error == null;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    // Rough size on a compact wire, for the packet budget.
    public int EstimatedBytes { get { int size = 72 + (HitKey?.Length ?? 0); if (Tags != null) foreach (var tag in Tags) size += 2 + tag.Length; return size; } }
}

// A hit the shooter's machine knows before the host answered.
public readonly struct PNEarlyHit
{
    public long TargetId { get; }
    public string HitKey { get; }
    public bool HasPoint { get; }
    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public PNEarlyHit(long targetId, string hitKey, bool hasPoint, float x, float y, float z) { TargetId = targetId; HitKey = hitKey; HasPoint = hasPoint; X = x; Y = y; Z = z; }
}

public interface IPNCombatEventTransport { void Send(PNCombatEvent combatEvent); }

public static class PNCombatEvents
{
    // How long an early hit waits for the host's event before it is cancelled (seconds); the network layer may set it from the round trip.
    public static double EarlyHoldSeconds = 1.5d;
    public const int MaxPendingEarly = 256;
    public static IPNCombatEventTransport Transport { get; set; }
    // The clock events are stamped with and early hits expire by; a test sets its own.
    private static readonly Func<double> DefaultClock = () => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
    private static Func<double> clock = DefaultClock;
    public static Func<double> Clock { get => clock; set => clock = value ?? DefaultClock; }
    // Told once per event on every machine. `matchedEarly` is true when this machine had already told the early phase of the same hit.
    public static event Action<PNCombatEvent, bool> Observed;
    public static event Action<PNEarlyHit> Early;
    public static event Action<PNEarlyHit> EarlyCancelled;
    // Evidence: events published (authority), delivered (client), told here, early hits told / matched / cancelled, events refused.
    public static int Published { get; private set; }
    public static int Delivered { get; private set; }
    public static int Told { get; private set; }
    public static int EarlyTold { get; private set; }
    public static int EarlyMatched { get; private set; }
    public static int EarlyCancels { get; private set; }
    public static int Refused { get; private set; }
    private static long nextSerial;
    private static readonly Dictionary<string, (PNEarlyHit Hit, double Deadline)> pending = new Dictionary<string, (PNEarlyHit, double)>();

    // The authority applied a result: stamps the event, tells this machine, and hands it to the wire.
    public static void Publish(PNCombatEvent combatEvent)
    {
        if (combatEvent == null) return;
        combatEvent.Serial = ++nextSerial; combatEvent.Clock = Clock();
        if (!combatEvent.TryValidate(out _)) { Refused++; return; }
        Published++;
        Tell(combatEvent);
        try { Transport?.Send(combatEvent); } catch (Exception) { Refused++; }
    }
    // A client received an event from the authority.
    public static void Deliver(PNCombatEvent combatEvent)
    {
        if (combatEvent == null || !combatEvent.TryValidate(out _)) { Refused++; return; }
        Delivered++; Tell(combatEvent);
    }
    // The shooter's machine saw its own guess hit `targetId` under `hitKey`: the early phase, kept until the event with that key comes or the hold ends.
    public static void RaiseEarly(long targetId, string hitKey, bool hasPoint, float x, float y, float z)
    {
        if (targetId <= 0 || string.IsNullOrEmpty(hitKey) || hitKey.Length > PNCombatEvent.MaxText) return;
        double now = Clock(); Expire(now);
        if (pending.ContainsKey(hitKey) || pending.Count >= MaxPendingEarly) return;
        var hit = new PNEarlyHit(targetId, hitKey, hasPoint, x, y, z);
        pending[hitKey] = (hit, now + Math.Max(.1d, EarlyHoldSeconds)); EarlyTold++;
        Safe(() => Early?.Invoke(hit));
    }
    // Cancels the early hits whose hold ended (called every frame by a hurt signal, or by a test).
    public static void Expire(double now)
    {
        if (pending.Count == 0) return;
        List<string> due = null;
        foreach (var pair in pending) if (now >= pair.Value.Deadline) (due ?? (due = new List<string>())).Add(pair.Key);
        if (due == null) return;
        foreach (var key in due) { var hit = pending[key].Hit; pending.Remove(key); EarlyCancels++; Safe(() => EarlyCancelled?.Invoke(hit)); }
    }
    public static int PendingEarly => pending.Count;
    private static void Tell(PNCombatEvent combatEvent)
    {
        bool matched = combatEvent.HitKey != null && pending.Remove(combatEvent.HitKey);
        if (matched) EarlyMatched++;
        Told++;
        Safe(() => Observed?.Invoke(combatEvent, matched));
    }
    private static void Safe(Action action)
    {
        try { action(); }
        catch (Exception error)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.LogException(error);
#else
            Console.Error.WriteLine(error);
#endif
        }
    }
    // Forgets everything (a new session, a test).
    public static void Reset()
    {
        pending.Clear(); Published = Delivered = Told = EarlyTold = EarlyMatched = EarlyCancels = Refused = 0; nextSerial = 0; Transport = null;
    }
}
