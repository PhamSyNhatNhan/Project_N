#if UNITY_5_3_OR_NEWER
using UnityEngine;

// EN: Burst emits upward light from the body; charging gathers it into the weapon.
// VI: Burst phát hạt ánh sáng bay lên từ thân; tụ lực hút ánh sáng vào kiếm.
public sealed class EveEnergyVisual : MonoBehaviour
{
    private EveEnergySettings settings;
    private Material ribbonMaterial, particleMaterial;
    private ParticleSystem motes, chargeParticles, pulse;
    private TrailRenderer wake;
    private Light glow;
    private EveWeaponRig weapon;
    private Mesh ring;
    private MeshRenderer ringRenderer;
    private Vector3[] vertices=new Vector3[98];
    private Color[] colors=new Color[98];
    private bool burst, moving;
    private float charge, age, moteRemainder, chargeRemainder, flash, groundY;
    private EveEngine.EveTimeScale clock;
    public static EveEnergyVisual Create(Transform owner,EveEnergySettings config)
    {
        var root=new GameObject("Eve sword energy");root.transform.SetParent(owner,false);root.layer=owner.gameObject.layer;
        var v=root.AddComponent<EveEnergyVisual>();v.settings=config;v.clock=owner.GetComponent<EveEngine.EveTimeScale>();
        v.weapon=owner.GetComponentInChildren<EveWeaponRig>();
        var capsule=owner.GetComponent<CapsuleCollider>();v.groundY=capsule!=null?capsule.center.y-capsule.height*.5f:0;
        v.Initialize();return v;
    }
    private void Initialize()
    {
        ribbonMaterial=EveVfxUtility.Material(false);particleMaterial=EveVfxUtility.Material(true);
        motes=EveVfxUtility.Particles(transform,"Burst rising motes",particleMaterial,settings.ParticleLifetime,.018f,0);
        var moteRenderer=motes.GetComponent<ParticleSystemRenderer>();
        moteRenderer.renderMode=ParticleSystemRenderMode.Stretch;moteRenderer.velocityScale=.08f;moteRenderer.lengthScale=1.3f;
        chargeParticles=EveVfxUtility.Particles(transform,"Sword charge attraction",particleMaterial,settings.ParticleLifetime,.023f,0);
        pulse=EveVfxUtility.Particles(transform,"Burst activation sparks",particleMaterial,settings.ParticleLifetime,.03f,0);
        var tail=new GameObject("Dash wake");tail.transform.SetParent(transform,false);tail.layer=gameObject.layer;
        wake=tail.AddComponent<TrailRenderer>();wake.sharedMaterial=ribbonMaterial;wake.time=settings.MotionTrailTime;
        wake.widthMultiplier=settings.MotionTrailWidth;wake.colorGradient=EveVfxUtility.GoldGradient(1.5f);wake.minVertexDistance=.015f;
        wake.widthCurve=AnimationCurve.EaseInOut(0,1,1,0);wake.emitting=false;
        glow=gameObject.AddComponent<Light>();glow.type=LightType.Point;glow.range=.8f;glow.color=new Color(1,.72f,.2f);glow.shadows=LightShadows.None;
        ring=new Mesh {name="Burst activation floor ring",hideFlags=HideFlags.HideAndDontSave};ring.MarkDynamic();
        gameObject.AddComponent<MeshFilter>().sharedMesh=ring;ringRenderer=gameObject.AddComponent<MeshRenderer>();ringRenderer.sharedMaterial=ribbonMaterial;
        ringRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        var uv=new Vector2[98];var triangles=new int[48*6];
        for(int i=0;i<=48;i++)
        {
            uv[i*2]=new Vector2(i/48f,0);uv[i*2+1]=new Vector2(i/48f,1);
            if(i==48)continue;int t=i*6,v=i*2;
            triangles[t]=v;triangles[t+1]=v+1;triangles[t+2]=v+2;triangles[t+3]=v+1;triangles[t+4]=v+3;triangles[t+5]=v+2;
        }
        ring.vertices=vertices;ring.uv=uv;ring.triangles=triangles;ringRenderer.enabled=false;
    }
    public void Charge(float progress)
    {
        charge=Mathf.Clamp01(progress);weapon?.Charge(charge);
        if(charge==0) {chargeRemainder=0;chargeParticles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);}
    }
    public void SetBurst(bool active)
    {
        if(burst && !active) {EmitPulse(settings.EndParticles,false);motes.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);}
        burst=active;weapon?.SetBurst(active);
    }
    public void SetMotion(bool active)
    {if(active&&!moving)wake.Clear();moving=active;wake.emitting=active;}
    public void Flash() {flash=.35f;EmitPulse(settings.ActivationParticles,true);}
    private void EmitPulse(int count,bool outward)
    {
        for(int i=0;i<count;i++)
        {
            float angle=i*Mathf.PI*2/Mathf.Max(1,count);var radial=new Vector3(Mathf.Cos(angle),.25f,Mathf.Sin(angle));
            pulse.Emit(new ParticleSystem.EmitParams {position=transform.position+Vector3.up*.12f+radial*.08f,
                velocity=radial*(outward?.9f:.3f),startColor=new Color(1.8f,1.1f,.15f,.7f)},1);
        }
    }
    public void Advance(float delta)
    {
        if(delta<=0)return;age+=delta;flash=Mathf.Max(0,flash-delta);
        if(burst)
        {
            moteRemainder+=delta*settings.BurstParticleRate;int count=Mathf.FloorToInt(moteRemainder);moteRemainder-=count;
            for(int i=0;i<count;i++)
            {
                float angle=Random.value*Mathf.PI*2;
                var radial=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                motes.Emit(new ParticleSystem.EmitParams {
                    position=transform.position+Vector3.up*Random.Range(.06f,.26f)+radial*Random.Range(.02f,settings.AuraRadius*.65f),
                    velocity=Vector3.up*Random.Range(settings.BurstRiseMinSpeed,settings.BurstRiseMaxSpeed)+radial*Random.Range(.015f,.07f),
                    startSize=Random.Range(settings.BurstParticleMinSize,settings.BurstParticleMaxSize),
                    startLifetime=Random.Range(settings.BurstParticleMinLifetime,settings.BurstParticleMaxLifetime),
                    startColor=new Color(1.8f,1.35f,.3f,.85f)},1);
            }
        }
        if(charge>0)
        {
            chargeRemainder+=delta*settings.ChargeParticleRate*(.4f+charge);int count=Mathf.FloorToInt(chargeRemainder);chargeRemainder-=count;
            var center=weapon!=null?Vector3.Lerp(weapon.GripPosition,weapon.BladeTip,.7f):transform.position+Vector3.up*.2f;
            for(int i=0;i<count;i++)
            {
                float a=age*7+i*2.39996f;var radial=new Vector3(Mathf.Cos(a),Mathf.Sin(a),Mathf.Sin(a*2)*.4f)*settings.AuraRadius;
                chargeParticles.Emit(new ParticleSystem.EmitParams {position=center+radial,velocity=-radial/settings.ParticleLifetime,
                    startColor=new Color(1.6f,1.3f,.55f,.7f)},1);
            }
        }
        ringRenderer.enabled=flash>0;
        if(ringRenderer.enabled)
        {
            float t=1-flash/.35f,radius=flash>0?Mathf.Lerp(.08f,settings.AuraRadius*3,t):settings.AuraRadius*(1.2f+Mathf.Sin(age*3)*.06f);
            for(int i=0;i<=48;i++)
            {
                float a=i/48f*Mathf.PI*2;var radial=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                vertices[i*2]=radial*radius+Vector3.up*(groundY+.018f);
                vertices[i*2+1]=radial*(radius+.018f)+Vector3.up*(groundY+.018f);
                colors[i*2]=colors[i*2+1]=new Color(1.8f,1.1f,.2f,flash>0?(1-t)*.7f:.35f);
            }
            ring.vertices=vertices;ring.colors=colors;ring.RecalculateBounds();
        }
        motes.Simulate(delta,true,false,false);chargeParticles.Simulate(delta,true,false,false);pulse.Simulate(delta,true,false,false);
        glow.intensity=settings.LightIntensity*(charge>0?charge:flash>0?flash*3:burst?.3f:0);
    }
    public void CancelCast() {moving=false;wake.emitting=false;wake.Clear();Charge(0);}
    public void Clear()
    {
        CancelCast();burst=false;flash=moteRemainder=chargeRemainder=0;glow.intensity=0;ringRenderer.enabled=false;
        motes.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);pulse.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
    }
    private void Update()=>Advance(clock!=null&&clock.isActiveAndEnabled?clock.DeltaTime:Time.deltaTime);
    private void OnDisable(){if(wake==null)return;bool retained=burst;Clear();burst=retained;}
    private void OnDestroy()
    {
        EveVfxUtility.DestroyResource(ring);EveVfxUtility.DestroyResource(ribbonMaterial);EveVfxUtility.DestroyResource(particleMaterial);
    }
}
#endif
