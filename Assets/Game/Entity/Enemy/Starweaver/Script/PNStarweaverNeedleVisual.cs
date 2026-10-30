#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

// Implementation-owned presentation. Flight uses projectile Age; the detached impact uses world GameplayTime.
// No dependency on the shooter's lifetime, and no autonomous movement or damage.
public sealed class PNStarweaverNeedleVisual : MonoBehaviour, IPNCameraFacing
{
    private const int Samples=40;
    private readonly List<Material> materials=new List<Material>();
    private readonly List<Mesh> meshes=new List<Mesh>();
    private readonly Vector3[] path=new Vector3[Samples];
    private readonly Vector3[][] threads={new Vector3[Samples],new Vector3[Samples]};
    private readonly Transform[] fragments=new Transform[12];
    private PNProjectileInstance owner;
    private EveWorldContext world;
    private Camera preview;
    private Transform head,glow,ring;
    private LineRenderer tail,core;
    private readonly LineRenderer[] gold=new LineRenderer[2];
    private readonly MaterialPropertyBlock[] threadBlocks=new MaterialPropertyBlock[2];
    private Material tailMat,coreMat,goldMat,glowMat,ringMat;
    private Vector3 position,heading,start;
    private float age,scale=1;
    private double impactStart;
    private bool impact,released,hasStart;
    public float VisualAge=>age;
    public static PNStarweaverNeedleVisual Create(PNProjectileInstance owner)
    {
        var go=new GameObject("Starweaver golden needle visual");go.layer=PNLayers.Projectile;go.transform.SetParent(owner.transform,false);
        var v=go.AddComponent<PNStarweaverNeedleVisual>();v.owner=owner;
        try{v.Build();return v;}catch{v.Dispose();throw;}
    }
    // Also used by the native render check; it does not construct a projectile outside the registry.
    public static PNStarweaverNeedleVisual Preview(Scene scene,Camera camera,bool impact=false)
    {
        var go=new GameObject(impact?"Needle impact preview":"Needle flight preview");SceneManager.MoveGameObjectToScene(go,scene);go.layer=PNLayers.Projectile;
        var v=go.AddComponent<PNStarweaverNeedleVisual>();v.preview=camera;v.impact=impact;
        try{v.Build();return v;}catch{v.Dispose();throw;}
    }
    private Material Material(int mode,string mask,Color tint,bool additive)
    {
        var shader=Resources.Load<Shader>("PNStarweaverEffects");if(shader==null)throw new InvalidOperationException("Starweaver shader missing");
        var m=new Material(shader){name="Starweaver needle "+mode,hideFlags=HideFlags.HideAndDontSave};materials.Add(m);
        m.SetFloat("_Mode",mode);m.SetFloat("_DstBlend",additive?1:10);m.SetColor("_Tint",tint);
        m.SetTexture("_NoiseTex",Resources.Load<Texture2D>("StarweaverVfx/Noise"));
        if(mask!=null)m.SetTexture("_MainTex",Resources.Load<Texture2D>("StarweaverVfx/"+mask));return m;
    }
    private Mesh Mesh(string name,Vector3[] vertices,int[] triangles,Vector2[] uv,Color[] colors=null)
    {
        var m=new Mesh{name=name,hideFlags=HideFlags.HideAndDontSave};meshes.Add(m);m.vertices=vertices;m.triangles=triangles;m.uv=uv;
        if(colors!=null)m.colors=colors;else{var c=new Color[vertices.Length];for(int i=0;i<c.Length;i++)c[i]=Color.white;m.colors=c;}
        m.RecalculateBounds();return m;
    }
    private Transform Draw(string name,Mesh mesh,Material mat)
    {
        var go=new GameObject(name);go.layer=PNLayers.Projectile;go.transform.SetParent(transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=mat;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;return go.transform;
    }
    private LineRenderer Line(string name,Material mat,float width)
    {
        var go=new GameObject(name);go.layer=PNLayers.Projectile;go.transform.SetParent(transform,false);var r=go.AddComponent<LineRenderer>();r.sharedMaterial=mat;r.useWorldSpace=true;r.positionCount=Samples;r.startWidth=0;r.endWidth=width;r.startColor=new Color(1,1,1,0);r.endColor=Color.white;return r;
    }
    private void Build()
    {
        goldMat=Material(impact?0:13,null,impact?new Color(1.65f,1.05f,.24f):Color.white,false);glowMat=Material(5,"Glow",new Color(.8f,.22f,1.4f),true);
        var quad=Mesh("Needle glow quad",new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)},new[]{0,1,2,0,2,3},new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});
        glow=Draw("Violet sheath glow",quad,glowMat);
        if(!impact)
        {
            // Elongated hollow gold eye and sharp tip: +X follows projected flight heading.
            var v=new Vector3[8];var uv=new Vector2[8];var colors=new Color[8];var tri=new int[24];
            var outer=new[]{new Vector3(.29f,0,0),new Vector3(-.05f,.048f,0),new Vector3(-.23f,0,0),new Vector3(-.05f,-.048f,0)};
            for(int i=0;i<4;i++){v[i]=outer[i];v[i+4]=Vector3.Lerp(new Vector3(-.04f,0,0),outer[i],.5f);colors[i]=i%2==0?new Color(1,.96f,.85f):new Color(.68f,.64f,.52f);colors[i+4]=new Color(1,.98f,.9f);int k=i*6,n=(i+1)%4;tri[k]=i;tri[k+1]=n;tri[k+2]=n+4;tri[k+3]=i;tri[k+4]=n+4;tri[k+5]=i+4;}
            for(int i=0;i<8;i++)uv[i]=new Vector2((v[i].x+.23f)/.52f,v[i].y/.096f+.5f);
            head=Draw("Golden needle eye",Mesh("Hollow gold needle",v,tri,uv,colors),goldMat);
            tailMat=Material(10,"Ribbon",Color.white,false);coreMat=Material(5,"Ribbon",new Color(.55f,.09f,.85f),true);
            goldMat.renderQueue=3010;glowMat.SetFloat("_Opacity",.55f);
            tail=Line("Violet energy wake",tailMat,.35f);
            tail.widthCurve=new AnimationCurve(new Keyframe(0,0),new Keyframe(.22f,.4f),new Keyframe(.65f,1),new Keyframe(.9f,.5f),new Keyframe(1,.12f));
            core=Line("Luminous wake core",coreMat,.042f);
            for(int i=0;i<2;i++){threadBlocks[i]=new MaterialPropertyBlock();gold[i]=Line("Woven gold filament "+i,goldMat,.008f);}
        }
        else
        {
            ringMat=Material(12,"Noise",Color.white,false);ring=Draw("Needle contact ripple",quad,ringMat);
            var star=Mesh("Needle impact star",new[]{Vector3.zero,new Vector3(0,.5f,0),new Vector3(.1f,.1f,0),new Vector3(.4f,0,0),new Vector3(.1f,-.1f,0),new Vector3(0,-.5f,0),new Vector3(-.1f,-.1f,0),new Vector3(-.4f,0,0),new Vector3(-.1f,.1f,0)},new[]{0,1,2,0,2,3,0,3,4,0,4,5,0,5,6,0,6,7,0,7,8,0,8,1},new Vector2[9]);
            for(int i=0;i<fragments.Length;i++)fragments[i]=Draw("Contact star "+i,star,goldMat);
        }
        if(Application.isPlaying)PNCameraFacingSystem.Register(this);
    }
    public void Begin(PNProjectileSpawn spawn,float visualScale)
    {world=owner.World;hasStart=true;start=position=spawn.Position;heading=spawn.Direction;age=0;scale=visualScale;gameObject.SetActive(true);Render(position,heading,0);}
    public void Render(Vector3 point,Vector3 direction,float elapsed)
    {
        if(!hasStart){start=point;hasStart=true;}position=point;heading=direction.normalized;age=elapsed;
        foreach(var m in materials)m.SetFloat("_FxTime",age);
        Align();
    }
    public bool Align()
    {
        if(released)return false;
        var camera=PNCameraFacing.ResolveCamera(preview,Application.isPlaying);if(camera==null)return false;
        var forward=camera.transform.forward;var right=Vector3.ProjectOnPlane(heading,forward).normalized;if(right.sqrMagnitude<.01f)right=camera.transform.right;
        var up=Vector3.Cross(forward,right).normalized;var rotation=Quaternion.LookRotation(forward,up);
        // Only the visible billboard is lifted; physics/hitbox/launch position are untouched.
        var lift=impact?Vector3.up*.10f:Vector3.zero;
        if(!impact)
        {
            head.SetPositionAndRotation(position+lift,rotation);head.localScale=Vector3.one*scale;
            glow.SetPositionAndRotation(position+lift,rotation);glow.localScale=new Vector3(.48f,.23f,1)*scale;
            float length=Mathf.Min(1.15f*scale,(position-start).magnitude);var side=Vector3.Cross(forward,heading).normalized;
            for(int j=0;j<Samples;j++)
            {
                float u=j/(float)(Samples-1),back=(1-u)*length;
                path[j]=position+lift-heading*back+side*(Mathf.Sin(u*10-age*13)*.018f*(1-u));
            }
            tail.SetPositions(path);core.SetPositions(path);
            tail.widthMultiplier=.35f*scale;core.endWidth=.035f*scale;
            // Each strand grows backwards from the needle, briefly holds, then dissolves.
            // One broad asymmetric arc per birth; no repeating turns or helix around the wake.
            for(int i=0;i<2;i++)
            {
                float period=i==0?.52f:.67f,elapsed=age-i*.14f;
                int birth=Mathf.FloorToInt(Mathf.Max(0,elapsed)/period);float phase=Mathf.Repeat(Mathf.Max(0,elapsed)/period,1);
                bool visible=elapsed>=0&&phase<.76f&&length>.025f;gold[i].gameObject.SetActive(visible);if(!visible)continue;
                float reach=Mathf.SmoothStep(0,1,Mathf.Clamp01(phase/.24f));
                float fade=Mathf.Clamp01((.76f-phase)/.26f);
                float variant=.5f+.5f*Mathf.Sin(birth*2.17f+i*3.1f);
                float extent=length*reach*(.58f+variant*.42f),bend=(i==0?1:-1)*(.06f+variant*.12f)*scale;
                for(int j=0;j<Samples;j++)
                {
                    float u=j/(float)(Samples-1),back=(1-u)*extent;
                    float free=1-u,lag=free*.12f,softTime=age-lag;
                    float arc=Mathf.Sin(u*Mathf.PI)*(.7f+.3f*u);
                    // Retarded phase down the strand: the free tail responds later than its root.
                    // Low uneven travelling bends, not turns around the projectile axis.
                    float wave=Mathf.Sin(softTime*13-free*5.5f+i*2.2f+birth*.7f);
                    float flutter=Mathf.Sin(softTime*21.7f-free*3+variant*4)*.25f;
                    float slack=Mathf.Pow(free,.65f);
                    float offset=arc*bend*(.75f+.25f*Mathf.Sin(softTime*6))+(wave+flutter)*slack*.045f*scale;
                    threads[i][j]=position+lift-heading*(.055f*scale+back)+side*offset+forward*(Mathf.Sin(softTime*9+i)*slack*.012f*scale);
                }
                gold[i].SetPositions(threads[i]);gold[i].endWidth=(i==0?.008f:.006f)*scale*fade;
                threadBlocks[i].SetFloat("_Reveal",fade);gold[i].SetPropertyBlock(threadBlocks[i]);
                gold[i].startColor=new Color(1,1,1,0);gold[i].endColor=new Color(1,1,1,fade);
            }
        }
        else
        {
            float fade=Mathf.Clamp01(1-age/.30f);goldMat.SetFloat("_Opacity",fade);glowMat.SetFloat("_Opacity",Mathf.Clamp01(1-age/.12f));ringMat.SetFloat("_Opacity",fade*.8f);
            glow.SetPositionAndRotation(position+lift,rotation);glow.localScale=Vector3.one*(.25f+age*2.5f)*scale;
            ring.SetPositionAndRotation(position+lift,rotation);ring.localScale=Vector3.one*(.12f+age*2.8f)*scale;
            for(int i=0;i<fragments.Length;i++){float a=i*2.399963f;var d=right*Mathf.Cos(a)+up*Mathf.Sin(a);fragments[i].SetPositionAndRotation(position+lift+d*(.04f+age*(.5f+(i%3)*.22f))*scale,rotation);fragments[i].localScale=Vector3.one*(.035f+(i%3)*.018f)*fade*scale;}
        }
        return true;
    }
    private void LateUpdate()
    {
        if(released)return;
        if(impact)
        {
            if(world!=null&&(!world.Session.IsActive||world.State==EveWorldState.Unloading||world.State==EveWorldState.Disposed||world.State==EveWorldState.Failed)){Dispose();return;}
            float elapsed=world!=null?(float)(world.GameplayTime-impactStart):age+Time.deltaTime;
            Render(position,heading,elapsed);if(age>=.32f)Dispose();
        }
        else if(owner!=null&&owner.IsRunning)Render(owner.Position,owner.Forward,owner.Age);
    }
    public void Contact(Vector3 point)
    {
        if(!Application.isPlaying)return;
        var v=Preview(gameObject.scene,null,true);v.gameObject.name="Starweaver needle contact";v.world=world;v.impactStart=world?.GameplayTime??0;v.scale=scale;v.Render(point,heading,0);PNWorldPresentationRegistration.Register(world,v.gameObject);
    }
    public void End(){gameObject.SetActive(false);}
    private void OnDisable(){PNCameraFacingSystem.Unregister(this);}
    private void OnEnable(){if(Application.isPlaying&&!released)PNCameraFacingSystem.Register(this);}
    public void Dispose(){if(released)return;Release();DestroyResource(gameObject);}
    private void Release(){if(released)return;released=true;PNCameraFacingSystem.Unregister(this);foreach(var m in materials)DestroyResource(m);foreach(var m in meshes)DestroyResource(m);materials.Clear();meshes.Clear();}
    private void OnDestroy(){Release();}
    private static void DestroyResource(UnityEngine.Object o){if(o==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(o);else UnityEngine.Object.DestroyImmediate(o);}
}
#endif
