using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class PNTimeScaleAreaSettings
{
    public float Radius, Reduction, MinimumReduction;
    public double? Duration;
    public bool Dependent, Exclude;
    public int Layers;
    public PNTimeScaleAreaSettings(JToken token)
    {
        var obj = PNBuffContentBatch.Object(token);
        PNBuffContentBatch.Fields(obj, "radius", "reduction", "minimumReduction", "duration", "dependent", "targetLayers", "layerMode");
        float Read(string name, bool positive = false)
        {
            double value = PNBuffContentBatch.Number(obj, name);
            if (value < 0 || positive && value <= 0 || value > float.MaxValue) throw new FormatException("Invalid area " + name);
            float result = (float)value;
            if (positive && result <= 0) throw new FormatException("Area radius is below float precision.");
            return result;
        }
        Radius = Read("radius", true); Reduction = Read("reduction"); MinimumReduction = Read("minimumReduction");
        if (obj["duration"] == null) throw new FormatException("Area duration is required; use null for owner-dependent permanence.");
        Duration = obj["duration"].Type == JTokenType.Null ? (double?)null : PNBuffContentBatch.Number(obj, "duration");
        if (Duration.HasValue) PNBuffCollection.Seconds(Duration.Value, false);
        Dependent = PNBuffContentBatch.Bool(obj, "dependent");
        if (!Duration.HasValue && !Dependent) throw new FormatException("Permanent area requires dependency.");
        if (obj["targetLayers"] == null) throw new FormatException("Area targetLayers is required."); Layers = PNTargetLayers.Parse(obj["targetLayers"], "area.targetLayers").Mask;
        string mode = PNBuffContentBatch.String(obj, "layerMode");
        if (mode != "Include" && mode != "Exclude") throw new FormatException("Area layerMode must be Include or Exclude.");
        Exclude = mode == "Exclude";
    }
    public static void ValidateDefinition(JToken token, EveRegistryHub hub)
    {
        var obj = PNBuffContentBatch.Object(token);
        PNBuffContentBatch.Fields(obj, "buffId", "pollPeriod", "fadeIn", "fadeOut", "visual");
        new PNTimeScaleAreaVisualSettings(obj["visual"]);
        var id = EveId.Parse(PNBuffContentBatch.String(obj, "buffId"));
        foreach (string field in new[] { "pollPeriod", "fadeIn", "fadeOut" })
        {
            double value = PNBuffContentBatch.Number(obj, field); PNBuffCollection.Seconds(value, false);
            if (value > float.MaxValue || (float)value <= 0) throw new FormatException("Area timing must fit a positive finite float.");
        }
        if (hub.IsSealed) hub.Buffs().GetEffective(id).ValidateInput(new JObject { ["reduction"] = 0, ["minimumReduction"] = 0 });
    }

}

#if UNITY_5_3_OR_NEWER

public class PNTimeScaleArea : PNAreaInstance
{
    private sealed class Grant
    {
        internal PNBuffCollection Collection;
        internal PNBuffReceipt Receipt;
    }
    private readonly Dictionary<EveEntityId, Grant> grants = new Dictionary<EveEntityId, Grant>();
    private readonly Dictionary<EveEntityId, PNStat> inside = new Dictionary<EveEntityId, PNStat>();
    // Things that are not entities (projectiles, areas, later blasts) own a clock and sit in the world's PNTimeTargets: the zone places its modifiers
    // straight on that clock, owned by this zone (see PollClocks). Entities take the same effect through a Buff (Poll).
    private sealed class ClockGrant { internal EveTimeScale Clock; internal long Scale, Minimum; }
    private readonly Dictionary<long, ClockGrant> clockGrants = new Dictionary<long, ClockGrant>();
    private readonly List<PNTimeTargets.Entry> clockHits = new List<PNTimeTargets.Entry>();
    private readonly HashSet<long> clockInside = new HashSet<long>();
    private float radius;
    private Collider[] buffer = new Collider[32];
    private PNTimeScaleAreaSettings settings;
    private PNAreaCreateRequest request;
    private PNSource ownerIdentity, followIdentity;
    private EveId buffId;
    private float pollPeriod, fadeIn, fadeOut, pollElapsed, visualAge, endingStrength, endingRadius;
    private double elapsed;
    private Action<PNDamageResult> death;
    private PNAreaOwnerSignal ownerSignal;
    private Action disabled;
    private PNBuffCollection ownerCollection, followCollection;
    private bool followLost;
    // Plan 4.4c V3: the players whose machine says they stand inside (taken instead of the authority's own overlap test while the policy holds); on a copy, what this machine last told.
    private readonly Dictionary<EveEntityId, PNStat> claimedPresence = new Dictionary<EveEntityId, PNStat>();
    private bool toldInside;
    public override bool TakeClaimedPresence(PNStat target, bool inside, float tolerance)
    {
        if (!IsRunning || IsReplica || target == null || !ValidEntity(target) || PNLayers.Contains(settings.Layers, target.gameObject.layer) == settings.Exclude) return false;
        if (!inside) { claimedPresence.Remove(target.EntityId); return true; }
        if ((target.transform.position - transform.position).magnitude > radius + Mathf.Max(0f, tolerance) + 1f) return false;
        claimedPresence[target.EntityId] = target; return true;
    }
    // A copy on the machine of a player: tells the authority when his body enters or leaves the field (state changes only).
    private void ClaimPresence()
    {
        var sink = Manager.ClaimBook.SinkFor(out var me); if (sink == null || me == null || !me.IsReady) return;
        bool selected = PNLayers.Contains(settings.Layers, me.gameObject.layer) != settings.Exclude;
        var collider = me.GetComponent<Collider>(); bool now = selected && !PNTargetCandidates.IsDead(me) && collider != null && (collider.bounds.ClosestPoint(transform.position) - transform.position).magnitude <= radius;
        if (now == toldInside) return;
        long id = Manager.ObjectIdOf(this); if (id <= 0) return;
        if (sink.SendClaim(PNObjectKind.Area, id, now ? 1 : 0)) { toldInside = now; Manager.ClaimBook.NoteSent(); }
    }
    public override void ActorWorldChanging(PNStat actor)
    {
        if(request==null)return;
        if(request.Follow==actor)followLost=true;
        if(settings.Dependent&&request.Owner==actor)Finish(PNAreaEndReason.OwnerUnavailable);
    }
    private PNAreaOwnerSignal followSignal;
    private Action followDisabled;
    private Action<PNDamageResult> followDied;
    // The zone does not slow itself (two overlapping zones would lengthen each other); every other area does follow a zone.
    public override bool AffectedByTime => false;
    // The registered radius times the creator's `area_scale` at creation.
    public float Radius => radius;
    public float VisualRadius { get; private set; }
    public float VisualStrength { get; private set; }
    public PNTimeScaleAreaVisualSettings VisualSettings { get; private set; }
    private PNTimeScaleDust dust;
    public int DustCount => dust != null ? dust.Count : 0;
    public float DustTime => dust != null ? dust.Time : 0;
    public override float RetirementDuration => fadeOut;
    public static void ValidateDefinition(JToken token, EveRegistryHub hub) => PNTimeScaleAreaSettings.ValidateDefinition(token, hub);
    protected override void Initialize(PNAreaCreateRequest context)
    {
        settings = new PNTimeScaleAreaSettings(context.Input); request = context;
        radius = settings.Radius * (float)ScaleFactor;
        if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0) throw new ArgumentException("The area_scale of the creator leaves the zone without a finite positive radius.");
        var data = (JObject)Definition.Payload; buffId = EveId.Parse(PNBuffContentBatch.String(data, "buffId"));
        VisualSettings = new PNTimeScaleAreaVisualSettings(data["visual"]);
        pollPeriod = (float)PNBuffContentBatch.Number(data, "pollPeriod");
        fadeIn = (float)PNBuffContentBatch.Number(data, "fadeIn"); fadeOut = (float)PNBuffContentBatch.Number(data, "fadeOut");
        if (context.IsReplica) { InitializeReplica(context); return; }
        if (settings.Dependent && !ValidEntity(context.Owner)) throw new ArgumentException("Dependent area requires a live same-session owner.");
        if (context.Follow != null && !ValidEntity(context.Follow)) throw new ArgumentException("Follow target must be live in same session.");
        ownerIdentity = settings.Dependent ? new PNSource(context.Owner.SessionId, context.Owner.EntityId) : null;
        followIdentity = context.Follow != null ? new PNSource(context.Follow.SessionId, context.Follow.EntityId) : null;
        ownerCollection = settings.Dependent ? CurrentCollection(context.Owner) : null;
        followCollection = CurrentCollection(context.Follow);
        followLost = false;
        if (context.Follow != null)
        {
            followSignal = context.Follow.GetComponent<PNAreaOwnerSignal>() ?? context.Follow.gameObject.AddComponent<PNAreaOwnerSignal>();
            followDisabled = () => followLost = true; followSignal.Disabled += followDisabled;
            followDied = _ => followLost = true; context.Follow.Died += followDied;
        }
        if (settings.Dependent)
        {
            death = _ => Finish(PNAreaEndReason.OwnerUnavailable); context.Owner.Died += death;
            ownerSignal = context.Owner.GetComponent<PNAreaOwnerSignal>() ?? context.Owner.gameObject.AddComponent<PNAreaOwnerSignal>();
            disabled = () => Finish(PNAreaEndReason.OwnerUnavailable); ownerSignal.Disabled += disabled;
        }
        elapsed = 0; pollElapsed = pollPeriod; visualAge = 0; VisualStrength = 0; VisualRadius = 0; claimedPresence.Clear(); toldInside = false;
        clockGrants.Clear();
        PNTimeScaleAreaRendering.Register(this);
        if (dust == null) dust = new PNTimeScaleDust(transform);
        dust.Initialize(this, VisualSettings);
    }
    // A copy on a machine without authority draws the field: no Buff, no clock effect, no owner / follow signals. It follows its entity when this machine has it; the authority's End record ends it.
    private void InitializeReplica(PNAreaCreateRequest context)
    {
        ownerIdentity = followIdentity = null; ownerCollection = followCollection = null; followLost = context.Follow == null;
        elapsed = 0; pollElapsed = pollPeriod; visualAge = 0; VisualStrength = 0; VisualRadius = 0; clockGrants.Clear(); claimedPresence.Clear(); toldInside = false;
        PNTimeScaleAreaRendering.Register(this);
        if (dust == null) dust = new PNTimeScaleDust(transform);
        dust.Initialize(this, VisualSettings);
    }
    public override float ProgressSeconds => (float)elapsed;
    protected override void OnScaleCorrected(double scale)
    {
        if (settings == null) return;
        float corrected = settings.Radius * (float)scale;
        if (!float.IsNaN(corrected) && !float.IsInfinity(corrected) && corrected > 0f) radius = corrected;
    }
    private bool ValidEntity(PNStat entity) => entity != null && entity.IsReady && entity.isActiveAndEnabled && !entity.IsDead && entity.SessionId == Manager.Session.SessionId && ReferenceEquals(entity.GetComponent<EveEntityIdentity>()?.World,World);
    private static PNBuffCollection CurrentCollection(PNStat entity)
    { var manager = entity != null ? entity.GetComponent<PNBuffManager>() : null; return manager != null && manager.IsReady ? manager.Buffs : null; }
    private bool SameEntity(PNStat entity, PNSource identity) => ValidEntity(entity) && identity != null && entity.EntityId == identity.EntityId && entity.SessionId == identity.SessionId;
    public override void Advance(float gameDelta)
    {
        if (!IsRunning) return;
        if (IsReplica) { AdvanceReplica(gameDelta); return; }
        if (settings.Dependent && (!SameEntity(request.Owner, ownerIdentity) || ownerCollection != null && !ReferenceEquals(ownerCollection, CurrentCollection(request.Owner)))) { Finish(PNAreaEndReason.OwnerUnavailable); return; }
        elapsed += gameDelta;
        if (settings.Duration.HasValue && elapsed >= settings.Duration.Value) { Finish(PNAreaEndReason.Expired); return; }
        if (request.Follow != null && !followLost)
        {
            if (SameEntity(request.Follow, followIdentity) && (followCollection == null || ReferenceEquals(followCollection, CurrentCollection(request.Follow)))) transform.position = request.Follow.transform.position;
            else followLost = true;
        }
        visualAge += gameDelta; VisualStrength = Mathf.Clamp01(visualAge / fadeIn);
        VisualRadius = radius * Mathf.SmoothStep(0, 1, Mathf.Clamp01(visualAge / VisualSettings.BirthDuration));
        dust.Advance(gameDelta, VisualStrength, true);
        pollElapsed += gameDelta;
        if (pollElapsed < pollPeriod) return;
        pollElapsed %= pollPeriod; Poll(); PollClocks();
    }
    private void AdvanceReplica(float gameDelta)
    {
        elapsed += gameDelta;
        if (settings.Duration.HasValue && elapsed >= settings.Duration.Value) { Finish(PNAreaEndReason.Expired); return; }
        if (request.Follow != null && !followLost)
        {
            if (request.Follow != null && request.Follow.isActiveAndEnabled) transform.position = request.Follow.transform.position; else followLost = true;
        }
        visualAge += gameDelta; VisualStrength = Mathf.Clamp01(visualAge / fadeIn);
        VisualRadius = radius * Mathf.SmoothStep(0, 1, Mathf.Clamp01(visualAge / VisualSettings.BirthDuration));
        dust.Advance(gameDelta, VisualStrength, true);
        // The copy slows the non-entity time targets (projectile / area / blast copies) the way the authority's zone does, so a copy inside the field ages as slowly as the real one. Entities get nothing here (no Buff): they follow the authority's snapshots.
        pollElapsed += gameDelta;
        if (pollElapsed < pollPeriod) return;
        pollElapsed %= pollPeriod; PollClocks(); ClaimPresence();
    }
    private void Poll()
    {
        inside.Clear(); var physics = gameObject.scene.GetPhysicsScene(); int count;
        var claims = Manager.ClaimBook.Policy;
        do
        {
            count = physics.OverlapSphere(transform.position, radius, buffer, ~0, QueryTriggerInteraction.Ignore);
            if (count < buffer.Length) break;
            Array.Resize(ref buffer, checked(buffer.Length * 2));
        } while (true);
        for (int i = 0; i < count; i++)
        {
            var collider = buffer[i]; buffer[i] = null;
            // Only root body colliders participate, not children/hitboxes.
            var target = collider != null ? collider.GetComponent<PNStat>() : null;
            if (!ValidEntity(target) || !ReferenceEquals(target.GetComponent<EveEntityIdentity>()?.World,World)) continue;
            if (claims != null && target.gameObject.layer == PNLayers.Player && claims.ClaimsDecide(target)) continue;
            bool selected = PNLayers.Contains(settings.Layers, target.gameObject.layer);
            if (selected == settings.Exclude) continue;
            var clock = target.GetComponent<EveTimeScale>(); var buffs = target.GetComponent<PNBuffManager>();
            if (clock == null || !clock.isActiveAndEnabled || buffs == null || !buffs.IsReady) continue;
            inside[target.EntityId] = target;
        }
        // The players whose machine decides stand inside as long as it says so (it withdraws the claim when he leaves; a machine that fell silent gives him back to the test above).
        if (claimedPresence.Count != 0)
            foreach (var pair in claimedPresence.ToArray())
            {
                var stat = pair.Value;
                if (claims == null || stat == null || !ValidEntity(stat) || !claims.ClaimsDecide(stat)) { claimedPresence.Remove(pair.Key); continue; }
                var clock = stat.GetComponent<EveTimeScale>(); var buffs = stat.GetComponent<PNBuffManager>();
                if (clock == null || !clock.isActiveAndEnabled || buffs == null || !buffs.IsReady) continue;
                inside[pair.Key] = stat;
            }
        foreach (var pair in grants.ToArray()) if (!inside.ContainsKey(pair.Key)) { Revoke(pair.Value); grants.Remove(pair.Key); }
        foreach (var pair in inside)
        {
            var collection = pair.Value.GetComponent<PNBuffManager>().Buffs;
            if (grants.TryGetValue(pair.Key, out var old))
            {
                if (!ReferenceEquals(old.Collection, collection)) { Revoke(old); grants.Remove(pair.Key); }
                else if (old.Receipt.Status == PNBuffStatus.Pending || old.Receipt.Handle.IsAlive) continue;
            }
            var receipt = collection.Apply(new PNBuffApplyRequest(buffId, request.Source,
                new JObject { ["reduction"] = settings.Reduction, ["minimumReduction"] = settings.MinimumReduction }));
            grants[pair.Key] = new Grant { Collection = collection, Receipt = receipt };
        }
    }
    // Everything registered in the world's PNTimeTargets (projectiles, areas that follow time, later blasts) inside the radius and selected by the same layer rule
    // as the entities (Include keeps the listed layers, Exclude every other) gets the same two clock modifiers an entity gets through the Buff, owned by this zone.
    // A target that left, ended or whose clock was reset is released. Adding a kind of target means giving it a layer and registering it, not changing this method.
    private void PollClocks()
    {
        clockInside.Clear();
        clockHits.Clear(); PNTimeTargets.For(World).Query(transform.position, radius, settings.Exclude ? ~settings.Layers : settings.Layers, clockHits);
        foreach (var entry in clockHits)
        {
            var clock = entry.Target.TimeClock; if (clock == null || !clock.isActiveAndEnabled) continue;
            clockInside.Add(entry.Id);
            if (clockGrants.ContainsKey(entry.Id)) continue;
            var grant = new ClockGrant { Clock = clock };
            grant.Scale = clock.AddOwnedModifier(buffId, EveTimeModifierKind.Scale, -settings.Reduction, this);
            try { grant.Minimum = clock.AddOwnedModifier(buffId, EveTimeModifierKind.MinScaleReduction, settings.MinimumReduction, this); }
            catch { clock.RemoveModifier(grant.Scale); throw; }
            clockGrants[entry.Id] = grant;
        }
        foreach (var id in clockGrants.Keys.Where(x => !clockInside.Contains(x)).ToArray()) { ReleaseClock(clockGrants[id]); clockGrants.Remove(id); }
    }
    private static void ReleaseClock(ClockGrant grant)
    {
        // The clock may already belong to another object (a pooled one); modifier ids are unique per clock, so only ours is removed.
        if (grant.Clock == null) return;
        grant.Clock.RemoveModifier(grant.Scale); grant.Clock.RemoveModifier(grant.Minimum);
    }
    private static void Revoke(Grant grant)
    {
        // Resolve receipt after its queued Apply, then remove that exact handle.
        PNBuffReceipt release=null;
        grant.Collection.Coordinator.Submit(() => { if (!grant.Collection.IsDisposed && grant.Receipt.Handle.IsAlive) release=grant.Collection.ReleaseOwned(grant.Receipt.Handle); });
        if(release?.Error!=null)throw release.Error;
        if(!grant.Collection.IsDisposed&&grant.Receipt.Handle.IsAlive)throw new InvalidOperationException("Owned Area Buff release must drain before resource teardown.");
    }
    protected override void Cleanup()
    {
        endingStrength = VisualStrength; endingRadius = VisualRadius; visualAge = 0;
        if (request != null && request.Owner != null && death != null) request.Owner.Died -= death;
        if (ownerSignal != null && disabled != null) ownerSignal.Disabled -= disabled;
        if (followSignal != null && followDisabled != null) followSignal.Disabled -= followDisabled;
        if (request != null && request.Follow != null && followDied != null) request.Follow.Died -= followDied;
        followDied = null;
        followSignal = null; followDisabled = null;
        ownerSignal = null; disabled = null; ownerCollection = followCollection = null;
        death = null;
        var failures=new List<Exception>();
        foreach (var pair in grants.ToArray()) { try { Revoke(pair.Value);grants.Remove(pair.Key); } catch (Exception error) { failures.Add(error); } }
        foreach (var pair in clockGrants.ToArray()) { try { ReleaseClock(pair.Value); clockGrants.Remove(pair.Key); } catch (Exception error) { failures.Add(error); } }
        clockInside.Clear(); clockHits.Clear();
        if(failures.Count>0)throw new AggregateException("Owned Area Buff cleanup remains pending.",failures);
        inside.Clear();
    }
    public override void AdvanceRetirement(float delta)
    { visualAge += delta; float remaining=Mathf.Clamp01(1-visualAge/fadeOut); VisualRadius=endingRadius*Mathf.SmoothStep(0,1,remaining); VisualStrength = endingStrength * remaining; dust?.Advance(delta, VisualStrength, false); }
    public override void OnPoolRelease()
    {
        PNTimeScaleAreaRendering.Unregister(this); VisualStrength = 0; VisualRadius = 0;
        dust?.Reset(); VisualSettings = null;
        Array.Clear(buffer, 0, buffer.Length);
        settings = null; request = null; ownerIdentity = followIdentity = null; radius = 0;
        base.OnPoolRelease();
    }
    public override void DisposePooledResources() { PNTimeScaleAreaRendering.Unregister(this); dust?.Dispose(); dust = null; }
}
#else
// Metadata only in standalone contract checks; native Unity owns geometry/lifecycle.
public class PNTimeScaleArea { }
#endif
