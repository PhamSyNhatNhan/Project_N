#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.Rendering;

// Cast-owned, presentation-only. Departure stays at its world position after the entity teleports.
public sealed class PNStarchanterTeleportVisual : IDisposable
{
    private const int Count=96;
    private readonly GameObject root;
    private readonly Mesh mesh;
    private readonly Material material,threadMaterial;
    private readonly Vector3[] vertices=new Vector3[Count*4];
    private readonly Color[] colors=new Color[Count*4];
    private readonly LineRenderer[] threads=new LineRenderer[6];
    private readonly LineRenderer opening;
    private readonly Vector3[] openingPath=new Vector3[65];
    private readonly Vector3[] path=new Vector3[25];
    private Vector3 departure,arrival;
    private float age,outSeconds,inSeconds;
    private bool begun,arrived,disposed;
    public bool HasArrival=>arrived;
    public Vector3 Departure=>departure;
    public PNStarchanterTeleportVisual(Transform parent)
    {
        root=new GameObject("Starchanter teleport stardust");root.layer=parent.gameObject.layer;root.transform.SetParent(parent,false);
        var shader=Resources.Load<Shader>("PNStarweaverEffects");if(shader==null)throw new InvalidOperationException("Teleport star shader missing");
        material=new Material(shader){name="Starchanter resonance teleport motes",hideFlags=HideFlags.HideAndDontSave};
        material.SetFloat("_Mode",5);material.SetFloat("_DstBlend",1);material.SetTexture("_MainTex",Resources.Load<Texture2D>("StarweaverVfx/Glow"));
        threadMaterial=new Material(shader){name="Starchanter resonance teleport wisps",hideFlags=HideFlags.HideAndDontSave};
        threadMaterial.SetFloat("_Mode",22);threadMaterial.SetFloat("_DstBlend",1);threadMaterial.SetColor("_Tint",new Color(.85f,.35f,1.4f));threadMaterial.SetTexture("_NoiseTex",Resources.Load<Texture2D>("StarweaverVfx/Noise"));
        var uv=new Vector2[vertices.Length];var triangles=new int[Count*6];
        for(int i=0;i<Count;i++){int k=i*4,t=i*6;uv[k]=Vector2.zero;uv[k+1]=Vector2.right;uv[k+2]=Vector2.one;uv[k+3]=Vector2.up;triangles[t]=k;triangles[t+1]=k+1;triangles[t+2]=k+2;triangles[t+3]=k;triangles[t+4]=k+2;triangles[t+5]=k+3;}
        mesh=new Mesh{name="Starchanter teleport grains",hideFlags=HideFlags.HideAndDontSave};mesh.MarkDynamic();mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles;
        root.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        for(int i=0;i<threads.Length;i++)
        {var go=new GameObject("Teleport rising wisp "+i);go.layer=root.layer;go.transform.SetParent(root.transform,false);var r=go.AddComponent<LineRenderer>();threads[i]=r;r.sharedMaterial=threadMaterial;r.positionCount=path.Length;r.useWorldSpace=false;r.widthMultiplier=.025f;r.enabled=false;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;}
        var ripple=new GameObject("Teleport arrival opening ripple");ripple.layer=root.layer;ripple.transform.SetParent(root.transform,false);
        opening=ripple.AddComponent<LineRenderer>();opening.sharedMaterial=threadMaterial;opening.positionCount=openingPath.Length;opening.useWorldSpace=false;opening.widthMultiplier=.035f;opening.enabled=false;opening.shadowCastingMode=ShadowCastingMode.Off;opening.receiveShadows=false;
    }
    public void Begin(Vector3 position,float vanish,float appear) {departure=position;age=0;outSeconds=vanish;inSeconds=appear;begun=true;arrived=false;}
    public void Arrive(Vector3 position) {if(!begun||disposed)return;arrival=position;arrived=true;}
    public void SetAge(float value) {age=value;}
    public void Cancel() {begun=false;}
    private static float Seed(int i)=>Mathf.Repeat(Mathf.Sin(i*127.1f+27.7f)*43758.5453f,1);
    public void Draw(Camera camera)
    {
        if(disposed||camera==null)return;
        // Fixed-world geometry: moving the owner cannot drag the departure burst along with it.
        root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
        bool visible=begun&&outSeconds>0&&inSeconds>0&&age<outSeconds+inSeconds+.35f;
        root.GetComponent<MeshRenderer>().enabled=visible;
        if(!visible)
        {
            // Not begun (Boost also aligns this presentation): durations are zero, so skip the divisions that would make NaN vertices.
            opening.enabled=false;
            for(int i=0;i<threads.Length;i++)threads[i].enabled=false;
            return;
        }
        var right=camera.transform.right;var up=camera.transform.up;
        material.SetFloat("_FxTime",age);threadMaterial.SetFloat("_FxTime",age);
        for(int i=0;i<Count;i++)
        {
            bool target=i>=Count/2;float p=target?(age-outSeconds)/inSeconds:age/outSeconds;
            bool on=visible&&(!target||arrived)&&p>=0&&p<1.35f;
            float u=Mathf.Clamp01(p),a=i*2.39996f+(target?-u:u)*2.5f;
            float spread=(target?1-u:u)*(.08f+Seed(i+3)*.16f);
            var center=target?arrival:departure;
            float h=target?(.04f+Seed(i+9)*.33f)*u:(.04f+Seed(i+9)*.30f)+u*.10f;
            var at=center+right*(Mathf.Cos(a)*spread)+Vector3.forward*(Mathf.Sin(a)*spread*.55f)+Vector3.up*h;
            float alpha=on?Mathf.Sin(Mathf.Clamp01(p/1.35f)*Mathf.PI):0;
            float size=.005f+Seed(i+17)*.007f;
            var color=i%5==0?new Color(1.15f,.70f,1.4f,alpha):new Color(.65f,.25f,1.3f,alpha);
            int k=i*4;var r=right*size;var v=up*size;vertices[k]=at-r-v;vertices[k+1]=at+r-v;vertices[k+2]=at+r+v;vertices[k+3]=at-r+v;
            colors[k]=colors[k+1]=colors[k+2]=colors[k+3]=color;
        }
        mesh.vertices=vertices;mesh.colors=colors;mesh.RecalculateBounds();
        float q=(age-outSeconds-inSeconds*.6f)/(inSeconds*.4f+.25f);
        opening.enabled=visible&&arrived&&q>=0&&q<1;
        if(opening.enabled)
        {float r=.06f+q*.38f;for(int j=0;j<openingPath.Length;j++){float a=j/(float)(openingPath.Length-1)*Mathf.PI*2;openingPath[j]=arrival+new Vector3(Mathf.Cos(a)*r,.025f,Mathf.Sin(a)*r);}opening.SetPositions(openingPath);opening.startColor=opening.endColor=new Color(1,1,1,Mathf.Sin(q*Mathf.PI)*.7f);}
        for(int i=0;i<threads.Length;i++)
        {
            bool target=i>=3;float p=target?(age-outSeconds)/inSeconds:age/outSeconds;
            bool on=visible&&(!target||arrived)&&p>=0&&p<1;var line=threads[i];line.enabled=on;if(!on)continue;
            float intensity=Mathf.Sin(p*Mathf.PI)*.75f;line.startColor=new Color(1,1,1,intensity);line.endColor=new Color(1,1,1,0);
            var at=target?arrival:departure;
            for(int j=0;j<path.Length;j++){float u=j/(float)(path.Length-1),a=u*3+i*2.1f+age*4,rad=Mathf.Sin(u*Mathf.PI)*.065f*(target?1-p:p);path[j]=at+Vector3.up*(.02f+u*.43f)+right*Mathf.Cos(a)*rad+camera.transform.forward*Mathf.Sin(a)*rad;}
            line.SetPositions(path);
        }
    }
    public void Dispose()
    {if(disposed)return;disposed=true;if(root!=null)root.SetActive(false);foreach(var o in new UnityEngine.Object[]{root,mesh,material,threadMaterial})if(o!=null){if(Application.isPlaying)UnityEngine.Object.Destroy(o);else UnityEngine.Object.DestroyImmediate(o);}}
}
#endif
