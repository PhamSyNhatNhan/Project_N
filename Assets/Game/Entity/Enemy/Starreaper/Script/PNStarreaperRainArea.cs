#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

// Mưa Tinh (registered `project_n:starreaper_rain_area`, schema PNStarreaperCombatContent.ValidateRainInput). Created by the rain Action the moment the arrow is shot into the sky:
//   - the centre is the nearest hostile within `seek` (the player at the shot; the Action's position when nobody is), the landing points are `PNStarreaperPlanner.RainPoints` around it,
//   - every landing zone is warned at once and stays until its arrow lands,
//   - after `flight` seconds the first arrow lands, then one every `interval`; a landing explodes: every living target of the caster's TargetLayers within `radius` takes the prepared damage once
//     (overlapping explosions count separately; the falling arrow itself deals nothing),
//   - dependent: the owner's death, disable or leaving the world ends the rain, so arrows that have not landed never do.
// The damage is prepared once at creation from the owner's stats (the combat source snapshot), exactly like a projectile's.
public sealed class PNStarreaperRainArea : PNAreaInstance
{
    private const float VisualTail = .45f;
    private PNStat owner; private EveEntityId ownerId; private bool dependent;
    private float flight, interval, radius, age; private int landed, layerMask;
    private readonly List<Vector3> points = new List<Vector3>();
    private PNPreparedDamage prepared; private PNCombatSourceSnapshot snapshot;
    private PNStarreaperRainVisual visual;
    public IReadOnlyList<Vector3> Points => points;      // read by the Player review to put a target under one landing
    public float Age => age;
    private readonly Collider[] buffer = new Collider[32]; private readonly HashSet<EveEntityId> struck = new HashSet<EveEntityId>();

    protected override void Initialize(PNAreaCreateRequest context)
    {
        var input = (JObject)context.Input;
        owner = context.Owner ?? throw new ArgumentException("The rain needs its owner's stats.");
        ownerId = owner.EntityId; dependent = input["dependent"].Value<bool>();
        flight = input["flight"].Value<float>(); interval = input["interval"].Value<float>(); radius = input["radius"].Value<float>();
        snapshot = context.Source.Snapshot ?? throw new InvalidOperationException("The rain needs a combat snapshot."); layerMask = context.Source.TargetLayers.Mask;
        double amount = 0d; foreach (var component in PNSkillValues.Parse(input["value"]).Components) amount += component.Evaluate(owner);
        prepared = snapshot.PrepareDamage(amount, input["canCrit"].Value<bool>(), null, PNSkillEffect.Ids(input["tags"]).ToArray());
        var centre = Seek(context.Position, input["seek"].Value<float>());
        points.Clear();
        foreach (var p in PNStarreaperPlanner.RainPoints(centre.x, centre.z, input["scatterRadius"].Value<float>(), input["minSpacing"].Value<float>(), input["minCount"].Value<int>(), input["maxCount"].Value<int>(), new System.Random()))
            points.Add(new Vector3(p.X, centre.y, p.Z));
        age = 0; landed = 0; struck.Clear();
        var muzzle = owner.GetComponent<PNStarreaperPresentation>();
        if (visual == null) visual = new PNStarreaperRainVisual(transform, gameObject.layer);
        visual.Begin(muzzle != null ? muzzle.ProjectilePosition : owner.transform.position + Vector3.up * .3f, points, radius, flight, interval, muzzle != null ? muzzle.SkyDirection : Vector3.up);
    }
    // The nearest living hostile within `seek` of `origin` (planar), as its position; `origin` itself when there is none.
    private Vector3 Seek(Vector3 origin, float seek)
    {
        PNStat best = null; float bestDistance = seek * seek;
        foreach (var entity in World.Session.Entities.Snapshot(World))
        {
            var s = entity.GetComponent<PNStat>(); if (!Valid(s) || !PNLayers.Contains(layerMask, s.gameObject.layer)) continue;
            var d = s.transform.position - origin; d.y = 0; float sq = d.sqrMagnitude; if (sq <= bestDistance) { bestDistance = sq; best = s; }
        }
        return best != null ? best.transform.position : origin;
    }
    private bool Valid(PNStat s) => s != null && s.IsReady && s.gameObject.activeInHierarchy && !PNTargetCandidates.IsDead(s) && PNWorldGameplay.CanExecute(s) && ReferenceEquals(s.GetComponent<EveEntityIdentity>()?.World, World);
    private bool OwnerAlive() => owner != null && owner.IsReady && !owner.IsDead && owner.isActiveAndEnabled && owner.EntityId == ownerId && ReferenceEquals(owner.GetComponent<EveEntityIdentity>()?.World, World);
    public override void ActorWorldChanging(PNStat actor) { if (dependent && ReferenceEquals(owner, actor)) Finish(PNAreaEndReason.OwnerUnavailable); }

    public override void Advance(float gameDelta)
    {
        if (!IsRunning) return;
        if (dependent && !OwnerAlive()) { Finish(PNAreaEndReason.OwnerUnavailable); return; }
        age += gameDelta;
        while (landed < points.Count && age >= flight + landed * interval) Explode(landed++);
        visual?.Advance(age);
        if (landed >= points.Count && age >= flight + (points.Count - 1) * interval + VisualTail) Finish(PNAreaEndReason.Expired);
    }
    private void Explode(int index)
    {
        var at = points[index]; visual?.Land(index, age);
        struck.Clear();
        var physics = gameObject.scene.GetPhysicsScene();
        int count = physics.OverlapSphere(at + Vector3.up * .15f, radius, buffer, layerMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var target = buffer[i] != null ? buffer[i].GetComponent<PNStat>() : null; buffer[i] = null;
            if (target == null || !Valid(target) || !struck.Add(target.EntityId)) continue;
            target.ReceiveDamage(snapshot.CreateDamage(prepared));
        }
    }
    protected override void Cleanup() { visual?.Stop(); points.Clear(); struck.Clear(); }
    public override void OnPoolRelease() { owner = null; prepared = null; snapshot = null; base.OnPoolRelease(); }
    public override void DisposePooledResources() { visual?.Dispose(); visual = null; }
}

// Area-owned analytic rain presentation; landing times and physical radii are
// supplied by the same area that deals damage. No independent animation clock.
public sealed class PNStarreaperRainVisual : IDisposable
{
    private readonly GameObject root;
    private readonly PNStarreaperSkyVisual sky;
    private readonly GameObject shakeSource;private IPNCameraHandle impactShake;
    private readonly List<PNStarreaperRainFallVisual> falls = new List<PNStarreaperRainFallVisual>();
    private List<Vector3> points;
    private float radius, flight, interval, age;
    private bool disposed, running;
    public PNStarreaperRainVisual(Transform parent, int layer)
    {
        root = new GameObject("Starreaper rain visual"); root.layer=layer;root.transform.SetParent(parent,false);
        sky=new PNStarreaperSkyVisual(root.transform);
        shakeSource=new GameObject("Rain impact camera source");shakeSource.transform.SetParent(root.transform,false);
    }
    public void Begin(Vector3 origin,List<Vector3> landing,float ringRadius,float flightSeconds,float intervalSeconds,Vector3? skyDirection=null)
    {
        Stop();points=landing;radius=ringRadius;flight=flightSeconds;interval=intervalSeconds;age=0;
        while(falls.Count<landing.Count)falls.Add(new PNStarreaperRainFallVisual(root.transform,falls.Count));
        running=true;PNViewCamera.Updated+=OnView;
        sky.Begin(origin,skyDirection??Vector3.up,flightSeconds);Advance(0);
    }
    private void OnView(){RenderSky(PNViewCamera.Current);}
    // Explicit camera also supports disposable native previews without claiming
    // or replacing the gameplay director's output.
    public void RenderSky(Camera camera)
    {
        if(disposed||!running||camera==null)return;
        sky.Render(camera);
        for(int i=0;i<points.Count;i++)falls[i].Render(points[i],radius,age,flight+i*interval,camera);
    }
    public void Land(int index,float seconds)
    {
        if(disposed||!running||index<0||index>=points.Count)return;
        impactShake?.Dispose();impactShake=null;
        shakeSource.transform.position=points[index];
        if(Application.isPlaying)impactShake=PNCameraServices.Current.RequestShake(shakeSource,"starreaper.rain.impact",
            new PNShakeSpec(.025f,28,.10f,.08f,0,PNShakeShape.Recoil,1,.35f,0,5,PNShakeFalloff.Quadratic));
    }
    public void Advance(float seconds){if(disposed||!running)return;age=seconds;sky.Advance(age);OnView();}
    public void Stop()
    {
        if(disposed)return;
        if(running)PNViewCamera.Updated-=OnView;
        running=false;impactShake?.Dispose();impactShake=null;sky.Stop();foreach(var f in falls)f.Clear();
    }
    public void Dispose()
    {
        if(disposed)return;Stop();disposed=true;sky.Dispose();foreach(var f in falls)f.Dispose();falls.Clear();
        if(root!=null){if(Application.isPlaying)UnityEngine.Object.Destroy(root);else UnityEngine.Object.DestroyImmediate(root);}
    }
}
#endif
