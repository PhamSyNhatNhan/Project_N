using System;
using EveEngine;
using Newtonsoft.Json.Linq;
#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using UnityEngine;

// A scope-owned grant. Different DOT sources share a target/profile renderer,
// while each vulnerability retains its own fixed slot and color.
public sealed class PNBuffElementVisualLease : IDisposable
{
    private PNBuffElementVisual visual;
    internal float Density = .15f;
    internal PNBuffElementVisualLease(PNBuffElementVisual visual) { this.visual = visual; }
    internal static PNBuffElementVisualLease Create(PNStat target, EveId profile, JObject data, PNBuffClock clock)
    {
        PNBuffElementVisual visual = null;
        foreach (var candidate in target.GetComponentsInChildren<PNBuffElementVisual>(true))
            if (candidate.Target == target && candidate.Profile == profile && candidate.HasGrants) { visual = candidate; break; }
        if (visual == null)
        {
            var root = new GameObject("Buff VFX " + profile.Path) { hideFlags = HideFlags.DontSave };
            root.layer = target.gameObject.layer; root.transform.SetParent(target.transform, false);
            try { visual = root.AddComponent<PNBuffElementVisual>(); visual.Initialize(target, profile, data, clock); }
            catch { if (visual != null) visual.RetireResources(); PNBuffElementVisual.Release(root); throw; }
        }
        var lease = new PNBuffElementVisualLease(visual); visual.Add(lease); return lease;
    }
    public void SetStacks(int stacks, int maximum) { Density = Mathf.Clamp01((float)stacks / Math.Max(1, maximum)); }
    public void Pulse() { if (visual != null) visual.Pulse(); }
    public void SetVisualTime(double time) { if (visual != null) visual.SetPresentationTime(time); }
    public void Dispose() { if (visual != null) visual.Remove(this); visual = null; }
}

public sealed class PNBuffElementVisual : MonoBehaviour
{
    public PNStat Target { get; private set; }
    public EveId Profile { get; private set; }
    public bool HasGrants => grants.Count > 0;
    public int GrantCount => grants.Count;
    public float VisualTime => time;
    public Vector3 BodyCenter { get; private set; }
    public float BodyWidth { get; private set; }
    public float BodyHeight { get; private set; }
    public Collider BodyCollider { get; private set; }
    private readonly List<PNBuffElementVisualLease> grants = new List<PNBuffElementVisualLease>();
    private readonly List<Collider> colliders = new List<Collider>();
    private readonly List<Vector3> vertices = new List<Vector3>(1800), normals = new List<Vector3>(1800);
    private readonly List<Color> colors = new List<Color>(1800);
    private readonly List<Vector2> uvs = new List<Vector2>(1800), shapes = new List<Vector2>(1800), contour = new List<Vector2>(80);
    private readonly List<int> bodyTriangles = new List<int>(2700), glowTriangles = new List<int>(2700);
    private static readonly Vector2[] ArrowPoints = {new Vector2(-.27f,.9f),new Vector2(.27f,.9f),new Vector2(.27f,.12f),new Vector2(.8f,.12f),new Vector2(0,-.9f),new Vector2(-.8f,.12f),new Vector2(-.27f,.12f)};
    private Mesh mesh;
    private Material material, glowMaterial;
    private MeshRenderer renderer;
    private EveTimeScale entityClock;
    private PNBuffClock clock;
    private string kind;
    private Color color;
    private float size, time, pulse;
    private bool presentationClock;
    public void SetPresentationTime(double value)
    { float next = (float)value; if (float.IsInfinity(next) || float.IsNaN(next)) throw new OverflowException("Visual clock exceeded float range."); pulse = Mathf.Max(0, pulse - Mathf.Max(0, next - time) * 4); time = next; presentationClock = true; }
    private int slot;
    private Vector3 right, up, forward;
    public void Initialize(PNStat target, EveId profile, JObject data, PNBuffClock selectedClock)
    {
        Target=target;Profile=profile;clock=selectedClock;entityClock=target.GetComponent<EveTimeScale>();
        kind=PNBuffContentBatch.String(data,"kind");slot=PNBuffContentBatch.Integer(data,"slot");size=(float)PNBuffContentBatch.Number(data,"size");
        var rgb=(JArray)data["color"];color=new Color(rgb[0].Value<float>(),rgb[1].Value<float>(),rgb[2].Value<float>(),1);
        var shader=Shader.Find("Project_N/VFX/BuffElemental");
        if(shader==null)throw new InvalidOperationException("Missing Buff elemental shader.");
        material=new Material(shader){name="Buff owned body material",hideFlags=HideFlags.HideAndDontSave};
        glowMaterial=new Material(shader){name="Buff owned glow material",hideFlags=HideFlags.HideAndDontSave};
        material.SetFloat("_DestinationBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        glowMaterial.SetFloat("_DestinationBlend",(float)UnityEngine.Rendering.BlendMode.One);
        mesh=new Mesh{name="Buff glyph mesh",hideFlags=HideFlags.HideAndDontSave};mesh.MarkDynamic();
        gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;renderer=gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterials=new[]{material,glowMaterial};renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
    }
    internal void Add(PNBuffElementVisualLease lease)=>grants.Add(lease);
    internal void Remove(PNBuffElementVisualLease lease)
    {grants.Remove(lease);if(grants.Count==0){gameObject.SetActive(false);RetireResources();Release(gameObject);}}
    public void Pulse()=>pulse=1;
    private void LateUpdate()=>AdvanceVisual(Time.deltaTime,Camera.main);

    // Shape-derived support dimensions projected onto the camera plane, never renderer or collider AABBs.
    private bool ReadBody(Camera camera,out float depth)
    {
        depth=0;Target.GetComponents(colliders);BodyCollider=null;
        foreach(var candidate in colliders)
            if(candidate.enabled && !candidate.isTrigger && (candidate is CapsuleCollider || candidate is BoxCollider || candidate is SphereCollider || candidate is CharacterController))
            {BodyCollider=candidate;break;}
        if(BodyCollider==null)return false;
        var t=BodyCollider.transform;Vector3 center;
        float Width(Vector3 axis,Vector3 halfX,Vector3 halfY,Vector3 halfZ)=>2*(Mathf.Abs(Vector3.Dot(axis,halfX))+Mathf.Abs(Vector3.Dot(axis,halfY))+Mathf.Abs(Vector3.Dot(axis,halfZ)));
        if(BodyCollider is BoxCollider box)
        {
            center=t.TransformPoint(box.center);var x=t.TransformVector(Vector3.right*box.size.x*.5f);var y=t.TransformVector(Vector3.up*box.size.y*.5f);var z=t.TransformVector(Vector3.forward*box.size.z*.5f);
            BodyWidth=Width(right,x,y,z);BodyHeight=Width(up,x,y,z);depth=Width(forward,x,y,z)*.5f;
        }
        else
        {
            float radius,height;int direction=1;Vector3 localCenter;
            if(BodyCollider is CapsuleCollider capsule){radius=capsule.radius;height=capsule.height;direction=capsule.direction;localCenter=capsule.center;}
            else if(BodyCollider is CharacterController character){radius=character.radius;height=character.height;localCenter=character.center;}
            else {var sphere=(SphereCollider)BodyCollider;radius=sphere.radius;height=radius*2;localCenter=sphere.center;direction=-1;}
            center=t.TransformPoint(localCenter);var scale=t.lossyScale;scale=new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));
            var localAxis=direction==0?Vector3.right:direction==2?Vector3.forward:Vector3.up;
            float axialScale=direction==0?scale.x:direction==2?scale.z:scale.y;
            float radialScale=direction==0?Mathf.Max(scale.y,scale.z):direction==2?Mathf.Max(scale.x,scale.y):Mathf.Max(scale.x,scale.z);
            if(direction<0)radialScale=Mathf.Max(scale.x,Mathf.Max(scale.y,scale.z));
            float r=radius*radialScale;float halfSegment=direction<0?0:Mathf.Max(0,height*axialScale*.5f-r);var axis=t.TransformDirection(localAxis).normalized;
            BodyWidth=2*(r+halfSegment*Mathf.Abs(Vector3.Dot(axis,right)));BodyHeight=2*(r+halfSegment*Mathf.Abs(Vector3.Dot(axis,up)));depth=r+halfSegment*Mathf.Abs(Vector3.Dot(axis,forward));
        }
        BodyCenter=center;var inherited=transform.lossyScale;
        return BodyWidth>1e-5f && BodyHeight>1e-5f && Mathf.Abs(inherited.x)>1e-6f && Mathf.Abs(inherited.y)>1e-6f && Mathf.Abs(inherited.z)>1e-6f;
    }
    public void AdvanceVisual(float gameDelta,Camera camera)
    {
        if(!HasGrants || Target==null || camera==null)return;
        float scale=entityClock!=null && entityClock.isActiveAndEnabled ? (clock==PNBuffClock.Game?1:clock==PNBuffClock.Entity?entityClock.Scale:Mathf.Max(0,entityClock.RawScale)):1;
        float delta=gameDelta*scale;if(!presentationClock){time+=delta;pulse=Mathf.Max(0,pulse-delta*4);}
        right=camera.transform.right;up=camera.transform.up;forward=camera.transform.forward;
        renderer.enabled=ReadBody(camera,out float depth);if(!renderer.enabled)return;
        transform.position=BodyCenter-forward*(depth+BodyHeight*.008f);transform.rotation=camera.transform.rotation;
        vertices.Clear();normals.Clear();colors.Clear();uvs.Clear();shapes.Clear();bodyTriangles.Clear();glowTriangles.Clear();
        float density=0;foreach(var grant in grants)density+=grant.Density;density=Mathf.Clamp01(density);
        if(kind=="Arrow")Arrow();else if(kind=="Lightning")Lightning(density);else if(kind=="Fire")Fire(density);else Ice(density);
        mesh.Clear();mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetColors(colors);mesh.SetUVs(0,uvs);mesh.SetUVs(1,shapes);
        mesh.subMeshCount=2;mesh.SetTriangles(bodyTriangles,0);mesh.SetTriangles(glowTriangles,1);mesh.RecalculateBounds();
        material.SetFloat("_BuffTime",time);glowMaterial.SetFloat("_BuffTime",time);
    }
    private Vector3 Local(Vector3 point)=>transform.InverseTransformVector(right*point.x+up*point.y+forward*point.z);
    private void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector2 ua,Vector2 ub,Vector2 uc,Color ca,Color cb,Color cc,bool glow,int shape,float seed=0)
    {
        int start=vertices.Count;var la=Local(a);var lb=Local(b);var lc=Local(c);var normal=Vector3.Cross(lb-la,lc-la).normalized;
        vertices.Add(la);vertices.Add(lb);vertices.Add(lc);for(int i=0;i<3;i++){normals.Add(normal);shapes.Add(new Vector2(shape,seed));}
        uvs.Add(ua);uvs.Add(ub);uvs.Add(uc);colors.Add(ca);colors.Add(cb);colors.Add(cc);
        var indices=glow?glowTriangles:bodyTriangles;indices.Add(start);indices.Add(start+1);indices.Add(start+2);
    }
    private void Quad(Vector2 center,Vector2 half,Color tint,bool glow,int shape=2,float seed=0,float angle=0)
    {
        Vector2 Rotate(Vector2 p)=>new Vector2(p.x*Mathf.Cos(angle)-p.y*Mathf.Sin(angle),p.x*Mathf.Sin(angle)+p.y*Mathf.Cos(angle));
        Vector3 a=center+Rotate(new Vector2(-half.x,-half.y)),b=center+Rotate(new Vector2(-half.x,half.y)),c=center+Rotate(half),d=center+Rotate(new Vector2(half.x,-half.y));
        Triangle(a,b,c,new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),tint,tint,tint,glow,shape,seed);
        Triangle(a,c,d,new Vector2(0,0),new Vector2(1,1),new Vector2(1,0),tint,tint,tint,glow,shape,seed);
    }
    private void Stroke(Vector2 a,Vector2 b,float width,Color tint,bool glow)
    {
        var direction=(b-a).normalized;var side=new Vector2(-direction.y,direction.x)*width;
        Triangle(a-side,a+side,b+side,new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),tint,tint,tint,glow,0);
        Triangle(a-side,b+side,b-side,new Vector2(0,0),new Vector2(1,1),new Vector2(1,0),tint,tint,tint,glow,0);
        Quad(a,Vector2.one*width,tint,glow);Quad(b,Vector2.one*width,tint,glow);
    }
    private Color Tint(float alpha,float intensity=1)=>new Color(color.r*intensity,color.g*intensity,color.b*intensity,alpha);
    private float Unit=>Mathf.Min(size*BodyHeight,BodyWidth*.72f);
    private static float Hash(float value)=>Mathf.Repeat(Mathf.Sin(value*127.1f+311.7f)*43758.5453f,1);
    private void Arrow()
    {
        float unit=Mathf.Min(size*BodyHeight,BodyWidth*.231f);
        // Slot zero sits inside the upper body; signed slots flank it at staggered heights.
        float height=BodyHeight*(slot==0?.35f:slot<0?.12f:-.04f);
        float bob=BodyHeight*(slot==0?.01f:.025f)*Mathf.Sin(time*2.2f);
        var offset=new Vector2(slot*BodyWidth*.75f,height+bob);
        float alpha=.8f+.08f*Mathf.Sin(time*2.2f);contour.Clear();
        for(int i=0;i<ArrowPoints.Length;i++)
        {
            var p=ArrowPoints[i];var enter=Vector2.Lerp(p,ArrowPoints[(i+ArrowPoints.Length-1)%ArrowPoints.Length],.23f);var leave=Vector2.Lerp(p,ArrowPoints[(i+1)%ArrowPoints.Length],.23f);
            for(int k=0;k<=8;k++){float t=k/8f;contour.Add(offset+unit*((1-t)*(1-t)*enter+2*(1-t)*t*p+t*t*leave));}
        }
        for(int i=0;i<contour.Count;i++){var a=contour[i];var b=contour[(i+1)%contour.Count];Stroke(a,b,unit*.12f,Tint(alpha*.12f),true);Stroke(a,b,unit*.045f,Tint(alpha,1.1f),true);}
    }
    // Independent rising particles: each has a stable spawn lane and a shrinking lifetime.
    private void Fire(float density)
    {
        int count=14+Mathf.RoundToInt(density*10);
        for(int i=0;i<count;i++)
        {
            float speed=.48f+Hash(i+2)*.35f,cycle=time*speed+i*.618f;
            float phase=Mathf.Repeat(cycle,1),seed=Mathf.Floor(cycle)+i*31;
            float lane=(Hash(seed+7)-.5f)*BodyWidth*1.18f;
            float drift=Mathf.Sin(phase*3.2f+seed)*BodyWidth*.09f*phase;
            var center=new Vector2(lane+drift,BodyHeight*(-.43f+phase*(.88f+.18f*Hash(seed+4))));
            float fade=Mathf.SmoothStep(0,1,phase/.12f)*(1-Mathf.SmoothStep(0,1,(phase-.5f)/.5f));
            float radius=Unit*(.045f+.055f*Hash(i+11))*(1-.7f*phase);
            var tint=new Color(1.3f,Mathf.Lerp(.74f,.22f,phase),.08f,fade*(.78f+.12f*pulse));
            Quad(center,new Vector2(radius,radius*(1.25f+.65f*Hash(i+5))),tint,true,2,seed,drift*2);
            Quad(center,Vector2.one*radius*2.1f,new Color(1,.25f,.025f,fade*.13f),true,2,seed);
            float ember=Mathf.Repeat(time*.7f+i*.618f,1);
            Quad(new Vector2(lane+Mathf.Sin(i+time)*BodyWidth*.08f,BodyHeight*(-.25f+ember*.72f)),Vector2.one*BodyHeight*.01f,new Color(1.4f,.65f,.12f,Mathf.Sin(ember*Mathf.PI)*.65f),true);
        }
    }
    private void Bolt(Vector2 a,float seed,float alpha)
    {
        var previous=a;Vector2 fork=a;float length=Unit*(.58f+.23f*Hash(seed));
        for(int k=1;k<=5;k++)
        {
            var next=a+new Vector2((Hash(seed+k)-.5f)*Unit*.55f,length*k/5);
            Stroke(previous,next,BodyHeight*.012f,Tint(alpha*.14f),true);
            Stroke(previous,next,BodyHeight*.0035f,new Color(1.4f,1.27f,.56f,alpha),true);
            if(k==3)fork=next;previous=next;
        }
        var branch=fork+new Vector2((Hash(seed+7)>.5f?1:-1)*Unit*.4f,Unit*.22f);
        Stroke(fork,branch,BodyHeight*.0025f,Tint(alpha*.65f,1.5f),true);
    }
    private void Lightning(float density)
    {
        int count=3+Mathf.RoundToInt(density*3);
        for(int i=0;i<count;i++)
        {
            float cycle=time*(2.1f+Hash(i)*1.7f)+i*.382f,phase=Mathf.Repeat(cycle,1);float seed=Mathf.Floor(cycle)+i*17;
            float flash=Mathf.SmoothStep(0,1,phase/.07f)*(1-Mathf.SmoothStep(0,1,(phase-.14f)/.30f));
            float alpha=Mathf.Max(flash*.8f,pulse*.6f);if(alpha<.02f)continue;
            var anchor=new Vector2((i%2==0?-1:1)*BodyWidth*(.43f+.09f*Hash(seed)),BodyHeight*(-.42f+Hash(seed+3)*.69f));Bolt(anchor,seed,alpha);
            Quad(anchor,Vector2.one*BodyHeight*.014f,new Color(1.4f,1.2f,.4f,alpha*.7f),true);
            Quad(anchor,Vector2.one*BodyHeight*.045f,Tint(alpha*.12f),true);
        }
    }
    private void Snowflake(Vector2 center,float radius,float angle,float alpha)
    {
        var core=new Color(.76f,.95f,1,alpha);
        var halo=new Color(.25f,.7f,1,alpha*.18f);
        void Line(Vector2 a,Vector2 b)
        {Stroke(a,b,radius*.085f,halo,true);Stroke(a,b,radius*.028f,core,true);}
        for(int arm=0;arm<6;arm++)
        {
            float turn=angle+arm*Mathf.PI/3;
            var axis=new Vector2(Mathf.Cos(turn),Mathf.Sin(turn));var side=new Vector2(-axis.y,axis.x);
            Line(center,center+axis*radius);
            for(int branch=0;branch<2;branch++)
            {
                float at=branch==0?.46f:.72f,len=branch==0?.25f:.19f;
                var joint=center+axis*(radius*at);
                Line(joint,joint+(axis*.65f+side*.76f)*(radius*len));
                Line(joint,joint+(axis*.65f-side*.76f)*(radius*len));
            }
        }
        Quad(center,Vector2.one*radius*.13f,new Color(.85f,.98f,1,alpha*.6f),true);
    }
    private void Ice(float density)
    {
        int count=4+Mathf.RoundToInt(density*3);
        for(int i=0;i<count;i++)
        {
            float phase=Mathf.Repeat(time*(.13f+Hash(i)*.06f)+i*.618f,1),angle=i*1.7f+time*(i%2==0?.22f:-.18f);
            var center=new Vector2((Hash(i+8)-.5f)*BodyWidth*1.65f+Mathf.Sin(time*.6f+i)*BodyWidth*.08f,BodyHeight*(.5f-phase*.94f));
            float alpha=Mathf.Sin(phase*Mathf.PI)*(.8f+pulse*.12f);
            Snowflake(center,Unit*(.27f+.12f*Hash(i+5)),angle,alpha);
        }
        for(int i=0;i<10;i++)
        {
            float phase=Mathf.Repeat(time*.22f+i*.618f,1);
            var center=new Vector2((Hash(i+18)-.5f)*BodyWidth*1.5f,BodyHeight*(.4f-phase*.82f));
            Quad(center,Vector2.one*BodyHeight*.008f,new Color(.65f,.9f,1,Mathf.Sin(phase*Mathf.PI)*.45f),true);
        }
        for(int i=0;i<3;i++)
        {
            float phase=Mathf.Repeat(time*.16f+i*.33f,1);var center=new Vector2(BodyWidth*(phase-.5f)*1.3f,BodyHeight*(-.44f+Mathf.Sin(time*.5f+i)*.025f));
            Quad(center,new Vector2(BodyWidth*.48f,BodyHeight*.055f),new Color(.48f,.75f,.9f,.12f*Mathf.Sin(phase*Mathf.PI)),false,2,i);
        }
    }
    internal static void Release(UnityEngine.Object value){if(value==null)return;if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
    internal void RetireResources(){Release(mesh);Release(material);Release(glowMaterial);mesh=null;material=null;glowMaterial=null;}
    private void OnDestroy(){grants.Clear();RetireResources();}
}
#else
public sealed class PNBuffElementVisualLease : IDisposable
{
    public void SetStacks(int stacks,int maximum) { }
    public void Pulse() { }
    public void Dispose() { }
}
#endif
