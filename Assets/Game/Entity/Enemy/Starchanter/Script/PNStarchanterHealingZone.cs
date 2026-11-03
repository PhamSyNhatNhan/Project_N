#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Private Starchanter field: native smoke particles, fine stars and outward-travelling healing fronts.
// Only masks/noise are borrowed from NamuFX. Geometry, clock, shader and ownership are project code.
public sealed class PNStarchanterHealingZone : IDisposable
{
    private const int Lanes=12, Segments=144, WaveBands=7, Grains=1800, Stars=20;
    private readonly GameObject root;
    private readonly List<Mesh> meshes=new List<Mesh>();
    private readonly List<Material> materials=new List<Material>();
    private readonly Mesh dust,points,links,floor;
    private readonly ParticleSystem[] cloudSystems=new ParticleSystem[3];
    private readonly Material[] smokeMaterials=new Material[3];
    private readonly ParticleSystem.Particle[] cloudParticles=new ParticleSystem.Particle[128];
    private readonly Vector3[] dustV=new Vector3[Grains*4],pointV=new Vector3[Stars*4],linkV=new Vector3[(Stars-4)*4],floorV=new Vector3[4],nodes=new Vector3[Stars];
    private readonly Color[] dustC=new Color[Grains*4],pointC=new Color[Stars*4],linkC=new Color[(Stars-4)*4];
    private readonly Material floorMat;
    private readonly LineRenderer border,borderGlow;
    private readonly Vector3[] boundary=new Vector3[Segments+1];
    private struct Grain {public float Along,Side,Height,Size,Speed;}
    private readonly Grain[] seeds=new Grain[Grains];
    private sealed class Wave
    {
        public float Birth=-100,Life;public bool Final;
        public Mesh Mesh;public MeshRenderer Renderer;
        public readonly Vector3[] V=new Vector3[(Segments+1)*WaveBands];
        public readonly Color[] C=new Color[(Segments+1)*WaveBands];
    }
    private readonly Wave[] waves={new Wave(),new Wave(),new Wave(),new Wave()};
    private int waveCursor;
    private float time,stop=-100,radius=2,entrance,fadeSeconds=1.55f;
    private bool interrupted;
    private bool active,disposed;
    public bool Active=>active;
    public float Radius=>radius;
    public PNStarchanterHealingZone(Transform parent)
    {
        root=new GameObject("Starchanter floating galaxy healing field");root.transform.SetParent(parent,false);root.layer=parent.gameObject.layer;
        try
        {
            BuildCloudParticles(0,"SmokeBody",14, .60f,.95f);
            BuildCloudParticles(1,"SmokeWisps",7, .70f,1.05f);
            BuildCloudParticles(2,"SmokeBody",12, .45f,.75f);
            var glow=StarMaterial("Glow",Color.white,5);
            dust=Create("Fine suspended stardust",dustV.Length,QuadTriangles(Grains),QuadUV(Grains),glow,out _);
            points=Create("Floating constellation nodes",pointV.Length,QuadTriangles(Stars),QuadUV(Stars),glow,out _);
            links=Create("Floating constellation links",linkV.Length,QuadTriangles(Stars-4),QuadUV(Stars-4),StarMaterial(null,Color.white,0),out _);
            floorMat=StarMaterial("Glow",new Color(.18f,.55f,.34f),5);
            floor=Create("Faint area footprint",4,QuadTriangles(1),QuadUV(1),floorMat,out _);
            floor.colors=new[]{Color.white,Color.white,Color.white,Color.white};
            border=Boundary("Exact healing radius",StarMaterial(null,Color.white,0),.007f);
            borderGlow=Boundary("Soft healing radius halo",StarMaterial("Ribbon",Color.white,5),.035f);
            var waveMat=CloudMaterial(2,true);
            var uv=new Vector2[(Segments+1)*WaveBands];for(int i=0;i<=Segments;i++)for(int j=0;j<WaveBands;j++)uv[i*WaveBands+j]=new Vector2(i/(float)Segments,j/(float)(WaveBands-1));
            foreach(var w in waves) {w.Mesh=Create("Outward healing energy front",w.V.Length,StripTriangles(1,Segments,WaveBands),uv,waveMat,out var r);w.Renderer=r;r.enabled=false;}
            for(int i=0;i<Grains;i++)seeds[i]=new Grain{Along=Hash(i+7),Side=Hash(i+17)-.5f,Height=Hash(i+29),Size=.002f+Hash(i+67)*.004f,Speed=.018f+Hash(i+79)*.025f};
        }
        catch{Dispose();throw;}
    }
    private Material CloudMaterial(int layer,bool additive)
    {
        var shader=Resources.Load<Shader>("PNStarchanterHealingField");
        var mask=Resources.Load<Texture2D>("StarchanterVfx/Cloud");var flow=Resources.Load<Texture2D>("StarchanterVfx/Flow");
        if(shader==null||mask==null||flow==null)throw new InvalidOperationException("Starchanter cloud shader/masks missing");
        var m=new Material(shader){name="Starchanter resonance galaxy "+layer,hideFlags=HideFlags.HideAndDontSave};materials.Add(m);
        m.SetFloat("_Layer",layer);m.SetFloat("_DstBlend",additive?1:10);m.SetTexture("_CloudTex",mask);m.SetTexture("_FlowTex",flow);return m;
    }
    private void BuildCloudParticles(int index,string texture,float rate,float small,float large)
    {
        var material=CloudMaterial(3,false);smokeMaterials[index]=material;
        var smoke=Resources.Load<Texture2D>("StarchanterVfx/"+texture);if(smoke==null)throw new InvalidOperationException("Smoke texture missing: "+texture);
        material.SetTexture("_CloudTex",smoke);
        var go=new GameObject(index==0?"Soft cloud particles":index==1?"Fine smoke particles":"Rim cloud particles");go.layer=root.layer;go.transform.SetParent(root.transform,false);
        var ps=go.AddComponent<ParticleSystem>();cloudSystems[index]=ps;ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);ps.useAutoRandomSeed=false;ps.randomSeed=(uint)(7301+index*43);
        var main=ps.main;main.playOnAwake=false;main.loop=true;main.duration=7;main.maxParticles=128;main.simulationSpace=ParticleSystemSimulationSpace.Local;
        main.startLifetime=new ParticleSystem.MinMaxCurve(4,7);main.startSize=new ParticleSystem.MinMaxCurve(small,large);main.startSpeed=0;
        main.startRotation=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);main.startColor=new Color(.34f,.65f,.47f,index==0?.28f:index==1?.38f:.26f);
        var shape=ps.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Circle;shape.radius=radius*(index==2?.86f:.70f);shape.radiusThickness=index==2?.18f:1;shape.rotation=new Vector3(-90,0,0);shape.position=Vector3.up*(index==0?.11f:.19f);
        var emission=ps.emission;emission.enabled=false;emission.rateOverTime=rate;
        var color=ps.colorOverLifetime;color.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.2f),new GradientAlphaKey(.8f,.7f),new GradientAlphaKey(0,1)});color.color=gradient;
        var velocity=ps.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.Local;velocity.x=new ParticleSystem.MinMaxCurve(.025f,.065f);velocity.y=new ParticleSystem.MinMaxCurve(.005f,.015f);velocity.z=new ParticleSystem.MinMaxCurve(-.02f,.035f);
        var rotation=ps.rotationOverLifetime;rotation.enabled=true;rotation.z=new ParticleSystem.MinMaxCurve(-.14f,.14f);
        var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.EaseInOut(0,.8f,1,1.15f));
        var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.sortMode=ParticleSystemSortMode.Distance;renderer.maxParticleSize=3;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
    }
    private Material StarMaterial(string texture,Color tint,int mode)
    {
        var shader=Resources.Load<Shader>("PNStarweaverEffects");if(shader==null)throw new InvalidOperationException("Star shader missing");
        var m=new Material(shader){name="Starchanter resonance galaxy stars",hideFlags=HideFlags.HideAndDontSave};materials.Add(m);
        m.SetFloat("_Mode",mode);m.SetFloat("_DstBlend",1);m.SetColor("_Tint",tint);if(texture!=null)m.SetTexture("_MainTex",Resources.Load<Texture2D>("StarweaverVfx/"+texture));return m;
    }
    private MeshRenderer Draw(string label,Mesh mesh,Material material)
    {
        var go=new GameObject(label);go.layer=root.layer;go.transform.SetParent(root.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;return r;
    }
    private LineRenderer Boundary(string label,Material material,float width)
    {
        var go=new GameObject(label);go.layer=root.layer;go.transform.SetParent(root.transform,false);
        var r=go.AddComponent<LineRenderer>();r.sharedMaterial=material;r.useWorldSpace=false;r.positionCount=boundary.Length;r.widthMultiplier=width;
        r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;return r;
    }
    private Mesh Create(string label,int count,int[] triangles,Vector2[] uv,Material material,out MeshRenderer renderer)
    {var m=new Mesh{name="Starchanter healing "+label,hideFlags=HideFlags.HideAndDontSave};meshes.Add(m);m.MarkDynamic();m.vertices=new Vector3[count];m.uv=uv;m.triangles=triangles;renderer=Draw(label,m,material);return m;}
    private static int[] StripTriangles(int lanes,int along,int across)
    {var t=new int[lanes*along*(across-1)*6];int k=0;for(int l=0;l<lanes;l++)for(int i=0;i<along;i++)for(int j=0;j<across-1;j++){int a=l*(along+1)*across+i*across+j,b=a+across;t[k++]=a;t[k++]=b;t[k++]=a+1;t[k++]=a+1;t[k++]=b;t[k++]=b+1;}return t;}
    private static int[] QuadTriangles(int count)
    {var t=new int[count*6];for(int i=0;i<count;i++){int a=i*4,k=i*6;t[k]=a;t[k+1]=a+1;t[k+2]=a+2;t[k+3]=a;t[k+4]=a+2;t[k+5]=a+3;}return t;}
    private static Vector2[] QuadUV(int count)
    {var u=new Vector2[count*4];for(int i=0;i<count;i++){int k=i*4;u[k]=Vector2.zero;u[k+1]=Vector2.right;u[k+2]=Vector2.one;u[k+3]=Vector2.up;}return u;}
    private static float Hash(int n)=>Mathf.Repeat(Mathf.Sin(n*127.1f+311.7f)*43758.5453f,1);
    public void Channel(bool value,float size) {if(disposed)return;radius=size;if(value&&!active){entrance=time;stop=-100;foreach(var w in waves)w.Birth=-100;foreach(var ps in cloudSystems){var shape=ps.shape;shape.radius=radius*(ps==cloudSystems[2]?.86f:.70f);var emission=ps.emission;emission.enabled=true;ps.Simulate(4,false,true,false);ps.Pause(false);}}else if(!value&&active){foreach(var ps in cloudSystems){var emission=ps.emission;emission.enabled=false;}}active=value;}
    public void Pulse(bool last) {if(disposed)return;var w=waves[waveCursor++%waves.Length];w.Birth=time;w.Life=last?1.55f:1.15f;w.Final=last;}
    public void Stop(bool complete=true)
    {
        if(disposed)return;
        foreach(var ps in cloudSystems){var emission=ps.emission;emission.enabled=false;}
        interrupted=!complete;fadeSeconds=complete?1.55f:.45f;
        if(active){stop=time;if(complete)foreach(var w in waves)if(w.Final&&time-w.Birth<.85f)stop=Mathf.Max(stop,w.Birth+.85f);}
        active=false;
    }
    public void Advance(float dt,Vector3 position) {if(disposed)return;time+=Mathf.Max(0,dt);root.transform.SetPositionAndRotation(position,Quaternion.identity);if(dt>0)foreach(var ps in cloudSystems)ps.Simulate(dt,false,false,false);}
    private float PassingWave(float distance,out float finalPower)
    {
        float power=0;finalPower=0;
        foreach(var w in waves)
        {float p=(time-w.Birth)/w.Life;if(p<0||p>=1)continue;float at=radius*p;float hit=Mathf.Exp(-Mathf.Pow((distance-at)/(w.Final?.27f:.16f),2));power+=hit;if(w.Final)finalPower+=hit;}
        return Mathf.Min(1,power);
    }
    private Vector3 LanePoint(int lane,float u,out Vector3 radial)
    {
        float phase=lane*2.399963f+time*(lane%2==0?.12f:-.08f)+Mathf.Sin(time*(.35f+lane*.012f)+lane)*.09f;
        float span=.95f+Hash(lane+23)*1.25f,a=phase+(u-.5f)*span;
        float r=radius*(.44f+Hash(lane+3)*.43f)+Mathf.Sin(u*Mathf.PI*2+lane+time*.16f)*.07f;
        radial=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
        float wave=PassingWave(r,out var finalPower);
        float y=.04f+(lane%4)*.032f+Mathf.Sin(u*Mathf.PI)*.05f+Mathf.Sin(time*(.65f+lane*.03f)+u*4+lane)*.035f+wave*.04f+finalPower*.16f;
        return radial*r+Vector3.up*y;
    }
    private static void Quad(Vector3[] v,Color[] c,int i,Vector3 at,Vector3 right,Vector3 up,Color color)
    {int k=i*4;v[k]=at-right-up;v[k+1]=at+right-up;v[k+2]=at+right+up;v[k+3]=at-right+up;c[k]=c[k+1]=c[k+2]=c[k+3]=color;}
    public void Draw(Camera camera)
    {
        if(disposed||camera==null)return;
        float fade=active?Mathf.SmoothStep(0,1,(time-entrance)/.45f):stop>-99?1-Mathf.SmoothStep(0,1,(time-stop)/fadeSeconds):0;
        float marks=interrupted&&!active?1-Mathf.SmoothStep(0,1,(time-stop)/.2f):fade;
        for(int i=0;i<boundary.Length;i++){float a=i/(float)Segments*Mathf.PI*2;boundary[i]=new Vector3(Mathf.Cos(a)*radius,.022f,Mathf.Sin(a)*radius);}
        border.SetPositions(boundary);borderGlow.SetPositions(boundary);
        border.startColor=border.endColor=new Color(.40f,1,.62f,marks*.58f);
        borderGlow.startColor=borderGlow.endColor=new Color(.35f,1,.64f,marks*.24f);
        foreach(var m in materials)m.SetFloat("_FxTime",time);
        var right=camera.transform.right;var up=camera.transform.up;
        for(int i=0;i<cloudSystems.Length;i++)
        {
            var mat=smokeMaterials[i];mat.SetFloat("_Opacity",fade);mat.SetFloat("_Radius",radius);mat.SetVector("_FieldCenter",root.transform.position);
            int count=cloudSystems[i].GetParticles(cloudParticles);
            for(int k=0;k<count;k++)
            {
                var p=cloudParticles[k];float wave=PassingWave(new Vector2(p.position.x,p.position.z).magnitude,out var finalPower);
                p.startColor=new Color(.34f+wave*.14f,.65f+wave*.22f,.47f+finalPower*.22f,(i==0?.28f:i==1?.38f:.26f)+wave*.15f);
                cloudParticles[k]=p;
            }
            cloudSystems[i].SetParticles(cloudParticles,count);
        }
        for(int i=0;i<Grains;i++)
        {
            var s=seeds[i];float u=Mathf.Repeat(s.Along+time*s.Speed,1);var p=LanePoint(i%Lanes,u,out var radial);
            p+=radial*s.Side*.35f+Vector3.up*(s.Height*.07f);
            float wave=PassingWave(new Vector2(p.x,p.z).magnitude,out _),blink=.45f+.55f*Mathf.Sin(u*Mathf.PI);
            Quad(dustV,dustC,i,p,right*s.Size,up*s.Size,new Color(.45f,.95f,.65f,fade*blink*(.18f+wave*.33f)));
        }
        dust.vertices=dustV;dust.colors=dustC;dust.RecalculateBounds();
        int edge=0;
        for(int i=0;i<Stars;i++)
        {
            int group=i/5,n=i%5;var at=LanePoint(group*3,.16f+n*.15f,out var radial);at+=radial*(.06f*Mathf.Sin(n*2.3f+group));at.y+=.06f+Mathf.Sin(n*2.1f)*.025f;nodes[i]=at;
            float wave=PassingWave(new Vector2(at.x,at.z).magnitude,out _),size=n==0?.022f:.012f;
            Quad(pointV,pointC,i,at,right*size,up*size,new Color(.75f,1,.45f,marks*(.25f+wave*.70f)));
            if(n==0)continue;var a=nodes[i-1];var side=Vector3.Cross((at-a).normalized,camera.transform.forward)*.0016f;int k=edge++*4;
            linkV[k]=a-side;linkV[k+1]=a+side;linkV[k+2]=at+side;linkV[k+3]=at-side;
            var color=new Color(.58f,.77f,.30f,marks*(.18f+wave*.20f));linkC[k]=linkC[k+1]=linkC[k+2]=linkC[k+3]=color;
        }
        points.vertices=pointV;points.colors=pointC;points.RecalculateBounds();links.vertices=linkV;links.colors=linkC;links.RecalculateBounds();
        floorV[0]=new Vector3(-radius,.018f,-radius);floorV[1]=new Vector3(radius,.018f,-radius);floorV[2]=new Vector3(radius,.018f,radius);floorV[3]=new Vector3(-radius,.018f,radius);floor.vertices=floorV;floor.RecalculateBounds();floorMat.SetFloat("_Opacity",fade*.035f);
        foreach(var w in waves)
        {
            float p=(time-w.Birth)/w.Life;w.Renderer.enabled=p>=0&&p<1&&fade>0;if(!w.Renderer.enabled)continue;
            float front=radius*p,width=w.Final?.40f:.24f;
            for(int i=0;i<=Segments;i++)
            {
                float a=i/(float)Segments*Mathf.PI*2,warp=.025f*Mathf.Sin(a*7+time*2),cos=Mathf.Cos(a),sin=Mathf.Sin(a);
                for(int j=0;j<WaveBands;j++)
                {
                    float v=j/(float)(WaveBands-1),r=Mathf.Max(0,front+(v-.5f)*width+warp);int k=i*WaveBands+j;
                    float soft=Mathf.Sin(v*Mathf.PI),y=.025f+soft*(w.Final?.32f:.13f)*(1-p*.45f);
                    w.V[k]=new Vector3(cos*r,y,sin*r);
                    w.C[k]=new Color(w.Final?.45f:.30f,1.1f,w.Final?.95f:.65f,fade*soft*Mathf.Sin(p*Mathf.PI)*(w.Final?1.0f:.75f));
                }
            }
            w.Mesh.vertices=w.V;w.Mesh.colors=w.C;w.Mesh.RecalculateBounds();
        }
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;if(root!=null)root.SetActive(false);
        foreach(var m in meshes)DestroyOwned(m);foreach(var m in materials)DestroyOwned(m);DestroyOwned(root);meshes.Clear();materials.Clear();
    }
    private static void DestroyOwned(UnityEngine.Object o) {if(o==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(o);else UnityEngine.Object.DestroyImmediate(o);}
}
#endif
