#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

public enum PNHurtPhase { Early, Confirmed, Cancelled }

// What a hurt reaction is told. Early: this machine saw its own guess hit (Event is null, the point is the guess's). Confirmed: the host's event (Event is set; MatchedEarly says the early phase was already told for it,
// so an implementation that reacted early need not react twice). Cancelled: an early hit the host never confirmed (take the early reaction back).
public readonly struct PNHurtInfo
{
    public PNHurtPhase Phase { get; }
    public PNCombatEvent Event { get; }
    public bool MatchedEarly { get; }
    public string HitKey { get; }
    public bool HasPoint { get; }
    public Vector3 Point { get; }
    public PNHurtInfo(PNHurtPhase phase, PNCombatEvent combatEvent, bool matchedEarly, string hitKey, bool hasPoint, Vector3 point)
    { Phase = phase; Event = combatEvent; MatchedEarly = matchedEarly; HitKey = hitKey; HasPoint = hasPoint; Point = point; }
}

// EN: The reaction point of one entity (Docs/SpawnedObjectSyncPlan.md 4.4b part 3). An implementation (a character's visual, a sound, a hit spark) takes the signal with PNHurtSignal.For(stat) and subscribes; the engine adds no
//     reaction of its own. Every source of health loss (projectile, melee Action, Buff over time, trap) reaches it through the same combat event, so a reaction is written once. Hurt fires for every damaging hit
//     (Applied may be 0 inside an iframe or when Lorica took it all; read the event), Blocked when a gate refused the hit, Healed for health given, Died once when the event killed.
// VI: Diem phan ung cua mot entity. Lop cai dat (visual, am thanh, tia lua) lay tin hieu bang PNHurtSignal.For(stat) va dang ky; engine khong them phan ung nao. Moi nguon mat mau di qua cung mot su kien chien dau.
[DisallowMultipleComponent]
public sealed class PNHurtSignal : MonoBehaviour
{
    public event Action<PNHurtInfo> Hurt;
    public event Action<PNCombatEvent> Blocked, Healed, Died;
    private static readonly Dictionary<long, PNHurtSignal> signals = new Dictionary<long, PNHurtSignal>();
    private static bool hooked;
    private PNStat stat; private long bound;

    public static PNHurtSignal For(PNStat stat)
    {
        if (stat == null) throw new ArgumentNullException(nameof(stat));
        var signal = stat.GetComponent<PNHurtSignal>() ?? stat.gameObject.AddComponent<PNHurtSignal>();
        signal.Bind(); return signal;
    }
    private void Bind()
    {
        if (stat == null) stat = GetComponent<PNStat>();
        long id = stat != null && stat.IsReady ? stat.EntityId.Value : 0L;
        if (id == bound) return;
        if (bound != 0 && signals.TryGetValue(bound, out var old) && ReferenceEquals(old, this)) signals.Remove(bound);
        bound = id; if (id != 0) signals[id] = this;
        Hook();
    }
    private static void Hook()
    {
        if (hooked) return;
        hooked = true; PNCombatEvents.Observed += OnObserved; PNCombatEvents.Early += OnEarly; PNCombatEvents.EarlyCancelled += OnCancelled;
    }
    private void OnEnable() => Bind();
    private void Update() { Bind(); if (PNCombatEvents.PendingEarly != 0) PNCombatEvents.Expire(PNCombatEvents.Clock()); }
    private void OnDisable() => Unbind();
    private void OnDestroy() => Unbind();
    private void Unbind() { if (bound != 0 && signals.TryGetValue(bound, out var current) && ReferenceEquals(current, this)) signals.Remove(bound); bound = 0; }

    private static void OnObserved(PNCombatEvent e, bool matchedEarly)
    {
        if (!signals.TryGetValue(e.TargetId, out var signal)) return;
        var point = new Vector3(e.PointX, e.PointY, e.PointZ);
        try
        {
            switch (e.Kind)
            {
                case PNCombatEventKind.Damage:
                    signal.Hurt?.Invoke(new PNHurtInfo(PNHurtPhase.Confirmed, e, matchedEarly, e.HitKey, e.HasPoint, point));
                    if (e.Killed) signal.Died?.Invoke(e);
                    break;
                case PNCombatEventKind.Blocked: signal.Blocked?.Invoke(e); break;
                case PNCombatEventKind.Heal: signal.Healed?.Invoke(e); break;
            }
        }
        catch (Exception error) { Debug.LogException(error, signal); }
    }
    private static void OnEarly(PNEarlyHit hit)
    {
        if (!signals.TryGetValue(hit.TargetId, out var signal)) return;
        try { signal.Hurt?.Invoke(new PNHurtInfo(PNHurtPhase.Early, null, false, hit.HitKey, hit.HasPoint, new Vector3(hit.X, hit.Y, hit.Z))); } catch (Exception error) { Debug.LogException(error, signal); }
    }
    private static void OnCancelled(PNEarlyHit hit)
    {
        if (!signals.TryGetValue(hit.TargetId, out var signal)) return;
        try { signal.Hurt?.Invoke(new PNHurtInfo(PNHurtPhase.Cancelled, null, false, hit.HitKey, hit.HasPoint, new Vector3(hit.X, hit.Y, hit.Z))); } catch (Exception error) { Debug.LogException(error, signal); }
    }
}
#endif
