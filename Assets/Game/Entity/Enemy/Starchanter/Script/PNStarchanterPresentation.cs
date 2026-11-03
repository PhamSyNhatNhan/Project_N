#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Private Starchanter presentation. Gameplay supplies its registered times/radius and real heal recipients.
// The fork is geometry, not a sprite: an open gold U, stem and pendant; no star between the tines.
[DefaultExecutionOrder(250), DisallowMultipleComponent]
public sealed class PNStarchanterPresentation : MonoBehaviour, IPNCameraFacing, IDisposable
{
    public static readonly Color HealingDust = new Color(.28f, 1f, .42f, 1);
    public const float WeaponScale = .6f;
    private readonly List<Mesh> meshes = new List<Mesh>();
    private readonly List<Material> materials = new List<Material>();
    private readonly LineRenderer[] waves = new LineRenderer[5];
    private readonly LineRenderer[] waveCores = new LineRenderer[5];
    private readonly Material[] waveMaterials = new Material[5];
    private readonly LineRenderer[] recipientRays = new LineRenderer[16];
    private readonly Vector3[] rayOrigins = new Vector3[16];
    private readonly float[] rayBirths = new float[16];
    private readonly bool[] rayBoost = new bool[16];
    private readonly Transform[] gather = new Transform[12];
    private readonly LineRenderer[] membranes = new LineRenderer[3];
    private readonly Vector3[] membranePath = new Vector3[25];
    private readonly Vector3[] line = new Vector3[65];
    private readonly float[] births = { -100, -100, -100, -100, -100 };
    private readonly Vector3[] origins = new Vector3[5];
    private readonly float[] spans = new float[5];
    private readonly Color[] waveColors = new Color[5];
    private GameObject root;
    private Transform fork, aura, haze, weaponLight, puppetLight;
    private Material gold, glow, hazeMaterial, weaponMaterial, gatherMaterial, puppetLightMaterial;
    private ParticleSystem dust, sparks;
    private PNStarchanterHealingZone healingZone;
    private PNStarchanterTeleportVisual teleport;
    private PNStarchanterStrikeVisual strikeVisual;
    private PNStarchanterSpeedVisual boostVisual;
    private PNVisualBody body;
    private PNStat stat;
    private PNVisualActor actor;
    private Camera preview;
    private float clock, castAge, outTime, inTime, interval, radius, tail, dustTimer, channelDuration, finalFlash=-100;
    private int cursor, rayCursor, cast; // 0 idle, 1 heal, 2 boost
    private bool channel, released, fadingPuppet;
    public bool HasWeapon => fork != null;
    public bool Casting => cast != 0;
    public int LivingDust => dust != null ? dust.particleCount : 0;
    public Vector3 ForkPosition => fork != null ? fork.position : transform.position;

    public void Preview(PNVisualActor value, Camera camera) { actor = value; preview = camera; Ensure(); }
    public void Heal(float age, float vanish, float appear, float duration, float tick, float zone)
    {
        Ensure();if(cast!=1||age<castAge)teleport.Begin(transform.position,vanish,appear);
        cast = 1; castAge = age; outTime = vanish; inTime = appear;teleport.SetAge(age);
        interval = tick; radius = zone; tail = 0; channelDuration=duration;
        bool now = age >= vanish + appear;
        healingZone.Channel(now, zone);
        channel = now; Align();
    }
    public void TeleportArrived() {teleport?.Arrive(transform.position);}
    public void Boost(float age, float windup, float zone)
    { Ensure(); cast = 2; castAge = age; outTime = windup; radius = zone; tail = 0; boostVisual.Prepare(age,windup,zone); Align(); }
    public void HealRecipient(Vector3 position, bool final)
    {
        Ensure(); Emit(dust, position + Vector3.up * .025f, final ? 24 : 12, HealingDust, .20f);
        AddRecipientRay(position, false);
        if (!released) { healingZone.Pulse(final); if(final)finalFlash=clock; released = true; }
    }
    public void BoostPulse(){Ensure();boostVisual.Pulse();}
    public void BoostRecipient(Vector3 position)
    { Ensure(); Emit(dust, position + Vector3.up * .04f, 6, new Color(1.3f, .85f, .25f), .08f,.3f); }
    public void BeginStrike(float seconds,Vector3 direction){Ensure();strikeVisual.Begin(seconds,direction);}
    public void FaceStrike(Vector3 direction){strikeVisual?.Face(direction);}
    public void CancelStrike(){strikeVisual?.CancelCharge();}
    public void ReleaseStrike(Vector3 contact)
    {
        Ensure();strikeVisual.Release(ForkPosition);
        Emit(sparks,contact+Vector3.up*.18f,12,new Color(.85f,.45f,1.4f),.055f);
        Emit(sparks,contact+Vector3.up*.18f,5,new Color(1.4f,1,.35f),.045f);
    }
    public void PulseReady() { released = false; }
    public void EndCast(bool complete)
    {
        if (root == null || cast == 0) return;
        teleport.Cancel();
        RestorePuppetFade();
        bool healing=cast==1;
        if(healing) healingZone.Stop(complete);
        else boostVisual.End(complete);
        if (!complete) Emit(sparks, ForkPosition, 12, new Color(.65f, .35f, 1f), .1f);
        cast = 0; channel = false; tail = complete ? (healing?.65f:.1f) : .25f;
    }
    private void Awake() { body = GetComponent<PNVisualBody>(); stat = GetComponent<PNStat>(); }
    private void LateUpdate()
    {
        if (body == null) body = GetComponent<PNVisualBody>();
        var next = body != null ? body.Actor : actor;
        if (next == null || (stat != null && stat.IsReady && stat.IsDead)) { Release(); return; }
        if (actor != next) { Release(); actor = next; }
        Ensure(); Advance(actor.ClockDelta());
    }
    public void Advance(float dt)
    {
        if (root == null) return;
        dt = Mathf.Max(0, dt); clock += dt; tail = Mathf.Max(0, tail - dt);
        // Manual simulation uses the puppet/entity clock, including pause and local time scale.
        if (dt > 0) { dust.Simulate(dt, false, false, false); sparks.Simulate(dt, false, false, false); }
        healingZone.Advance(dt,transform.position);
        strikeVisual.Advance(dt);boostVisual.Advance(dt);
        foreach (var m in materials) m.SetFloat("_FxTime", clock);
        Align();
    }
    private Material Make(int mode, string mask, Color tint, bool additive)
    {
        var shader = Resources.Load<Shader>("PNStarweaverEffects");
        if (shader == null) throw new InvalidOperationException("Missing star VFX shader.");
        var m = new Material(shader) { name = "Starchanter resonance " + mode, hideFlags = HideFlags.HideAndDontSave };
        materials.Add(m); m.SetFloat("_Mode", mode); m.SetFloat("_DstBlend", additive ? 1 : 10); m.SetColor("_Tint", tint);
        m.SetTexture("_NoiseTex", Resources.Load<Texture2D>("StarweaverVfx/Noise"));
        if (mask != null) m.SetTexture("_MainTex", Resources.Load<Texture2D>("StarweaverVfx/" + mask));
        return m;
    }
    private Transform Draw(string label, Mesh mesh, Material material)
    {
        var go = new GameObject(label); go.layer = gameObject.layer; go.transform.SetParent(root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        return go.transform;
    }
    private void Ensure()
    {
        if (root != null) return;
        if (actor == null && body != null) actor = body.Actor;
        root = new GameObject("Starchanter tuning fork and resonance") { hideFlags = HideFlags.DontSave };
        root.transform.SetParent(transform, false);
        try
        {
            healingZone = new PNStarchanterHealingZone(root.transform);
            teleport = new PNStarchanterTeleportVisual(root.transform);
            strikeVisual = new PNStarchanterStrikeVisual(root.transform);
            boostVisual = new PNStarchanterSpeedVisual(root.transform);
            gold = Make(0, null, Color.white, false);
            glow = Make(5, "Glow", new Color(.65f, .32f, 1.25f), true);
            fork = Draw("Open gold tuning fork", BuildFork(), gold);
            weaponMaterial = Make(20, null, new Color(1.2f,.62f,.20f), true);
            weaponLight = Draw("Casting light along gold prongs", fork.GetComponent<MeshFilter>().sharedMesh, weaponMaterial);
            weaponLight.SetParent(fork, false); weaponLight.localPosition = new Vector3(0,0,-.018f);
            var quad = new Mesh { name = "Starchanter resonance quad", hideFlags = HideFlags.HideAndDontSave };
            meshes.Add(quad); quad.vertices = new[] { new Vector3(-.5f,-.5f), new Vector3(.5f,-.5f), new Vector3(.5f,.5f), new Vector3(-.5f,.5f) };
            quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up }; quad.colors = new[] { Color.white, Color.white, Color.white, Color.white }; quad.triangles = new[] { 0,1,2,0,2,3 }; quad.RecalculateBounds();
            aura = Draw("Violet resonance between tines", quad, glow);
            hazeMaterial = Make(7,"Smoke",new Color(.75f,.5f,1f),false);
            haze = Draw("Textured ritual haze",quad,hazeMaterial);
            haze.GetComponent<MeshRenderer>().sortingOrder = -2;
            gatherMaterial = Make(1,null,new Color(1.5f,.95f,.3f),true);
            for(int i=0;i<gather.Length;i++) gather[i]=Draw("Gold gathering mote "+i,quad,gatherMaterial);
            var ribbon = Make(5, "Ribbon", Color.white, true);
            var membraneMaterial = Make(22,null,new Color(.85f,.55f,1.5f),true);
            for (int i = 0; i < membranes.Length; i++)
            {
                var go = new GameObject("Sound membrane " + i); go.layer = gameObject.layer; go.transform.SetParent(root.transform, false);
                var r = go.AddComponent<LineRenderer>(); membranes[i] = r; r.sharedMaterial = membraneMaterial;
                r.positionCount = membranePath.Length; r.useWorldSpace = true; r.widthMultiplier = .012f;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                r.enabled = true;
            }
            for (int i = 0; i < waves.Length; i++)
            {
                var go = new GameObject("Expanding resonance " + i); go.layer = gameObject.layer; go.transform.SetParent(root.transform, false);
                var r = go.AddComponent<LineRenderer>(); waves[i] = r; r.sharedMaterial = ribbon;
                waveMaterials[i]=Make(22,null,Color.white,true);r.sharedMaterial=waveMaterials[i];
                r.positionCount = line.Length; r.useWorldSpace = true; r.widthMultiplier = .11f; r.enabled = false;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                var core = new GameObject("Resonance bright edge "+i);core.layer=gameObject.layer;core.transform.SetParent(root.transform,false);
                var c=core.AddComponent<LineRenderer>();waveCores[i]=c;c.sharedMaterial=ribbon;c.positionCount=line.Length;c.useWorldSpace=true;c.widthMultiplier=.022f;c.enabled=false;
                c.shadowCastingMode=ShadowCastingMode.Off;c.receiveShadows=false;
            }
            var healRay=Make(22,null,new Color(.35f,1.4f,.62f),true);
            var speedRay=Make(22,null,new Color(1.5f,1,.35f),true);
            for(int i=0;i<recipientRays.Length;i++)
            {var go=new GameObject("Recipient rising light "+i);go.layer=gameObject.layer;go.transform.SetParent(root.transform,false);
                var r=go.AddComponent<LineRenderer>();recipientRays[i]=r;r.sharedMaterial=i%2==0?healRay:speedRay;r.useWorldSpace=true;r.positionCount=3;r.widthMultiplier=.065f;r.enabled=false;
                r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;rayBirths[i]=-100;}
            dust = Particles("Green healing dust", Make(5, "Glow", Color.white, true), 240);
            sparks = Particles("Gold and violet resonance motes", Make(1, null, Color.white, true), 160);
            if (Application.isPlaying) PNCameraFacingSystem.Register(this);
        }
        catch { Release(); throw; }
    }
    private ParticleSystem Particles(string label, Material material, int count)
    {
        var go = new GameObject(label); go.layer = gameObject.layer; go.transform.SetParent(root.transform, false);
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.playOnAwake = false; main.loop = false; main.maxParticles = count; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = .85f; main.startSize = .028f; main.startSpeed = 0;
        var emission = ps.emission; emission.enabled = false; var shape = ps.shape; shape.enabled = false;
        var col = ps.colorOverLifetime; col.enabled = true;
        var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white,0), new GradientColorKey(Color.white,1) }, new[] { new GradientAlphaKey(0,0), new GradientAlphaKey(.85f,.15f), new GradientAlphaKey(0,1) }); col.color = gradient;
        var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0,1,1,0));
        var r = ps.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = material; r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        return ps;
    }
    private void Emit(ParticleSystem ps, Vector3 position, int count, Color color, float spread,float lifetime=-1)
    {
        for (int i = 0; i < count; i++)
        {
            float a = i * 2.399963f + clock, r = spread * Mathf.Sqrt((i+.5f)/count);
            var p = new ParticleSystem.EmitParams { position = position + new Vector3(Mathf.Cos(a)*r,0,Mathf.Sin(a)*r), velocity = new Vector3(Mathf.Cos(a)*.025f,.08f+(i%5)*.018f,Mathf.Sin(a)*.025f),
                startColor = color, startSize = ps==dust ? .006f+(i%4)*.002f : .023f+(i%4)*.006f, startLifetime = lifetime>0?lifetime:.75f+(i%5)*.13f };
            ps.Emit(p, 1);
        }
    }
    private void AddWave(Vector3 origin, float extent, Color color)
    { int k = cursor++ % waves.Length; births[k] = clock; origins[k] = origin; spans[k] = extent; waveColors[k] = color; }
    private void AddRecipientRay(Vector3 origin,bool boost)
    {
        for(int j=0;j<3;j++)
        { int k=((rayCursor++%8)*2)+(boost?1:0);rayOrigins[k]=origin+new Vector3((j-1)*.06f,.025f,j*.015f);rayBirths[k]=clock-j*.045f;rayBoost[k]=boost; }
    }
    public bool Align()
    {
        if (root == null || fork == null) return false;
        var camera = PNCameraFacing.ResolveCamera(preview, Application.isPlaying); if (camera == null) return false;
        healingZone.Draw(camera);
        teleport.Draw(camera);
        var frame = actor != null ? actor.transform : transform;
        float side = actor != null && actor.FacingLeft ? -1 : 1;
        float activity = cast == 1 && channel ? .7f+.3f*Mathf.Cos((castAge-outTime-inTime)*Mathf.PI*2/Mathf.Max(.1f,interval)) : cast == 2 || strikeVisual.Charging ? 1 : tail;
        float finalCharge=cast==1?Mathf.SmoothStep(0,1,(castAge-outTime-inTime-channelDuration+.65f)/.65f):0;
        float finalGlow=Mathf.Max(finalCharge*.5f,Mathf.Max(0,1-(clock-finalFlash)/.85f));
        float vanish = cast == 1 && castAge < outTime ? 1-Mathf.SmoothStep(0,1,castAge/outTime) : cast == 1 ? Mathf.SmoothStep(0,1,(castAge-outTime)/inTime) : 1;
        if(actor!=null&&cast==1&&castAge<outTime+inTime)
        {actor.SetFade(vanish);actor.Tick(0);fadingPuppet=true;}
        else RestorePuppetFade();
        fork.SetPositionAndRotation(frame.TransformPoint(new Vector3(side*(.18f+.012f*Mathf.Sin(clock*1.7f)), .23f+.018f*Mathf.Sin(clock*2.2f),-.025f)), frame.rotation * Quaternion.Euler(0,side*-18,side*(-9+Mathf.Sin(clock*1.8f)*5)+activity*Mathf.Sin(clock*35)*.7f));
        fork.localScale = Vector3.one * (WeaponScale*vanish);
        fork.position+=frame.up*(boostVisual.Lift*.24f)-frame.right*(side*boostVisual.Lift*.06f);
        fork.rotation*=Quaternion.Euler(0,0,side*boostVisual.Lift*18);
        boostVisual.DrawFrame(transform.position,camera);
        strikeVisual.Pose(fork,transform,frame);
        strikeVisual.Draw(fork);
        gold.SetFloat("_Opacity", vanish);
        float charge=cast==1?Mathf.Clamp01(castAge/Mathf.Max(.1f,outTime+inTime)):cast==2?Mathf.Clamp01(castAge/Mathf.Max(.1f,outTime)):strikeVisual.Charging?1:0;
        weaponMaterial.SetFloat("_Opacity",(.08f+activity*.35f)*charge*vanish);
        weaponMaterial.SetFloat("_Reveal",charge);
        weaponMaterial.SetColor("_Tint",cast==2?new Color(1.5f,1.1f,.4f):new Color(1.2f,.62f,.20f));
        var rot = Quaternion.LookRotation(camera.transform.forward,camera.transform.up);
        aura.SetPositionAndRotation(fork.TransformPoint(new Vector3(0,.078f,-.025f)),rot);
        aura.localScale = new Vector3(.12f,.17f,1) * vanish;
        glow.SetFloat("_Opacity", (.025f+activity*.85f)*vanish);
        haze.SetPositionAndRotation(frame.TransformPoint(new Vector3(0,.19f,.04f)),rot);haze.localScale=new Vector3(.40f,.43f,1);
        hazeMaterial.SetFloat("_Opacity",cast==0?tail*.20f:activity*.26f*vanish);
        if(actor!=null&&puppetLight==null)
        {
            // Borrow the live puppet mesh/atlas; own only the overlay material and child object.
            puppetLightMaterial=Make(23,null,new Color(.38f,1.3f,.64f),true);
            puppetLight=Draw("Final healing light around puppet",actor.Mesh,puppetLightMaterial);
            puppetLight.GetComponent<MeshRenderer>().sortingOrder=-1;
        }
        if(puppetLight!=null&&actor!=null)
        {
            puppetLight.GetComponent<MeshFilter>().sharedMesh=actor.Mesh;
            puppetLightMaterial.SetTexture("_MainTex",actor.Renderer.sharedMaterial.GetTexture("_MainTex"));
            puppetLightMaterial.SetFloat("_Opacity",finalGlow*.65f);
            puppetLight.SetPositionAndRotation(actor.transform.position+camera.transform.forward*.006f,actor.transform.rotation);
            puppetLight.localScale=actor.transform.lossyScale*(1.02f+finalGlow*.07f);
        }
        glow.SetColor("_Tint",cast==2?new Color(1.3f,.95f,.4f):new Color(.65f,.32f,1.25f));
        glow.SetFloat("_Opacity",(.025f+activity*.85f+finalGlow*.75f)*vanish);
        gatherMaterial.SetFloat("_Mode",cast==2?15:1);
        gatherMaterial.SetFloat("_Opacity",cast!=0||strikeVisual.Charging?vanish*.85f:0);
        for(int i=0;i<gather.Length;i++)
        {float p=Mathf.Repeat(clock*1.15f+i/12f,1),a=i*2.39996f;var end=fork.TransformPoint(new Vector3(0,.09f,0));
            gather[i].SetPositionAndRotation(end+(camera.transform.right*Mathf.Cos(a)+camera.transform.up*Mathf.Sin(a))*(.21f*(1-p)),rot);
            gather[i].localScale=Vector3.one*(.010f+(i%3)*.006f)*Mathf.Sin(p*Mathf.PI);}
        for (int i = 0; i < membranes.Length; i++)
        {
            float p = Mathf.Repeat(clock*1.2f+i/3f,1);
            var tint = cast==2?new Color(1.3f,1,.4f,activity*Mathf.Sin(p*Mathf.PI)*.7f*vanish):new Color(.85f,.55f,1.5f, activity*Mathf.Sin(p*Mathf.PI)*.9f*vanish);
            membranes[i].startColor = membranes[i].endColor = tint;
            for (int j = 0; j < membranePath.Length; j++)
            {
                float u = j/(float)(membranePath.Length-1);
                membranePath[j] = fork.TransformPoint(new Vector3(Mathf.Lerp(-.051f,.051f,u),.025f+p*.12f+Mathf.Sin(u*Mathf.PI)*.035f*Mathf.Sin(clock*7+i),-.018f));
            }
            membranes[i].SetPositions(membranePath);
        }
        for (int i = 0; i < waves.Length; i++)
        {
            float t = clock-births[i]; bool active = t >= 0 && t < 1.1f;
            waves[i].enabled = waveCores[i].enabled = active; if (!active) continue;
            float p = t/1.1f, reach = spans[i]*(.12f+.88f*Mathf.SmoothStep(0,1,p));
            var color = waveColors[i]; color.a *= Mathf.Sin(p*Mathf.PI); waves[i].startColor = waves[i].endColor = color;
            waveMaterials[i].SetColor("_Tint",color);waveCores[i].startColor=waveCores[i].endColor=new Color(color.r,color.g,color.b,color.a*.7f);
            for (int j = 0; j < line.Length; j++)
            {
                float a = j/(float)(line.Length-1)*Mathf.PI*2;
                line[j] = origins[i] + new Vector3(Mathf.Cos(a)*reach,.035f+Mathf.Sin(a*3+clock*2)*.009f,Mathf.Sin(a)*reach);
            }
            waves[i].SetPositions(line);
            waveCores[i].SetPositions(line);
        }
        for(int i=0;i<recipientRays.Length;i++)
        {
            float t=(clock-rayBirths[i])/.7f;bool on=t>=0&&t<1;recipientRays[i].enabled=on;if(!on)continue;
            recipientRays[i].widthMultiplier=rayBoost[i]?.065f:.025f;
            var color=new Color(1,1,1,Mathf.Sin(t*Mathf.PI)*(rayBoost[i]?.9f:.5f));recipientRays[i].startColor=recipientRays[i].endColor=color;
            var origin=rayOrigins[i];var dir=rayBoost[i]?-camera.transform.right*side:Vector3.up;
            var start=origin+dir*(t*.12f);recipientRays[i].SetPosition(0,start);recipientRays[i].SetPosition(1,start+dir*.20f+camera.transform.up*.035f);
            recipientRays[i].SetPosition(2,start+dir*(rayBoost[i]?.32f:.38f));
        }
        return true;
    }
    private Mesh BuildFork()
    {
        var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
        Color light = new Color(1,.80f,.4f), mid = new Color(.76f,.43f,.15f), dark = new Color(.25f,.12f,.08f);
        void Face(Vector3 a, Vector3 b, Vector3 d, Vector3 e, Color color)
        { int k=v.Count; v.Add(a);v.Add(b);v.Add(d);v.Add(e); for(int n=0;n<4;n++)c.Add(color); t.AddRange(new[]{k,k+1,k+2,k,k+2,k+3}); }
        void Strip(Vector2 a, Vector2 b, Vector2 d, Vector2 e)
        {
            Vector3 P(Vector2 p,float z)=>new Vector3(p.x,p.y,z);
            Face(P(a,-.012f),P(b,-.012f),P(d,-.012f),P(e,-.012f),mid);
            Vector2 ae=Vector2.Lerp(a,b,.18f),ee=Vector2.Lerp(e,d,.18f);
            Vector2 bi=Vector2.Lerp(b,a,.16f),di=Vector2.Lerp(d,e,.16f);
            Face(P(a,-.013f),P(ae,-.013f),P(ee,-.013f),P(e,-.013f),new Color(1,.9f,.66f));
            Face(P(bi,-.013f),P(b,-.013f),P(d,-.013f),P(di,-.013f),dark);
            Face(P(ae,-.014f),P(bi,-.014f),P(di,-.014f),P(ee,-.014f),light);
            Face(P(a,-.015f),P(Vector2.Lerp(a,b,.09f),-.015f),P(Vector2.Lerp(e,d,.09f),-.015f),P(e,-.015f),dark);
            Face(P(e,.012f),P(d,.012f),P(b,.012f),P(a,.012f),mid);
            Face(P(a,.012f),P(a,-.012f),P(e,-.012f),P(e,.012f),dark);
            Face(P(b,-.012f),P(b,.012f),P(d,.012f),P(d,-.012f),mid);
        }
        // Semicircular heel joins the two straight prongs; the inside stays empty.
        for(int i=0;i<18;i++)
        { float a=Mathf.PI+i*Mathf.PI/18,b=Mathf.PI+(i+1)*Mathf.PI/18;
            Strip(new Vector2(Mathf.Cos(a)*.075f,Mathf.Sin(a)*.075f),new Vector2(Mathf.Cos(a)*.052f,Mathf.Sin(a)*.052f),new Vector2(Mathf.Cos(b)*.052f,Mathf.Sin(b)*.052f),new Vector2(Mathf.Cos(b)*.075f,Mathf.Sin(b)*.075f)); }
        foreach(float s in new[]{-1f,1f})
        { Strip(new Vector2(s*.075f,0),new Vector2(s*.052f,0),new Vector2(s*.052f,.175f),new Vector2(s*.075f,.175f));
            Face(new Vector3(s*.075f,.175f,-.012f),new Vector3(s*.052f,.175f,-.012f),new Vector3(s*.052f,.175f,.012f),new Vector3(s*.075f,.175f,.012f),light); }
        Strip(new Vector2(-.012f,-.067f),new Vector2(.012f,-.067f),new Vector2(.009f,-.155f),new Vector2(-.009f,-.155f));
        Strip(new Vector2(0,-.14f),new Vector2(.025f,-.166f),new Vector2(0,-.205f),new Vector2(-.025f,-.166f));
        Face(new Vector3(0,-.14f,-.017f),new Vector3(.025f,-.166f,-.017f),new Vector3(0,-.205f,-.017f),new Vector3(0,-.166f,-.020f),mid);
        Face(new Vector3(0,-.14f,-.017f),new Vector3(0,-.166f,-.020f),new Vector3(0,-.205f,-.017f),new Vector3(-.025f,-.166f,-.017f),new Color(1,.91f,.66f));
        var mesh = new Mesh { name="Faceted gold open tuning fork", hideFlags=HideFlags.HideAndDontSave }; meshes.Add(mesh);
        var uv=new List<Vector2>(v.Count);foreach(var point in v)uv.Add(new Vector2((point.x+.075f)/.15f,(point.y+.205f)/.38f));
        mesh.SetVertices(v);mesh.SetColors(c);mesh.SetTriangles(t,0);mesh.SetUVs(0,uv);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    private void OnDisable() { Release(); }
    private void OnDestroy() { Release(); }
    public void Dispose() { Release(); }
    private void RestorePuppetFade()
    {if(!fadingPuppet)return;if(actor!=null){actor.SetFade(1);actor.Tick(0);}fadingPuppet=false;}
    private void Release()
    {
        PNCameraFacingSystem.Unregister(this);
        RestorePuppetFade();
        healingZone?.Dispose();healingZone=null;
        teleport?.Dispose();teleport=null;
        strikeVisual?.Dispose();strikeVisual=null;
        boostVisual?.Dispose();boostVisual=null;
        if (!ReferenceEquals(root,null)) { root.SetActive(false); DestroyOwned(root); }
        foreach(var m in materials)DestroyOwned(m);foreach(var m in meshes)DestroyOwned(m);
        materials.Clear();meshes.Clear();root=null;fork=aura=puppetLight=null;dust=sparks=null;cast=0;channel=false;clock=tail=dustTimer=0;cursor=0;finalFlash=-100;
        for(int i=0;i<births.Length;i++)births[i]=-100;
    }
    private static void DestroyOwned(UnityEngine.Object value)
    { if(ReferenceEquals(value,null))return; if(Application.isPlaying)Destroy(value);else DestroyImmediate(value); }
}
#endif
