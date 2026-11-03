#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.Rendering;

// A full-shaft weapon wake sampled from the actual fork transforms; no detached crescent.
public sealed class PNStarchanterStrikeVisual : IDisposable
{
    private const int Segments=56;
    private readonly GameObject root;private readonly Material[] materials=new Material[3];private readonly Mesh[] meshes=new Mesh[3];private readonly MeshRenderer[] renderers=new MeshRenderer[3];
    private readonly Vector3[] vertices=new Vector3[(Segments+1)*2];
    private readonly Vector3[] tips=new Vector3[Segments+1],butts=new Vector3[Segments+1];
    private int samples;private float lastSample=-100,distance;private float time,birth=-100,release=-100,windup;private Vector3 direction,origin;
    public bool Charging=>birth>-99&&release<0;
    public PNStarchanterStrikeVisual(Transform parent)
    {
        root=new GameObject("Starchanter diagonal violet slash");root.layer=parent.gameObject.layer;root.transform.SetParent(parent,false);
        try
        {
            var uv=new Vector2[vertices.Length];var colors=new Color[vertices.Length];var triangles=new int[Segments*6];
            for(int i=0;i<=Segments;i++){uv[i*2]=new Vector2(i/(float)Segments,0);uv[i*2+1]=new Vector2(i/(float)Segments,1);colors[i*2]=colors[i*2+1]=Color.white;if(i<Segments){int a=i*2,k=i*6;triangles[k]=a;triangles[k+1]=a+1;triangles[k+2]=a+2;triangles[k+3]=a+1;triangles[k+4]=a+3;triangles[k+5]=a+2;}}
            for(int i=0;i<3;i++)
            {
                var m=new Material(Resources.Load<Shader>("PNStarweaverEffects")){name="Starchanter resonance slash "+i,hideFlags=HideFlags.HideAndDontSave};materials[i]=m;
                m.SetFloat("_Opacity",i==1?1.6f:1);m.SetFloat("_Mode",14);m.SetFloat("_DstBlend",i==2?1:10);m.SetFloat("_SlashLayer",i);m.SetColor("_Tint",new Color(.9f,.8f,1.1f));
                m.SetTexture("_MainTex",Resources.Load<Texture2D>("StarkeeperVfx/SlashMask"));m.SetTexture("_SlashDissolveTex",Resources.Load<Texture2D>("StarkeeperVfx/SlashDissolve"));m.SetTexture("_SlashFlowTex",Resources.Load<Texture2D>("StarkeeperVfx/SlashFlow"));m.SetTexture("_SlashGrainTex",Resources.Load<Texture2D>("StarkeeperVfx/SlashGrain"));m.renderQueue=3000+i;
                var mesh=new Mesh{name="Compact diagonal slash "+i,hideFlags=HideFlags.HideAndDontSave};meshes[i]=mesh;mesh.vertices=vertices;mesh.uv=uv;mesh.colors=colors;mesh.triangles=triangles;
                var go=new GameObject("Textured violet slash "+i);go.layer=root.layer;go.transform.SetParent(root.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var r=go.AddComponent<MeshRenderer>();renderers[i]=r;r.sharedMaterial=m;r.enabled=false;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
            }
        }
        catch{Dispose();throw;}
    }
    public void Begin(float seconds,Vector3 forward){birth=time;release=-100;windup=seconds;samples=0;lastSample=-100;Face(forward);}
    public void Face(Vector3 forward){forward.y=0;if(forward.sqrMagnitude>.0001f){distance=forward.magnitude;direction=forward.normalized;}}
    public void Release(Vector3 point){origin=point;release=time;}
    public void CancelCharge(){if(Charging){birth=-100;foreach(var r in renderers)r.enabled=false;}}
    public bool Moving=>Charging||release>=0&&time-release<.42f;
    // Grip is near the lower shaft. The complete fork moves through anticipation, impact and follow-through.
    public void Pose(Transform fork,Transform caster,Transform frame)
    {
        if(!Moving)return;
        float phase=Charging?Mathf.Clamp01((time-birth)/Mathf.Max(.01f,windup)):1;
        float swing=Mathf.SmoothStep(0,1,(phase-.42f)/.58f),after=release>=0?Mathf.Max(0,time-release):0;
        var restPosition=fork.position;var restRotation=fork.rotation;
        var back=caster.position-direction*.10f+Vector3.up*.36f;
        var contact=caster.position+direction*Mathf.Clamp(distance-.10f,.22f,1.1f)+Vector3.up*.18f;
        var grip=Vector3.Lerp(back,contact,swing);
        var axis=Vector3.Slerp((-direction*.65f+Vector3.up*.76f).normalized,(direction*.93f-Vector3.up*.36f).normalized,swing);
        if(release>=0){float follow=Mathf.SmoothStep(0,1,after/.10f);grip+=direction*(follow*.05f)-Vector3.up*(follow*.035f);axis=Vector3.Slerp(axis,(direction*.65f-Vector3.up*.76f).normalized,follow);}
        var rotation=Quaternion.FromToRotation(frame.up,axis)*frame.rotation;
        var position=grip-rotation*new Vector3(0,-.14f*fork.localScale.y,0);
        float enter=Charging?Mathf.SmoothStep(0,1,phase/.22f):1,recover=release>=0?Mathf.SmoothStep(0,1,(after-.14f)/.28f):0;
        fork.SetPositionAndRotation(Vector3.Lerp(Vector3.Lerp(restPosition,position,enter),restPosition,recover),Quaternion.Slerp(Quaternion.Slerp(restRotation,rotation,enter),restRotation,recover));
    }
    public void Advance(float dt){time+=Mathf.Max(0,dt);}
    public void Draw(Transform fork)
    {
        float age=time-release;bool swinging=Charging&&(time-birth)/Mathf.Max(.01f,windup)>.42f||release>=0&&age<.12f;
        if(swinging&&time!=lastSample)
        {
            lastSample=time;
            if(samples==tips.Length){for(int i=1;i<tips.Length;i++){tips[i-1]=tips[i];butts[i-1]=butts[i];}samples--;}
            tips[samples]=fork.TransformPoint(new Vector3(0,.175f,0));butts[samples]=fork.TransformPoint(new Vector3(0,-.205f,0));samples++;
        }
        bool on=samples>=2&&(Charging||release>=0&&age<.36f);
        root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
        for(int layer=0;layer<3;layer++)
        {
            renderers[layer].enabled=on;if(!on)continue;
            materials[layer].SetFloat("_SlashPhase",Charging?.10f:Mathf.Clamp01(age/.36f));
            for(int j=0;j<=Segments;j++)
            {
                float sample=j/(float)Segments*(samples-1);int k=Mathf.FloorToInt(sample),next=Mathf.Min(k+1,samples-1);float f=sample-k;
                vertices[j*2]=Vector3.Lerp(butts[k],butts[next],f);vertices[j*2+1]=Vector3.Lerp(tips[k],tips[next],f);
            }
            meshes[layer].vertices=vertices;meshes[layer].RecalculateBounds();
        }
    }
    public void Dispose(){if(!ReferenceEquals(root,null)){root.SetActive(false);DestroyOwned(root);}foreach(var m in materials)DestroyOwned(m);foreach(var mesh in meshes)DestroyOwned(mesh);}
    private static void DestroyOwned(UnityEngine.Object value){if(ReferenceEquals(value,null))return;if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
}
#endif
