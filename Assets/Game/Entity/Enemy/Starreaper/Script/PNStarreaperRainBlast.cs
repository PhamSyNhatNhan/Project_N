#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

// Mưa Tinh, one landing of the rain (registered `project_n:starreaper_rain_blast`): a Cylinder Blast dependent on its owner (a dead, disabled or departing owner ends it, so arrows that have not
// landed never do). The rain Operation creates one per landing point at the same moment, with `delay = flight + i * interval`; the first (`sky`) also draws the arrow shot into the sky.
// Each blast warns its zone from the start (the falling arrow is the telegraph), explodes once when its own clock reaches `delay`, and deals the prepared damage once to every target within `radius`.
public sealed class PNStarreaperRainBlast : PNCylinderBlast
{
    private PNStarreaperRainVisual visual; private bool sky; private int index;
    public PNStarreaperRainVisual Visual => visual;
    protected override void ReadShape(JObject input) { sky = input["sky"].Value<bool>(); index = input["index"].Value<int>(); }
    protected override void OnBegin(JObject input)
    {
        var owner = Owner; var muzzle = owner != null ? owner.GetComponent<PNStarreaperPresentation>() : null;
        if (visual == null) visual = new PNStarreaperRainVisual(transform, gameObject.layer);
        visual.Begin(muzzle != null ? muzzle.ProjectilePosition : Center + Vector3.up * .3f, new List<Vector3> { Center }, Radius, Delay, 0f, muzzle != null ? muzzle.SkyDirection : Vector3.up, sky);
    }
    protected override void OnPhase(float time, float delay) { visual?.Advance(time); }
    protected override void OnDetonated() { visual?.Land(0, Delay); }
    protected override void OnEnded() { visual?.Stop(); }
    public override void DisposePooledResources() { visual?.Dispose(); visual = null; }
}

// The rain's Operation (registered `project_n:starreaper_rain_blasts`, schema PNStarreaperCombatContent.ValidateRainInput): picks the centre (the nearest hostile within `seek` of the context
// position, else the position), plans the landing points (PNStarreaperPlanner) and creates one rain blast per point, all dependent on the caster.
public sealed class PNStarreaperRainOperation : PNSkillOperation
{
    private bool executed;
    public int Created { get; private set; }
    public Vector3[] Points { get; private set; } = Array.Empty<Vector3>();
    public override void Execute(PNSkillActionContext context, JToken token)
    {
        if (executed || context == null || !context.Skill.IsReady || !context.Position.HasValue) throw new InvalidOperationException("Fresh Ready position context required.");
        PNStarreaperCombatContent.ValidateRainInput(token); executed = true;
        var input = (JObject)token; var caster = context.Skill.GetComponent<PNStat>();
        var scene = caster.gameObject.scene; var manager = PNBlastManager.Resolve(EveRegistrySession.Context, scene);
        if (!ReferenceEquals(manager.Session.Registry, context.Registry)) throw new InvalidOperationException("Rain session mismatch.");
        var source = PNCombatSource.Capture(caster); var centre = Seek(context, source.TargetLayers.Mask, context.Position.Value, input["seek"].Value<float>());
        var planned = PNStarreaperPlanner.RainPoints(centre.x, centre.z, input["scatterRadius"].Value<float>(), input["minSpacing"].Value<float>(), input["minCount"].Value<int>(), input["maxCount"].Value<int>(), new System.Random());
        float flight = input["flight"].Value<float>(), interval = input["interval"].Value<float>();
        var points = new List<Vector3>();
        for (int i = 0; i < planned.Count; i++)
        {
            var at = new Vector3(planned[i].X, centre.y, planned[i].Z); points.Add(at);
            var blast = new JObject { ["delay"] = flight + i * interval, ["tail"] = input["tail"], ["radius"] = input["radius"], ["height"] = input["height"], ["value"] = input["value"], ["canCrit"] = input["canCrit"], ["tags"] = input["tags"], ["index"] = i, ["sky"] = i == 0 };
            var receipt = manager.Create(new PNBlastCreateRequest(PNStarreaperCombatContent.RainBlast, blast, source, scene, at, caster, PNBlastDependency.Owner));
            if (receipt.Status == PNBlastStatus.Failed) throw new InvalidOperationException("Rain blast creation failed.", receipt.Error);
            if (receipt.Status == PNBlastStatus.Created) Created++;
        }
        Points = points.ToArray();
    }
    private static Vector3 Seek(PNSkillActionContext c, int layerMask, Vector3 origin, float seek)
    {
        PNStat best = null; float bestDistance = seek * seek;
        foreach (var entity in c.World != null ? c.World.Session.Entities.Snapshot(c.World) : EveRegistrySession.Context.Entities.Snapshot())
        {
            var s = entity.GetComponent<PNStat>();
            if (s == null || !s.IsReady || !s.gameObject.activeInHierarchy || PNTargetCandidates.IsDead(s) || !PNWorldGameplay.CanExecute(s) || !PNLayers.Contains(layerMask, s.gameObject.layer)) continue;
            var d = s.transform.position - origin; d.y = 0; float sq = d.sqrMagnitude; if (sq <= bestDistance) { bestDistance = sq; best = s; }
        }
        return best != null ? best.transform.position : origin;
    }
    public override void Dispose() { executed = true; }
}

// Analytic rain presentation of one landing: the landing time and the physical radius are supplied by the blast that deals damage, and `Advance` is given the blast's own
// clock, so a time effect on the blast slows the falling arrow and the detonation together. `withSky` draws the arrow shot into the sky (only the first blast of a rain does).
public sealed class PNStarreaperRainVisual : IDisposable
{
    private readonly GameObject root;
    private readonly PNStarreaperSkyVisual sky;
    private readonly GameObject shakeSource;private IPNCameraHandle impactShake;
    private readonly List<PNStarreaperRainFallVisual> falls = new List<PNStarreaperRainFallVisual>();
    private List<Vector3> points;
    private float radius, flight, interval, age;
    private bool disposed, running, skyOn;
    public PNStarreaperRainVisual(Transform parent, int layer)
    {
        root = new GameObject("Starreaper rain visual"); root.layer=layer;root.transform.SetParent(parent,false);
        sky=new PNStarreaperSkyVisual(root.transform);
        shakeSource=new GameObject("Rain impact camera source");shakeSource.transform.SetParent(root.transform,false);
    }
    public void Begin(Vector3 origin,List<Vector3> landing,float ringRadius,float flightSeconds,float intervalSeconds,Vector3? skyDirection=null,bool withSky=true)
    {
        Stop();skyOn=withSky;points=landing;radius=ringRadius;flight=flightSeconds;interval=intervalSeconds;age=0;
        while(falls.Count<landing.Count)falls.Add(new PNStarreaperRainFallVisual(root.transform,falls.Count));
        running=true;PNViewCamera.Updated+=OnView;
        if(skyOn)sky.Begin(origin,skyDirection??Vector3.up,flightSeconds);Advance(0);
    }
    private void OnView(){RenderSky(PNViewCamera.Current);}
    // Explicit camera also supports disposable native previews without claiming
    // or replacing the gameplay director's output.
    public void RenderSky(Camera camera)
    {
        if(disposed||!running||camera==null)return;
        if(skyOn)sky.Render(camera);
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
    public void Advance(float seconds){if(disposed||!running)return;age=seconds;if(skyOn)sky.Advance(age);OnView();}
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
