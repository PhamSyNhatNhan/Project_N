#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// The Action supplies age; LateUpdate only follows the current puppet/weapon pose.
[DefaultExecutionOrder(250)]
public sealed class PNStarweaverCastVisual:MonoBehaviour,IPNCameraFacing,IDisposable
{
    private readonly List<Material> materials=new List<Material>();
    private readonly List<Mesh> meshes=new List<Mesh>();
    private readonly Transform[] dust=new Transform[12];
    private readonly LineRenderer[] threads=new LineRenderer[4],shots=new LineRenderer[3];
    private readonly Vector3[][] paths={new Vector3[28],new Vector3[28],new Vector3[28],new Vector3[28]};
    private Transform source,eyeGlow,flash,haze;
    private PNStarweaverPresentation weapon;
    private PNVisualActor actor;
    private Material glowMaterial,starMaterial,flashMaterial,threadMaterial,hazeMaterial,shotMaterial;
    private Camera preview;
    private Vector3 direction;
    private float age,windup;
    private bool triangle,disposed;
    public float VisualAge=>age;
    public static PNStarweaverCastVisual Create(Transform source,PNStarweaverPresentation weapon,PNVisualActor actor,bool triangle,float windup,Vector3 direction,Camera preview=null)
    {
        var go=new GameObject(triangle?"Starweaver constellation cast":"Starweaver weave cast");SceneManager.MoveGameObjectToScene(go,source.gameObject.scene);
        var v=go.AddComponent<PNStarweaverCastVisual>();v.source=source;v.weapon=weapon;v.actor=actor;v.triangle=triangle;v.windup=windup;v.direction=direction.normalized;v.preview=preview;
        try{v.Build();v.SetTime(0);var identity=source.GetComponent<EveEngine.EveEntityIdentity>();if(identity!=null)PNWorldPresentationRegistration.Register(identity.World,go);return v;}catch{v.Dispose();throw;}
    }
    private Material Make(int mode,string texture,Color tint,bool additive)
    {
        var m=new Material(Resources.Load<Shader>("PNStarweaverEffects")){name="Starweaver cast "+mode,hideFlags=HideFlags.HideAndDontSave};materials.Add(m);
        m.SetFloat("_Mode",mode);m.SetFloat("_DstBlend",additive?1:10);m.SetColor("_Tint",tint);m.SetTexture("_NoiseTex",Resources.Load<Texture2D>("StarweaverVfx/Noise"));
        if(texture!=null)m.SetTexture("_MainTex",Resources.Load<Texture2D>("StarweaverVfx/"+texture));return m;
    }
    private Transform Quad(string name,Mesh mesh,Material mat)
    {
        var go=new GameObject(name);go.layer=source.gameObject.layer;go.transform.SetParent(transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=mat;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;return go.transform;
    }
    private LineRenderer Line(string name,Material mat,int count,float width)
    {
        var go=new GameObject(name);go.layer=source.gameObject.layer;go.transform.SetParent(transform,false);var r=go.AddComponent<LineRenderer>();r.sharedMaterial=mat;r.useWorldSpace=true;r.positionCount=count;r.startWidth=0;r.endWidth=width;return r;
    }
    private void Build()
    {
        var mesh=new Mesh{name="Cast quad",hideFlags=HideFlags.HideAndDontSave};meshes.Add(mesh);mesh.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)};mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};mesh.triangles=new[]{0,1,2,0,2,3};mesh.colors=new[]{Color.white,Color.white,Color.white,Color.white};mesh.RecalculateBounds();
        glowMaterial=Make(5,"Glow",new Color(.7f,.15f,1.2f),true);starMaterial=Make(1,null,new Color(1.3f,.9f,.25f),true);
        flashMaterial=Make(1,null,new Color(1.4f,.7f,1.7f),true);threadMaterial=Make(13,null,Color.white,false);
        hazeMaterial=Make(7,"Smoke",new Color(.5f,.18f,.7f),false);shotMaterial=Make(5,"Ribbon",new Color(.9f,.4f,1.3f),true);
        eyeGlow=Quad("Needle eye charge",mesh,glowMaterial);flash=Quad("Cast release star",mesh,flashMaterial);haze=Quad("Thin body haze",mesh,hazeMaterial);
        haze.GetComponent<MeshRenderer>().sortingOrder=-2;
        for(int i=0;i<12;i++)dust[i]=Quad("Gathering mote "+i,mesh,starMaterial);
        for(int i=0;i<4;i++)threads[i]=Line("Casting thread "+i,threadMaterial,28,i==0?.006f:.004f);
        for(int i=0;i<3;i++)shots[i]=Line("Release streak "+i,shotMaterial,2,.035f);
        if(Application.isPlaying)PNCameraFacingSystem.Register(this);
    }
    public void SetTime(float seconds)
    {
        if(disposed)return;age=seconds;weapon?.SetCastVisual(triangle,age,windup,direction);
        foreach(var m in materials)m.SetFloat("_FxTime",age);Align();
    }
    private void LateUpdate(){if(source==null||!source.gameObject.activeInHierarchy){Dispose();return;}Align();}
    public bool Align()
    {
        if(disposed||source==null)return false;
        var view=PNCameraFacing.ResolveCamera(preview,Application.isPlaying);if(view==null)return false;
        var forward=view.transform.forward;var right=view.transform.right;var up=view.transform.up;var rotation=Quaternion.LookRotation(forward,up);
        var frame=actor!=null?actor.transform:source;float side=actor!=null&&actor.FacingLeft?-1:1;
        var eye=weapon!=null?weapon.EyePosition:frame.TransformPoint(new Vector3(side*.25f,.28f,0));
        var tip=weapon!=null?weapon.TipPosition:eye+direction*.1f;
        var hat=frame.TransformPoint(new Vector3(side*.04f,.38f,0));if(actor!=null&&actor.TryGetSocketWorld("head_top",out var socket))hat=socket+up*.12f;
        float progress=age/windup,post=age-windup,charge=Mathf.Clamp01(progress);
        float response=0;if(triangle)for(int i=0;i<3;i++){float arrival=windup*(.86f+i*.04f);if(age>=arrival)response+=Mathf.Exp(-(age-arrival)*35);}
        float release=post>=0?Mathf.Clamp01(1-post/(triangle?.28f:.14f)):0;
        float fade=post<0?1:Mathf.Clamp01(1-post/.25f);
        glowMaterial.SetFloat("_Opacity",(.12f+charge*.6f+response*.6f)*fade);flashMaterial.SetFloat("_Opacity",release);
        eyeGlow.SetPositionAndRotation(eye-forward*.015f,rotation);eyeGlow.localScale=Vector3.one*(.065f+.085f*charge+response*.035f);
        flash.SetPositionAndRotation(tip-forward*.025f,rotation);flash.localScale=Vector3.one*(triangle?.16f:.13f)*(1+(1-release)*.5f);
        haze.gameObject.SetActive(triangle&&fade>0);haze.SetPositionAndRotation(frame.TransformPoint(new Vector3(-side*.04f,.13f,.03f))+forward*.03f,rotation);haze.localScale=new Vector3(.28f,.30f,1);hazeMaterial.SetFloat("_Opacity",charge*.22f*fade);
        starMaterial.SetFloat("_Opacity",fade*.8f);
        for(int i=0;i<12;i++)
        {
            float p=Mathf.Repeat(age*(triangle?1.8f:2.8f)+i/12f,1),a=i*2.399963f;
            float radius=(1-p)*(triangle?.17f:.12f);
            dust[i].gameObject.SetActive(post<0);dust[i].SetPositionAndRotation(eye+(right*Mathf.Cos(a)+up*Mathf.Sin(a))*radius,rotation);dust[i].localScale=Vector3.one*(.005f+(i%3)*.003f)*Mathf.Sin(p*Mathf.PI);
        }
        threadMaterial.SetFloat("_Opacity",(.35f+charge*.4f+response*.25f)*fade);
        for(int i=0;i<4;i++)
        {
            bool shown=fade>0&&(triangle||i<3);threads[i].gameObject.SetActive(shown);if(!shown)continue;
            Vector3 start=i==0&&triangle?hat:eye+(right*Mathf.Cos(i*2.1f)+up*Mathf.Sin(i*2.1f))*(triangle?.20f:.12f);
            Vector3 end=triangle&&i>0?eye+direction*(.2f+charge*.12f)+up*((i-2)*.045f):eye;
            for(int j=0;j<28;j++){float u=j/27f,bend=Mathf.Sin(u*Mathf.PI);paths[i][j]=Vector3.Lerp(start,end,u)+up*(Mathf.Sin(age*9-u*4+i)*bend*.018f+(i==0&&triangle?bend*.05f:0))+right*(bend*(i==0&&triangle?side*.10f:.018f*(i%2==0?1:-1)));}
            threads[i].SetPositions(paths[i]);
        }
        for(int i=0;i<3;i++)
        {
            shots[i].gameObject.SetActive(!triangle&&release>0);var d=Quaternion.AngleAxis((i-1)*9,Vector3.up)*direction;
            shots[i].SetPosition(0,tip);shots[i].SetPosition(1,tip+d*(.08f+(1-release)*.18f));shots[i].startColor=new Color(1,1,1,release);shots[i].endColor=new Color(1,1,1,0);
        }
        return true;
    }
    private void Release()
    {
        if(disposed)return;disposed=true;if(weapon!=null)weapon.EndCastVisual();PNCameraFacingSystem.Unregister(this);
        foreach(var m in materials)DestroyResource(m);foreach(var m in meshes)DestroyResource(m);materials.Clear();meshes.Clear();
    }
    public void Dispose(){if(disposed)return;Release();gameObject.SetActive(false);DestroyResource(gameObject);}
    private void OnDestroy(){Release();}
    private static void DestroyResource(UnityEngine.Object o){if(o==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(o);else UnityEngine.Object.DestroyImmediate(o);}
}
#endif
