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
    // Projectiles are not entities: the area places its modifiers straight on their own clock, owned by this area (see PollProjectiles).
    private sealed class ProjectileGrant { internal PNProjectileClock Clock; internal long Scale, Minimum; }
    private readonly Dictionary<long, ProjectileGrant> projectileGrants = new Dictionary<long, ProjectileGrant>();
    private readonly List<PNProjectileHandle> projectileHits = new List<PNProjectileHandle>();
    private readonly HashSet<long> projectileInside = new HashSet<long>();
    private PNProjectileManager projectiles;
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
    public override void ActorWorldChanging(PNStat actor)
    {
        if(request==null)return;
        if(request.Follow==actor)followLost=true;
        if(settings.Dependent&&request.Owner==actor)Finish(PNAreaEndReason.OwnerUnavailable);
    }
    private PNAreaOwnerSignal followSignal;
    private Action followDisabled;
    private Action<PNDamageResult> followDied;
    public float Radius => settings?.Radius ?? 0;
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
        var data = (JObject)Definition.Payload; buffId = EveId.Parse(PNBuffContentBatch.String(data, "buffId"));
        VisualSettings = new PNTimeScaleAreaVisualSettings(data["visual"]);
        pollPeriod = (float)PNBuffContentBatch.Number(data, "pollPeriod");
        fadeIn = (float)PNBuffContentBatch.Number(data, "fadeIn"); fadeOut = (float)PNBuffContentBatch.Number(data, "fadeOut");
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
        elapsed = 0; pollElapsed = pollPeriod; visualAge = 0; VisualStrength = 0; VisualRadius = 0;
        projectiles = PNProjectileManager.Find(Manager.Session, gameObject.scene); projectileGrants.Clear();
        PNTimeScaleAreaRendering.Register(this);
        if (dust == null) dust = new PNTimeScaleDust(transform);
        dust.Initialize(this, VisualSettings);
    }
    private bool ValidEntity(PNStat entity) => entity != null && entity.IsReady && entity.isActiveAndEnabled && !entity.IsDead && entity.SessionId == Manager.Session.SessionId && ReferenceEquals(entity.GetComponent<EveEntityIdentity>()?.World,World);
    private static PNBuffCollection CurrentCollection(PNStat entity)
    { var manager = entity != null ? entity.GetComponent<PNBuffManager>() : null; return manager != null && manager.IsReady ? manager.Buffs : null; }
    private bool SameEntity(PNStat entity, PNSource identity) => ValidEntity(entity) && identity != null && entity.EntityId == identity.EntityId && entity.SessionId == identity.SessionId;
    public override void Advance(float gameDelta)
    {
        if (!IsRunning) return;
        if (settings.Dependent && (!SameEntity(request.Owner, ownerIdentity) || ownerCollection != null && !ReferenceEquals(ownerCollection, CurrentCollection(request.Owner)))) { Finish(PNAreaEndReason.OwnerUnavailable); return; }
        elapsed += gameDelta;
        if (settings.Duration.HasValue && elapsed >= settings.Duration.Value) { Finish(PNAreaEndReason.Expired); return; }
        if (request.Follow != null && !followLost)
        {
            if (SameEntity(request.Follow, followIdentity) && (followCollection == null || ReferenceEquals(followCollection, CurrentCollection(request.Follow)))) transform.position = request.Follow.transform.position;
            else followLost = true;
        }
        visualAge += gameDelta; VisualStrength = Mathf.Clamp01(visualAge / fadeIn);
        VisualRadius = Radius * Mathf.SmoothStep(0, 1, Mathf.Clamp01(visualAge / VisualSettings.BirthDuration));
        dust.Advance(gameDelta, VisualStrength, true);
        pollElapsed += gameDelta;
        if (pollElapsed < pollPeriod) return;
        pollElapsed %= pollPeriod; Poll(); PollProjectiles();
    }
    private void Poll()
    {
        inside.Clear(); var physics = gameObject.scene.GetPhysicsScene(); int count;
        do
        {
            count = physics.OverlapSphere(transform.position, settings.Radius, buffer, ~0, QueryTriggerInteraction.Ignore);
            if (count < buffer.Length) break;
            Array.Resize(ref buffer, checked(buffer.Length * 2));
        } while (true);
        for (int i = 0; i < count; i++)
        {
            var collider = buffer[i]; buffer[i] = null;
            // Only root body colliders participate, not children/hitboxes.
            var target = collider != null ? collider.GetComponent<PNStat>() : null;
            if (!ValidEntity(target) || !ReferenceEquals(target.GetComponent<EveEntityIdentity>()?.World,World)) continue;
            bool selected = PNLayers.Contains(settings.Layers, target.gameObject.layer);
            if (selected == settings.Exclude) continue;
            var clock = target.GetComponent<EveTimeScale>(); var buffs = target.GetComponent<PNBuffManager>();
            if (clock == null || !clock.isActiveAndEnabled || buffs == null || !buffs.IsReady) continue;
            inside[target.EntityId] = target;
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
    // Projectiles inside the radius (and not excluded by the layer rule, which treats them as the Projectile layer) get the same two clock
    // modifiers an entity gets through the Buff, owned by this area. A projectile that left, ended or whose clock was reset is released.
    private void PollProjectiles()
    {
        projectileInside.Clear();
        bool selected = PNLayers.Contains(settings.Layers, PNLayers.Projectile);
        if (projectiles != null && selected != settings.Exclude)
        {
            projectileHits.Clear(); projectiles.QueryRunning(World, transform.position, settings.Radius, projectileHits);
            foreach (var handle in projectileHits)
            {
                if (!projectiles.TryGetClock(handle, out var clock) || !clock.isActiveAndEnabled) continue;
                projectileInside.Add(handle.InstanceId);
                if (projectileGrants.ContainsKey(handle.InstanceId)) continue;
                var grant = new ProjectileGrant { Clock = clock };
                grant.Scale = clock.AddOwnedModifier(buffId, EveTimeModifierKind.Scale, -settings.Reduction, this);
                try { grant.Minimum = clock.AddOwnedModifier(buffId, EveTimeModifierKind.MinScaleReduction, settings.MinimumReduction, this); }
                catch { clock.RemoveModifier(grant.Scale); throw; }
                projectileGrants[handle.InstanceId] = grant;
            }
        }
        foreach (var id in projectileGrants.Keys.Where(x => !projectileInside.Contains(x)).ToArray()) { ReleaseProjectile(projectileGrants[id]); projectileGrants.Remove(id); }
    }
    private static void ReleaseProjectile(ProjectileGrant grant)
    {
        // The clock may already belong to another projectile (pooled object); modifier ids are unique per clock, so only ours is removed.
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
        foreach (var pair in projectileGrants.ToArray()) { try { ReleaseProjectile(pair.Value); projectileGrants.Remove(pair.Key); } catch (Exception error) { failures.Add(error); } }
        projectileInside.Clear(); projectileHits.Clear();
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
        settings = null; request = null; ownerIdentity = followIdentity = null; projectiles = null;
        base.OnPoolRelease();
    }
    public override void DisposePooledResources() { PNTimeScaleAreaRendering.Unregister(this); dust?.Dispose(); dust = null; }
}
#else
// Metadata only in standalone contract checks; native Unity owns geometry/lifecycle.
public class PNTimeScaleArea { }
#endif
