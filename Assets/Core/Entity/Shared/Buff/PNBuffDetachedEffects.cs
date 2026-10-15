using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using EveEngine;
using UnityEngine;

// Presentation resources with their own lifetime; never owned by an actor/presenter lease.
public sealed class PNBuffDetachedEffects : IDisposable
{
    private sealed class Effect { public double Expires; public PNBuffResourceScope Resources; }
    private static readonly ConditionalWeakTable<EveSessionContext, PNBuffDetachedEffects> services = new ConditionalWeakTable<EveSessionContext, PNBuffDetachedEffects>();
    private readonly EveSessionContext session;
    private readonly int maximumActive;
    private readonly double maximumLifetime;
    private readonly Func<double> clock;
    private readonly bool render;
    private readonly List<Effect> effects = new List<Effect>();
    private double lastTime;
    public bool IsDisposed { get; private set; }
    public int ActiveCount => effects.Count;
    public int DroppedCount { get; private set; }
    private PNBuffDetachedEffects(EveSessionContext session, int maximumActive, double maximumLifetime, Func<double> clock, bool render)
    { this.session = session; this.maximumActive = maximumActive; this.maximumLifetime = maximumLifetime; this.clock = clock; this.render = render; session.Stopping += Dispose; session.ConnectionFailed += Failed; }
    public static PNBuffDetachedEffects Configure(EveSessionContext session, int maximumActive, double maximumLifetimeSeconds, Func<double> monotonicSeconds, bool renderEnabled)
    {
        if (session == null || !session.IsActive || maximumActive < 1 || monotonicSeconds == null || services.TryGetValue(session, out _)) throw new ArgumentException("Explicit active-session presentation policy required.");
        PNBuffCollection.Seconds(maximumLifetimeSeconds, false);
        var result = new PNBuffDetachedEffects(session, maximumActive, maximumLifetimeSeconds, monotonicSeconds, renderEnabled); services.Add(session, result);
#if UNITY_5_3_OR_NEWER
        if (Application.isPlaying && renderEnabled) { var root = new GameObject("Buff detached presentation pump"); UnityEngine.Object.DontDestroyOnLoad(root); root.AddComponent<PNBuffDetachedPump>().Initialize(result); }
#endif
        return result;
    }
    public static PNBuffDetachedEffects For(EveSessionContext session) => session != null && services.TryGetValue(session, out var value) ? value : null;
    private double Now()
    { double value = clock(); PNBuffCollection.Seconds(value, true); if (value < lastTime) throw new InvalidOperationException("Monotonic presentation clock required."); return lastTime = value; }
    // The factory must use captured values, not actor/GameObject references or a presenter scope.
    public bool TryCreate(PNBuffEndReason reason, double lifetimeSeconds, Action<PNBuffResourceScope> createFromFrozenValues)
    {
        PNBuffCollection.Seconds(lifetimeSeconds, false); if (createFromFrozenValues == null) throw new ArgumentNullException(nameof(createFromFrozenValues));
        if (IsDisposed || !session.IsActive || !render || reason == PNBuffEndReason.Unload || reason == PNBuffEndReason.SessionEnded) return false;
        if (effects.Count >= maximumActive) { DroppedCount++; return false; }
        double expires = PNBuffCollection.Sum(Now(), Math.Min(lifetimeSeconds, maximumLifetime));
        var resources = new PNBuffResourceScope(Report);
        try { createFromFrozenValues(resources); effects.Add(new Effect { Expires = expires, Resources = resources }); return true; }
        catch (Exception error) { resources.Dispose(); Report(error); return false; }
    }
    public void Advance()
    {
        if (IsDisposed) return;
        if (!session.IsActive) { Dispose(); return; }
        double now = Now(); foreach (var effect in effects.Where(x => now >= x.Expires).ToArray()) { effects.Remove(effect); effect.Resources.Dispose(); }
    }
    private static void Report(Exception error) => Debug.LogException(error, null);
    private void Failed(string code, string message) => Dispose();
    public void Dispose()
    { if (IsDisposed) return; IsDisposed = true; session.Stopping -= Dispose; session.ConnectionFailed -= Failed; foreach (var effect in effects.ToArray()) effect.Resources.Dispose(); effects.Clear(); }
}
public sealed class PNBuffDetachedPump : MonoBehaviour
{
    private PNBuffDetachedEffects service;
    public void Initialize(PNBuffDetachedEffects value) { service = value; }
    private void Update() { if (service == null || service.IsDisposed) { Destroy(gameObject); return; } service.Advance(); }
}
