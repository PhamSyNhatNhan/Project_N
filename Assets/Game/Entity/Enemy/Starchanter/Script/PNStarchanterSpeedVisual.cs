#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// One caster-owned downlight at the registered buff radius. No recipient component or persistent buff art.
public sealed class PNStarchanterSpeedVisual : IDisposable
{
    private readonly GameObject root;
    private readonly List<Material> materials=new List<Material>();
    private readonly List<Mesh> meshes=new List<Mesh>();
    private const int StarCount=28;
    private const float HaloFade=.70f,StarFadeStart=.50f,StarFadeDuration=.48f;
    private readonly Mesh stars;
    private readonly Vector3[] starVertices=new Vector3[StarCount*4];
    private readonly Color[] starColors=new Color[StarCount*4];
    private readonly Material starMaterial;
    private readonly Material curtainMaterial,floorMaterial;
    private float time,birth=-100,age,windup,radius;private bool preparing;
    public bool Visible=>birth>=0&&time-birth<HaloFade;
    public float Lift=>preparing&&birth<0?Mathf.SmoothStep(0,1,age/Mathf.Max(.01f,windup)):Visible?1-Mathf.SmoothStep(0,1,(time-birth)/.38f):0;
    public PNStarchanterSpeedVisual(Transform parent)
    {
        root=new GameObject("Starchanter one-shot golden downlight");root.layer=parent.gameObject.layer;root.transform.SetParent(parent,false);
        try
        {
            curtainMaterial=Make(5,"StarchanterVfx/SmokeBody",new Color(1.3f,1.05f,.5f));
            floorMaterial=Make(5,"StarweaverVfx/Glow",new Color(1.15f,.86f,.3f));
            Draw("Soft gold downlight volume",Curtain(),curtainMaterial);
            Draw("Brief buff footprint",Floor(),floorMaterial);
            starMaterial=Make(15,null,new Color(1.5f,1.15f,.5f));
            stars=new Mesh{name="Blessing star cloud",hideFlags=HideFlags.HideAndDontSave};meshes.Add(stars);
            var uv=new Vector2[StarCount*4];var triangles=new int[StarCount*6];
            for(int i=0;i<StarCount;i++){int v=i*4,t=i*6;uv[v]=Vector2.zero;uv[v+1]=Vector2.right;uv[v+2]=Vector2.one;uv[v+3]=Vector2.up;triangles[t]=v;triangles[t+1]=v+1;triangles[t+2]=v+2;triangles[t+3]=v;triangles[t+4]=v+2;triangles[t+5]=v+3;}
            stars.vertices=starVertices;stars.uv=uv;stars.colors=starColors;stars.triangles=triangles;Draw("Lingering gold blessing stars",stars,starMaterial);
            DrawFrame(Vector3.zero);
        }
        catch{Dispose();throw;}
    }
    private Material Make(int mode,string texture,Color tint)
    {
        var m=new Material(Resources.Load<Shader>("PNStarweaverEffects")){name="Starchanter resonance downlight "+mode,hideFlags=HideFlags.HideAndDontSave};materials.Add(m);
        m.SetFloat("_Mode",mode);m.SetFloat("_DstBlend",1);m.SetColor("_Tint",tint);m.SetTexture("_NoiseTex",Resources.Load<Texture2D>("StarweaverVfx/Noise"));
        if(texture!=null)m.SetTexture("_MainTex",Resources.Load<Texture2D>(texture));return m;
    }
    private void Draw(string name,Mesh mesh,Material material)
    {
        var go=new GameObject(name);go.layer=root.layer;go.transform.SetParent(root.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
    }
    private Mesh Curtain()
    {
        const int segments=64,bands=7;var v=new Vector3[(segments+1)*bands];var uv=new Vector2[v.Length];var c=new Color[v.Length];var tri=new int[segments*(bands-1)*6];int k=0;
        for(int i=0;i<=segments;i++)for(int j=0;j<bands;j++)
        {
            float u=i/(float)segments,h=j/(float)(bands-1),a=u*Mathf.PI*2;int n=i*bands+j;
            v[n]=new Vector3(Mathf.Cos(a)*(1-h),h*6f,Mathf.Sin(a)*(1-h));uv[n]=new Vector2(u*3,h);
            c[n]=new Color(1,1,1,Mathf.Sin(h*Mathf.PI)*(.25f+.15f*Mathf.Sin(a*5)));
            if(i<segments&&j<bands-1){tri[k++]=n;tri[k++]=n+bands;tri[k++]=n+1;tri[k++]=n+1;tri[k++]=n+bands;tri[k++]=n+bands+1;}
        }
        var mesh=new Mesh{name="Buff inverted V cone",hideFlags=HideFlags.HideAndDontSave};meshes.Add(mesh);mesh.vertices=v;mesh.uv=uv;mesh.colors=c;mesh.triangles=tri;mesh.RecalculateBounds();return mesh;
    }
    private Mesh Floor()
    {
        var mesh=new Mesh{name="Buff light footprint",hideFlags=HideFlags.HideAndDontSave};meshes.Add(mesh);
        const int n=64;var v=new Vector3[n+1];var uv=new Vector2[n+1];var c=new Color[n+1];var tri=new int[n*3];v[0]=new Vector3(0,.018f,0);uv[0]=Vector2.one*.5f;c[0]=Color.white;
        for(int i=0;i<n;i++){float a=i/(float)n*Mathf.PI*2;v[i+1]=new Vector3(Mathf.Cos(a),.018f,Mathf.Sin(a));uv[i+1]=new Vector2(v[i+1].x,v[i+1].z)*.5f+Vector2.one*.5f;c[i+1]=Color.white;tri[i*3]=0;tri[i*3+1]=i+1;tri[i*3+2]=(i+1)%n+1;}
        mesh.vertices=v;mesh.uv=uv;mesh.colors=c;mesh.triangles=tri;mesh.RecalculateBounds();return mesh;
    }
    public void Prepare(float elapsed,float castWindup,float size){if(!preparing)birth=-100;preparing=true;age=elapsed;windup=castWindup;radius=size;}
    public void Pulse(){birth=time;}
    public void End(bool complete){preparing=false;if(!complete)birth=-100;}
    public void Advance(float dt){time+=Mathf.Max(0,dt);foreach(var m in materials)m.SetFloat("_FxTime",time);}
    public void DrawFrame(Vector3 position,Camera camera=null)
    {
        root.transform.SetPositionAndRotation(position,Quaternion.identity);root.transform.localScale=new Vector3(radius,1,radius);
        float p=(time-birth)/HaloFade,fade=Visible?(1-Mathf.SmoothStep(0,1,p)):0;
        curtainMaterial.SetFloat("_Opacity",fade*.85f);floorMaterial.SetFloat("_Opacity",fade*.20f);
        float elapsed=time-birth,starFade=birth>=0?1-Mathf.SmoothStep(0,1,(elapsed-StarFadeStart)/StarFadeDuration):0;
        starMaterial.SetFloat("_Opacity",starFade);
        if(camera!=null&&starFade>0)
        {
            for(int i=0;i<StarCount;i++)
            {
                float seed=Mathf.Repeat(Mathf.Sin(i*127.1f+19.7f)*43758.5453f,1),h=.20f+seed*2.2f-Mathf.Max(0,elapsed)*.14f;
                float a=i*2.39996f,d=(1-h/6f)*(.25f+.68f*Mathf.Sqrt(Mathf.Repeat(i*.754877f+.3f,1)));
                var at=new Vector3(Mathf.Cos(a)*d,h,Mathf.Sin(a)*d);
                float size=.045f+(i%4)*.016f,blink=.72f+.28f*Mathf.Sin(i*3+elapsed*8);
                var right=root.transform.InverseTransformVector(camera.transform.right*size);var up=root.transform.InverseTransformVector(camera.transform.up*size);
                int k=i*4;starVertices[k]=at-right-up;starVertices[k+1]=at+right-up;starVertices[k+2]=at+right+up;starVertices[k+3]=at-right+up;
                for(int j=0;j<4;j++)starColors[k+j]=new Color(1,1,1,blink);
            }
            stars.vertices=starVertices;stars.colors=starColors;stars.RecalculateBounds();
        }
    }

    public void Dispose(){if(!ReferenceEquals(root,null)){root.SetActive(false);DestroyOwned(root);}foreach(var m in materials)DestroyOwned(m);foreach(var m in meshes)DestroyOwned(m);materials.Clear();meshes.Clear();}
    private static void DestroyOwned(UnityEngine.Object value){if(ReferenceEquals(value,null))return;if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
}
#endif
