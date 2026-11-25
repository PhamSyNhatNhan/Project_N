using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: `project_n:gura_burst_area` (Docs/GuraCharacter.md 4.4, D2/D5): the tide of Gura's Burst. A new area class (not the time-scale area) that shares its service: enemies take the time-scale Buff and projectiles / blasts / areas
//     take owned clock modifiers (-reduction) through the world's PNTimeTargets; every PLAYER inside gets the `gura_blessing` Buff (re-applied while inside, so it lapses soon after leaving). It carries the trait
//     `solizeitastronhringur` (one such area per world instance; a newer one ends the older with `Replaced`). It ends with the caster's Burst (the Burst state going out), at its `duration`, or when its owner is lost.
//     The registered payload {pollPeriod, slowBuffId, blessingId, burstStateId}; the input {radius, reduction, minimumReduction, duration}. Authority only: a copy draws nothing yet (VFX is later work).
// VI: Vung thuy trieu cua Burst Gura: lop area moi dung chung dich vu time scale. Quai nhan Buff lam cham, dan/blast/area bi cham dong ho; nguoi choi trong vung nhan Buff `gura_blessing`. Ket thuc cung trang thai Burst.
public sealed class PNGuraTideSettings
{
    public float Radius, Reduction, MinimumReduction; public double Duration;
    public PNGuraTideSettings(JToken token)
    {
        var obj = PNBuffContentBatch.Object(token); PNBuffContentBatch.Fields(obj, "radius", "reduction", "minimumReduction", "duration");
        double radius = PNBuffContentBatch.Number(obj, "radius"), reduction = PNBuffContentBatch.Number(obj, "reduction"), minimum = PNBuffContentBatch.Number(obj, "minimumReduction"), duration = PNBuffContentBatch.Number(obj, "duration");
        if (!(radius > 0d && radius <= 50d) || reduction < 0d || minimum < 0d) throw new FormatException("Invalid tide numbers.");
        PNBuffCollection.Seconds(duration, false); Radius = (float)radius; Reduction = (float)reduction; MinimumReduction = (float)minimum; Duration = duration;
    }
    public static void ValidateDefinition(JToken token, EveRegistryHub hub)
    {
        var obj = PNBuffContentBatch.Object(token); PNBuffContentBatch.Fields(obj, "pollPeriod", "slowBuffId", "blessingId", "burstStateId");
        double poll = PNBuffContentBatch.Number(obj, "pollPeriod"); if (!(poll > 0d && poll <= 5d)) throw new FormatException("Tide pollPeriod must be in (0, 5].");
        foreach (var key in new[] { "slowBuffId", "blessingId", "burstStateId" }) EveId.Parse(PNBuffContentBatch.String(obj, key));
    }
}

#if UNITY_5_3_OR_NEWER
public sealed class PNGuraTideArea : PNAreaInstance
{
    private sealed class Grant { internal PNBuffCollection Collection; internal PNBuffReceipt Receipt; }
    private sealed class ClockGrant { internal EveTimeScale Clock; internal long Scale, Minimum; }
    private readonly Dictionary<EveEntityId, Grant> slowed = new Dictionary<EveEntityId, Grant>();
    private readonly Dictionary<long, ClockGrant> clockGrants = new Dictionary<long, ClockGrant>();
    private readonly List<PNTimeTargets.Entry> clockHits = new List<PNTimeTargets.Entry>();
    private readonly HashSet<long> clockInside = new HashSet<long>();
    private readonly HashSet<EveEntityId> seen = new HashSet<EveEntityId>();
    private Collider[] buffer = new Collider[32];
    private PNGuraTideSettings settings; private PNAreaCreateRequest request;
    private EveId slowBuffId, blessingId, burstStateId;
    private float pollPeriod, pollElapsed, radius; private double elapsed;
    private PNSkill ownerSkill;
    public float Radius => radius;
    public override bool AffectedByTime => false;
    public override float ProgressSeconds => (float)elapsed;
    public static void ValidateDefinition(JToken token, EveRegistryHub hub) => PNGuraTideSettings.ValidateDefinition(token, hub);

    protected override void OnStart(PNAreaCreateRequest context)
    {
        settings = new PNGuraTideSettings(context.Input); request = context; radius = settings.Radius * (float)ScaleFactor;
        var data = (JObject)Definition.Payload; slowBuffId = EveId.Parse(PNBuffContentBatch.String(data, "slowBuffId")); blessingId = EveId.Parse(PNBuffContentBatch.String(data, "blessingId"));
        burstStateId = EveId.Parse(PNBuffContentBatch.String(data, "burstStateId")); pollPeriod = (float)PNBuffContentBatch.Number(data, "pollPeriod");
        elapsed = 0; pollElapsed = pollPeriod; slowed.Clear(); clockGrants.Clear();
        ownerSkill = context.Owner != null ? context.Owner.GetComponent<PNSkill>() : null;
    }
    protected override void OnPerform(float gameDelta)
    {
        if (!IsRunning) return;
        elapsed += gameDelta;
        if (elapsed >= settings.Duration) { Finish(PNAreaEndReason.Expired); return; }
        if (IsReplica) return;
        // The tide ends with the Burst (a grace of a moment lets the casting behavior raise the state first).
        if (elapsed > 0.5 && (ownerSkill == null || !StateOn(ownerSkill.Abilities))) { Finish(PNAreaEndReason.OwnerUnavailable); return; }
        pollElapsed += gameDelta; if (pollElapsed < pollPeriod) return;
        pollElapsed %= pollPeriod; Poll(); PollClocks();
    }
    private bool StateOn(PNAbilityCollection abilities)
    {
        if (abilities == null) return false;
        foreach (var found in abilities.Find(burstStateId)) if (found is PNAbilityState s && s.IsActive) return true;
        return false;
    }
    private bool Valid(PNStat entity) => entity != null && entity.IsReady && entity.isActiveAndEnabled && !entity.IsDead && entity.SessionId == Manager.Session.SessionId && ReferenceEquals(entity.GetComponent<EveEntityIdentity>()?.World, World);

    private void Poll()
    {
        seen.Clear(); var physics = gameObject.scene.GetPhysicsScene(); int count;
        do { count = physics.OverlapSphere(transform.position, radius, buffer, ~0, QueryTriggerInteraction.Ignore); if (count < buffer.Length) break; Array.Resize(ref buffer, checked(buffer.Length * 2)); } while (true);
        for (int i = 0; i < count; i++)
        {
            var collider = buffer[i]; buffer[i] = null; var target = collider != null ? collider.GetComponent<PNStat>() : null;
            if (!Valid(target)) continue;
            var buffs = target.GetComponent<PNBuffManager>(); if (buffs == null || !buffs.IsReady) continue;
            int layer = target.gameObject.layer;
            if (layer == PNLayers.Player) { if (seen.Add(target.EntityId)) buffs.Buffs.Apply(new PNBuffApplyRequest(blessingId, request.Source)); }
            else if (layer == PNLayers.Enemy)
            {
                var clock = target.GetComponent<EveTimeScale>(); if (clock == null || !clock.isActiveAndEnabled) continue;
                seen.Add(target.EntityId);
                if (slowed.TryGetValue(target.EntityId, out var old))
                {
                    if (ReferenceEquals(old.Collection, buffs.Buffs) && (old.Receipt.Status == PNBuffStatus.Pending || old.Receipt.Handle.IsAlive)) continue;
                    Revoke(old); slowed.Remove(target.EntityId);
                }
                var receipt = buffs.Buffs.Apply(new PNBuffApplyRequest(slowBuffId, request.Source, new JObject { ["reduction"] = settings.Reduction, ["minimumReduction"] = settings.MinimumReduction }));
                slowed[target.EntityId] = new Grant { Collection = buffs.Buffs, Receipt = receipt };
            }
        }
        foreach (var pair in slowed.ToArray()) if (!seen.Contains(pair.Key)) { Revoke(pair.Value); slowed.Remove(pair.Key); }
    }
    // Projectiles, blasts and areas (not entities) own a clock registered in the world's PNTimeTargets; the tide puts its modifiers straight on it, owned by this area.
    private void PollClocks()
    {
        clockInside.Clear(); clockHits.Clear();
        PNTimeTargets.For(World).Query(transform.position, radius, PNLayers.Bit(PNLayers.Projectile) | PNLayers.Bit(PNLayers.Blast) | PNLayers.Bit(PNLayers.Area), clockHits);
        foreach (var entry in clockHits)
        {
            var clock = entry.Target.TimeClock; if (clock == null || !clock.isActiveAndEnabled) continue;
            clockInside.Add(entry.Id); if (clockGrants.ContainsKey(entry.Id)) continue;
            var grant = new ClockGrant { Clock = clock };
            grant.Scale = clock.AddOwnedModifier(slowBuffId, EveTimeModifierKind.Scale, -settings.Reduction, this);
            try { grant.Minimum = clock.AddOwnedModifier(slowBuffId, EveTimeModifierKind.MinScaleReduction, settings.MinimumReduction, this); } catch { clock.RemoveModifier(grant.Scale); throw; }
            clockGrants[entry.Id] = grant;
        }
        foreach (var id in clockGrants.Keys.Where(x => !clockInside.Contains(x)).ToArray()) { Release(clockGrants[id]); clockGrants.Remove(id); }
    }
    private static void Release(ClockGrant grant) { if (grant.Clock == null) return; grant.Clock.RemoveModifier(grant.Scale); grant.Clock.RemoveModifier(grant.Minimum); }
    private static void Revoke(Grant grant)
    {
        PNBuffReceipt release = null;
        grant.Collection.Coordinator.Submit(() => { if (!grant.Collection.IsDisposed && grant.Receipt.Handle.IsAlive) release = grant.Collection.ReleaseOwned(grant.Receipt.Handle); });
        if (release?.Error != null) throw release.Error;
    }
    protected override void OnEnd(PNAreaEndReason reason) => ReleaseEffects();
    protected override void OnCancel(PNAreaEndReason reason) => ReleaseEffects();
    private void ReleaseEffects()
    {
        var failures = new List<Exception>();
        foreach (var pair in slowed.ToArray()) { try { Revoke(pair.Value); slowed.Remove(pair.Key); } catch (Exception error) { failures.Add(error); } }
        foreach (var pair in clockGrants.ToArray()) { try { Release(pair.Value); clockGrants.Remove(pair.Key); } catch (Exception error) { failures.Add(error); } }
        clockInside.Clear(); clockHits.Clear(); seen.Clear();
        if (failures.Count > 0) throw new AggregateException("Tide cleanup remains pending.", failures);
    }
    protected override void Cleanup() { ReleaseEffects(); ownerSkill = null; }
    public override void OnPoolRelease() { Array.Clear(buffer, 0, buffer.Length); settings = null; request = null; ownerSkill = null; radius = 0; base.OnPoolRelease(); }
}
#else
// Metadata only in standalone contract checks; native Unity owns geometry/lifecycle.
public class PNGuraTideArea { public static void ValidateDefinition(JToken token, EveRegistryHub hub) => PNGuraTideSettings.ValidateDefinition(token, hub); }
#endif
