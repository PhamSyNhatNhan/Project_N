#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.Rendering;

// Mob-owned analytic particles and textured ribbons, with explicit entity/projectile age.
// Two reusable batches (alpha ink and additive light); no Update clock or per-frame objects.
public sealed class PNStarreaperEnergyMesh : IDisposable
{
    private sealed class Layer
    {
        private const int Capacity=128;
        public readonly GameObject Object;public readonly Mesh Mesh;public readonly Material Material;
        private readonly Vector3[] v=new Vector3[Capacity*4];private readonly Vector2[] uv=new Vector2[Capacity*4],kind=new Vector2[Capacity*4];
        private readonly Color[] colors=new Color[Capacity*4];private readonly int[] tri=new int[Capacity*6];private int count;
        public Layer(Transform parent,string name,bool additive)
        {
            var shader=Resources.Load<Shader>("PNStarreaperPierce");if(shader==null)throw new InvalidOperationException("Missing Starreaper pierce shader");
            var beam=Texture("BeamTrail");var flow=Texture("FlowTrail");var cover=Texture("CoverTrail");
            Object=new GameObject(name);Object.layer=parent.gameObject.layer;Object.transform.SetParent(parent,false);
            Material=new Material(shader){name="Starreaper pierce "+name,hideFlags=HideFlags.HideAndDontSave};Material.SetFloat("_DstBlend",additive?1:10);
            Material.SetTexture("_NoiseTex",Resources.Load<Texture2D>("StarweaverVfx/Noise"));
            Material.SetTexture("_RainNoiseTex",Texture("RainNoise"));Material.SetTexture("_RainImpactTex",Texture("RainImpactSheet"));Material.SetTexture("_RainDarkTex",Texture("RainDarkSheet"));Material.SetTexture("_RainSmokeTex",Texture("RainSmokeSheet"));
            Material.SetTexture("_BeamTex",beam);Material.SetTexture("_FlowTex",flow);Material.SetTexture("_CoverTex",cover);Material.renderQueue=additive?3012:3010;
            Mesh=new Mesh{name=name,hideFlags=HideFlags.HideAndDontSave};Mesh.MarkDynamic();Object.AddComponent<MeshFilter>().sharedMesh=Mesh;
            var r=Object.AddComponent<MeshRenderer>();r.sharedMaterial=Material;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
            for(int i=0;i<Capacity;i++){int k=i*4,j=i*6;tri[j]=k;tri[j+1]=k+1;tri[j+2]=k+2;tri[j+3]=k;tri[j+4]=k+2;tri[j+5]=k+3;uv[k]=Vector2.zero;uv[k+1]=Vector2.right;uv[k+2]=Vector2.one;uv[k+3]=Vector2.up;}
        }
        private static Texture2D Texture(string name)
        {
            var texture=Resources.Load<Texture2D>("StarreaperVfx/"+name);if(texture==null)throw new InvalidOperationException("Missing Starreaper texture "+name);return texture;
        }
        public void Begin(float age){count=0;Material.SetFloat("_FxTime",age);}
        public void Quad(Vector3 centre,Vector3 along,Vector3 up,float length,float width,Color color,int mode,float u0=0,float u1=1,float frame=0)
        {
            if(count==Capacity)throw new InvalidOperationException("Starreaper visual batch full");
            int k=count++*4;var x=along*(length*.5f);var y=up*(width*.5f);var t=Object.transform;
            v[k]=t.InverseTransformPoint(centre-x-y);v[k+1]=t.InverseTransformPoint(centre+x-y);v[k+2]=t.InverseTransformPoint(centre+x+y);v[k+3]=t.InverseTransformPoint(centre-x+y);
            uv[k]=new Vector2(u0,0);uv[k+1]=new Vector2(u1,0);uv[k+2]=new Vector2(u1,1);uv[k+3]=new Vector2(u0,1);
            for(int j=0;j<4;j++){colors[k+j]=color;kind[k+j]=new Vector2(mode,frame);}
        }
        public void Strip(Vector3 a,Vector3 b,Vector3 acrossA,Vector3 acrossB,float width,Color color,int mode,float u0,float u1)
        {
            if(count==Capacity)throw new InvalidOperationException("Starreaper visual batch full");
            int k=count++*4;var t=Object.transform;var ya=acrossA*(width*.5f);var yb=acrossB*(width*.5f);
            v[k]=t.InverseTransformPoint(a-ya);v[k+1]=t.InverseTransformPoint(b-yb);v[k+2]=t.InverseTransformPoint(b+yb);v[k+3]=t.InverseTransformPoint(a+ya);
            uv[k]=new Vector2(u0,0);uv[k+1]=new Vector2(u1,0);uv[k+2]=new Vector2(u1,1);uv[k+3]=new Vector2(u0,1);
            for(int j=0;j<4;j++){colors[k+j]=color;kind[k+j]=new Vector2(mode,0);}
        }
        public void End(){Mesh.Clear();if(count==0)return;Mesh.SetVertices(v,0,count*4);Mesh.SetUVs(0,uv,0,count*4);Mesh.SetUVs(1,kind,0,count*4);Mesh.SetColors(colors,0,count*4);Mesh.SetTriangles(tri,0,count*6,0);Mesh.RecalculateBounds();}
        public void Dispose(){Destroy(Mesh);Destroy(Material);Destroy(Object);}
    }
    private readonly Layer ink,light;private bool disposed;
    public PNStarreaperEnergyMesh(Transform parent,string name){ink=new Layer(parent,name+" ink",false);try{light=new Layer(parent,name+" light",true);}catch{ink.Dispose();throw;}}
    public void Opacity(float value){ink.Material.SetFloat("_GlobalOpacity",Mathf.Clamp01(value));light.Material.SetFloat("_GlobalOpacity",Mathf.Clamp01(value));}
    public void Begin(float age){ink.Begin(age);light.Begin(age);}
    public void Quad(bool additive,Vector3 c,Vector3 x,Vector3 y,float length,float width,Color color,int mode,float u0=0,float u1=1,float frame=0){(additive?light:ink).Quad(c,x,y,length,width,color,mode,u0,u1,frame);}
    public void Strip(bool additive,Vector3 a,Vector3 b,Vector3 acrossA,Vector3 acrossB,float width,Color color,int mode,float u0,float u1){(additive?light:ink).Strip(a,b,acrossA,acrossB,width,color,mode,u0,u1);}
    public void BlackStar(Vector3 centre,Vector3 x,Vector3 y,Vector3 front,float size,float opacity,float tilt)
    {
        var sx=x*Mathf.Cos(tilt)+y*Mathf.Sin(tilt);var sy=y*Mathf.Cos(tilt)-x*Mathf.Sin(tilt);
        Quad(false,centre+front*4,sx,sy,size,size,new Color(.006f,.002f,.012f,opacity*.98f),14);
        Quad(true,centre+front*5,sx,sy,size,size,new Color(.42f,.055f,.82f,opacity*.50f),15);
    }
    // The same connected body is used while nocked and flying. Core remains the
    // first light quad so shape and string alignment can be measured natively.
    public void Body(Vector3 centre,Vector3 x,Vector3 y,Vector3 front,float scale,float width,float age,float energy,float sheath=1)
    {
        Quad(true,centre+front*4,x,y,.34f*scale,.048f*scale,new Color(.92f,.40f,1.48f,.8f+energy*.2f),5);
        Quad(false,centre+front,x,y,.44f*scale,width*.78f,new Color(.014f,.003f,.032f,.94f),9);
        Quad(true,centre+front*2,x,y,.43f*scale,width*.64f*sheath,new Color(.36f,.05f,.82f,.18f*energy),9);
        Quad(true,centre+front*2,x,y,.46f*scale,width*sheath,new Color(.40f,.065f,.92f,.12f*energy),7);
        Quad(true,centre+front*3,x,y,.41f*scale,width*.46f*sheath,new Color(.50f,.11f,1.03f,.10f*energy),6);
        var normal=Vector3.Cross(x,y).normalized;
        for(int ribbon=0;ribbon<2;ribbon++)for(int i=0;i<10;i++)
        {
            float u0=i/10f,u1=(i+1)/10f;
            var a=BodyPoint(centre,x,y,scale,age,ribbon,u0);var b=BodyPoint(centre,x,y,scale,age,ribbon,u1);var d=b-a;
            var acrossA=BodyAcross(centre,x,y,normal,scale,age,ribbon,u0);var acrossB=BodyAcross(centre,x,y,normal,scale,age,ribbon,u1);
            float w=(ribbon==0?.105f:.078f)*scale;
            Strip(false,a+front,b+front,acrossA,acrossB,w*1.4f,new Color(.014f,.003f,.030f,.90f),10,u0,u1);
            Strip(true,a+front*3,b+front*3,acrossA,acrossB,w*1.4f*sheath,new Color(.44f,.065f,1.02f,.30f*energy),11,u0,u1);
        }
    }
    private static Vector3 BodyPoint(Vector3 centre,Vector3 x,Vector3 y,float scale,float age,int ribbon,float u)
    {
        float envelope=Mathf.Sin(u*Mathf.PI),sign=ribbon==0?1:-1;
        float side=sign*(.048f+Mathf.Sin(age*2.2f+u*4.8f+ribbon*1.7f)*.016f)*envelope;
        return centre+x*Mathf.Lerp(ribbon==0?-.24f:-.20f,ribbon==0?.12f:.17f,u)*scale+y*side*scale;
    }
    private static Vector3 BodyAcross(Vector3 centre,Vector3 x,Vector3 y,Vector3 normal,float scale,float age,int ribbon,float u)
    {
        var d=BodyPoint(centre,x,y,scale,age,ribbon,Mathf.Min(1,u+.001f))-BodyPoint(centre,x,y,scale,age,ribbon,Mathf.Max(0,u-.001f));
        return Vector3.Cross(normal,d).normalized;
    }
    public void End(){ink.End();light.End();}
    public void Clear(){if(disposed)return;Begin(0);End();}
    public void Dispose(){if(disposed)return;disposed=true;ink.Dispose();light.Dispose();}
    private static void Destroy(UnityEngine.Object o){if(o==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(o);else UnityEngine.Object.DestroyImmediate(o);}
}
#endif
