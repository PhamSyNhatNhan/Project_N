#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using EveEngine;

// EN: Trail records two blade anchors as the real sword moves; no prebuilt arc.
// VI: Trail ghi hai điểm trên lưỡi kiếm đang chuyển động; không dựng sẵn cung chém.
public sealed class EveSweepVisual : MonoBehaviour
{
    private struct BladeSample { public float Time; public Vector3 Near, Tip; }
    private readonly List<BladeSample> samples = new List<BladeSample>(80);
    private readonly List<Vector3> vertices = new List<Vector3>(320);
    private readonly List<Color> colors = new List<Color>(160);
    private readonly List<Vector2> uv = new List<Vector2>(160);
    private readonly List<int> triangles = new List<int>(480);
    private EveSweepSettings settings;
    private EveHitboxPose pose;
    private EveWeaponRig weapon;
    private Mesh mesh;
    private Material ribbonMaterial, particleMaterial;
    private TrailRenderer tipTrail;
    private ParticleSystem sparks;
    private Transform tip;
    private float previousTime, sparkRemainder, releasedAge;
    private bool released;
    private EveTimeScale clock;
    private PNStat caster;
    public Transform AttackRoot => transform;
    public float Progress { get; private set; }
    public float Radius => settings.Radius;
    public Vector3 HeadPosition => tip.position;
    public int LiveParticles => sparks.particleCount;
    public int BladeSamples => samples.Count;
    public static EveSweepVisual Create(Transform owner, EveHitboxPose pose, EveSweepSettings config)
    {
        var weapon=owner.GetComponentInChildren<EveWeaponRig>();
        if (weapon==null) throw new System.InvalidOperationException("Eve sword rig must be loaded before an attack.");
        var root=new GameObject("Eve sword trail"); root.layer=owner.gameObject.layer;
        SceneManager.MoveGameObjectToScene(root,owner.gameObject.scene); root.transform.SetParent(owner,false);
        root.transform.SetPositionAndRotation(pose.Position,pose.Rotation);
        var v=root.AddComponent<EveSweepVisual>(); v.settings=config; v.pose=pose; v.weapon=weapon;v.clock=owner.GetComponent<EveTimeScale>();v.caster=owner.GetComponent<PNStat>();
        try { v.Initialize(); v.Render(0,0); return v; }
        catch { EveVfxUtility.DestroyResource(root); throw; }
    }
    private void Initialize()
    {
        ribbonMaterial=EveVfxUtility.Material(false); ribbonMaterial.SetFloat("_Volume",1); particleMaterial=EveVfxUtility.Material(true);
        mesh=new Mesh { name="Recorded sword blade trail",hideFlags=HideFlags.HideAndDontSave }; mesh.MarkDynamic();
        gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial=ribbonMaterial; renderer.shadowCastingMode=ShadowCastingMode.Off;
        tip=new GameObject("Sword tip trail").transform; tip.SetParent(transform,false); tip.gameObject.layer=gameObject.layer;
        tipTrail=tip.gameObject.AddComponent<TrailRenderer>(); tipTrail.sharedMaterial=ribbonMaterial; tipTrail.emitting=false;
        tipTrail.time=settings.Duration*settings.TrailFraction; tipTrail.widthMultiplier=settings.TrailWidth;
        tipTrail.colorGradient=EveVfxUtility.GoldGradient(settings.Intensity);
        tipTrail.widthCurve=AnimationCurve.EaseInOut(0,1,1,0); tipTrail.minVertexDistance=.015f; tipTrail.Clear();
        sparks=EveVfxUtility.Particles(transform,"Sword sparks",particleMaterial,settings.ParticleLifetime,settings.ParticleSize,0);
        var sparkRenderer=sparks.GetComponent<ParticleSystemRenderer>();sparkRenderer.renderMode=ParticleSystemRenderMode.Stretch;
        sparkRenderer.velocityScale=.06f;sparkRenderer.lengthScale=1.5f;
    }
    public void Render(float normalized,float delta)
    {
        transform.SetPositionAndRotation(pose.Position,pose.Rotation);
        Progress=Mathf.Clamp01(normalized);
        float now=Progress*settings.Duration;
        float begin=Mathf.Max(previousTime,settings.SweepStart), end=Mathf.Min(Progress,settings.SweepEnd);
        if (Progress>previousTime && begin<=end && Progress>=settings.SweepStart && previousTime<settings.SweepEnd)
        {
            int steps=Mathf.Max(1,Mathf.CeilToInt((end-begin)*settings.Duration/.004f));
            for(int i=0;i<=steps;i++)
            {
                float n=Mathf.Lerp(begin,end,i/(float)steps);
                if(samples.Count!=0 && n*settings.Duration<=samples[samples.Count-1].Time)continue;
                float p=settings.SweepProgress(n);
                var sample=new BladeSample { Time=n*settings.Duration,
                    Near=settings.Point(p,Mathf.Max(settings.InnerRadius,settings.Radius*.08f)),
                    Tip=settings.Point(p,settings.Radius) };
                samples.Add(sample); tipTrail.AddPosition(transform.TransformPoint(sample.Tip));
            }
        }
        float lifetime=settings.Duration*settings.TrailFraction;
        samples.RemoveAll(s=>now-s.Time>lifetime);
        mesh.Clear(); vertices.Clear(); uv.Clear(); colors.Clear(); triangles.Clear();
        var normal=Quaternion.AngleAxis(settings.Tilt,Vector3.right)*Vector3.up;
        for(int i=0;i<samples.Count;i++)
        {
            var sample=samples[i];float age=Mathf.Clamp01((now-sample.Time)/lifetime);
            var center=(sample.Near+sample.Tip)*.5f;
            float taper=Mathf.Sqrt(1-age);
            var near=Vector3.Lerp(center,sample.Near,taper);var far=Vector3.Lerp(center,sample.Tip,taper);
            var depth=normal*(settings.TrailDepth*.5f*taper);
            vertices.Add(near);vertices.Add(center+depth);vertices.Add(far);vertices.Add(center-depth);
            float u=i/(float)Mathf.Max(1,samples.Count-1);
            uv.Add(new Vector2(u,.12f));uv.Add(new Vector2(u,.5f));uv.Add(new Vector2(u,.95f));uv.Add(new Vector2(u,.5f));
            var gold=new Color(1,.9f,.45f)*settings.Intensity;
            gold.a=(1-age)*(1-age)*.28f;
            var core=new Color(1,1,.86f)*settings.Intensity;core.a=gold.a;
            var rootGold=gold;rootGold.a*=.18f;
            colors.Add(rootGold);colors.Add(core);colors.Add(gold);colors.Add(core);
            if(i==0)continue;int v=i*4,old=v-4;
            for(int side=0;side<4;side++)Quad(old+side,old+(side+1)%4,v+(side+1)%4,v+side);
        }
        if(samples.Count>1)
        {Quad(3,2,1,0);int last=(samples.Count-1)*4;Quad(last,last+1,last+2,last+3);}
        if(vertices.Count>0) { mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds(); }
        var direction=pose.Rotation*settings.SwordDirection(Progress);
        var pivot=pose.Position+Vector3.up*settings.Height;
        weapon.Strike(pivot,direction,settings.Radius,1,pose.Rotation*normal);
        tip.position=pivot+direction*settings.Radius;
        float sweptDelta=Mathf.Max(0,end-begin)*settings.Duration;
        if(sweptDelta>0)
        {
            sparkRemainder+=sweptDelta*settings.ParticleRate;int count=Mathf.FloorToInt(sparkRemainder);sparkRemainder-=count;
            for(int i=0;i<count;i++)
            {
                float n=Mathf.Lerp(begin,end,(i+Random.value)/Mathf.Max(1,count));
                float p=settings.SweepProgress(n),radius=settings.Radius*Random.Range(.45f,1f);
                var point=transform.TransformPoint(settings.Point(p,radius));
                var tangent=transform.TransformDirection(settings.Point(Mathf.Min(1,p+.01f),radius)-settings.Point(Mathf.Max(0,p-.01f),radius)).normalized;
                sparks.Emit(new ParticleSystem.EmitParams {position=point,
                    velocity=tangent*Random.Range(settings.ParticleSpeed*.6f,settings.ParticleSpeed)+transform.TransformDirection(normal)*Random.Range(-.55f,.55f)+Vector3.up*Random.Range(.06f,.2f),
                    startLifetime=Random.Range(settings.ParticleLifetime*.7f,settings.ParticleLifetime),startSize=Random.Range(settings.ParticleSize*.65f,settings.ParticleSize),
                    startColor=new Color(2,1.9f,1.15f,.95f)},1);
            }
        }
        if(delta>0)sparks.Simulate(delta,true,false,false);
        previousTime=Progress;
    }
    private void Quad(int a,int b,int c,int d)
    {triangles.Add(a);triangles.Add(b);triangles.Add(c);triangles.Add(a);triangles.Add(c);triangles.Add(d);}
    public void Impact(Vector3 position,int count)
    {
        for(int i=0;i<count;i++) sparks.Emit(new ParticleSystem.EmitParams {
            position=position+Vector3.up*.14f,velocity=Random.onUnitSphere*settings.ParticleSpeed*.65f+Vector3.up*.25f,
            startSize=Random.Range(settings.ParticleSize*.7f,settings.ParticleSize*1.2f),
            startLifetime=settings.ParticleLifetime,startColor=new Color(2,1.95f,1.3f,.95f)},1);
    }
    public void Release(bool cancelled)
    {
        if(released)return;released=true;
        if(weapon!=null)weapon.EndStrike();tipTrail.emitting=false;
        if(cancelled || !Application.isPlaying)
        {
            mesh.Clear();tipTrail.Clear();sparks.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            if(Application.isPlaying)Destroy(gameObject);else DestroyImmediate(gameObject);
        }
        // Completed attacks retain their frozen sweep and fade without extending the damage window.
    }
    private void LateUpdate()
    {
        transform.SetPositionAndRotation(pose.Position,pose.Rotation);
        if(!released)return;
        if(caster!=null && (!caster.IsReady || caster.IsDead)){Destroy(gameObject);return;}
        float delta=clock!=null&&clock.isActiveAndEnabled?clock.DeltaTime:Time.deltaTime;
        releasedAge+=delta;
        float alpha=settings.Afterglow>0?Mathf.Clamp01(1-releasedAge/settings.Afterglow):0;
        ribbonMaterial.SetColor("_Tint",new Color(1,1,1,alpha));
        sparks.Simulate(delta,true,false,false);
        if(releasedAge>=Mathf.Max(settings.Afterglow,settings.ParticleLifetime))Destroy(gameObject);
    }
    private void OnDestroy()
    { EveVfxUtility.DestroyResource(mesh);EveVfxUtility.DestroyResource(ribbonMaterial);EveVfxUtility.DestroyResource(particleMaterial); }
}
public static class EveVfxUtility
{
    public static Material Material(bool particle)
    {
        var shader = Shader.Find("Project_N/VFX/EveEnergy");
        if (shader == null) throw new System.InvalidOperationException("Missing Eve energy shader.");
        var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        material.SetFloat("_Particle", particle ? 1 : 0); return material;
    }
    public static Gradient GoldGradient(float intensity)
    {
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(new Color(1, 1, .85f) * intensity, 0),
            new GradientColorKey(new Color(1, .88f, .4f) * intensity, 1) },
            new[] { new GradientAlphaKey(.8f, 0), new GradientAlphaKey(0, 1) });
        return gradient;
    }
    public static ParticleSystem Particles(Transform parent, string name, Material material, float lifetime, float size, float speed)
    {
        var child = new GameObject(name); child.transform.SetParent(parent, false);
        child.layer = parent.gameObject.layer;
        var system = child.AddComponent<ParticleSystem>(); system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main; main.playOnAwake = false; main.loop = true; main.duration = 1;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.startLifetime = lifetime; main.startSize = new ParticleSystem.MinMaxCurve(size * .5f, size);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * .3f, speed); main.maxParticles = 256;
        main.startColor = new Color(1.5f, 1.05f, .3f, .75f); main.gravityModifier = 0;
        var emission = system.emission; emission.enabled = false;
        var shape = system.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .03f;
        var color = system.colorOverLifetime; color.enabled = true; color.color = GoldGradient(1);
        var sizeLife = system.sizeOverLifetime; sizeLife.enabled = true; sizeLife.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, 1, 1, 0));
        var renderer = system.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard; renderer.shadowCastingMode = ShadowCastingMode.Off;
        // EN: Initialize before first Emit; initial Simulate otherwise clears those particles.
        // VI: Khởi tạo trước Emit đầu tiên để Simulate không xóa hạt vừa phát.
        system.Simulate(0, true, true, false);
        return system;
    }
    public static void DestroyResource(Object value)
    { if (value == null) return; if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
}
#endif
