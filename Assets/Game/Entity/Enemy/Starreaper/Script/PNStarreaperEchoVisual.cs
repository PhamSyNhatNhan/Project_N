#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using UnityEngine;
using UnityEngine.Rendering;

// A presentation-only puppet, with its own area clock. It never selects a
// target or launches a projectile. Atlas/actor mesh are borrowed, not copied.
public sealed class PNStarreaperEchoVisual : IDisposable
{
    public const float DissolveSeconds=.65f;
    private readonly GameObject root,copy;
    private readonly PNVisualActor actor;
    private readonly PNStarreaperPresentation bow;
    private readonly Material ink,weapon;
    private readonly MeshFilter filter;private readonly MeshRenderer renderer;
    private readonly PNStarreaperEnergyMesh dust;
    private MeshRenderer[] bowRenderers;
    private float age,delay,gap,shotAge=-10,tail;
    private bool running,dissolving,disposed;
    private Vector3 aim=Vector3.right;
    public PNVisualActor Actor=>actor;
    public PNStarreaperEchoVisual(Transform parent,EveRegistryHub hub,EveId appearance)
    {
        root=new GameObject("Starreaper echo");root.layer=parent.gameObject.layer;root.transform.SetParent(parent,false);
        try
        {
            actor=root.AddComponent<PNVisualActor>();actor.Bind(hub.Get<PNVisualAppearanceDefinition>().Get(appearance),hub,Vector3.zero);
            PNVisualSystem.Unregister(actor);actor.HideStrings();actor.SetRate(0);
            ink=Make("Starreaper echo ink",1);weapon=Make("Starreaper echo bow",2);
            copy=new GameObject("Echo atlas silhouette");copy.layer=root.layer;copy.transform.SetParent(root.transform,false);
            filter=copy.AddComponent<MeshFilter>();renderer=copy.AddComponent<MeshRenderer>();renderer.sharedMaterial=ink;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            dust=new PNStarreaperEnergyMesh(root.transform,"Echo dust");
            bow=root.AddComponent<PNStarreaperPresentation>();bow.BindEchoActor(actor);
        }
        catch{Dispose();throw;}
    }
    private static Material Make(string name,float mode)
    {
        var m=new Material(Resources.Load<Shader>("PNStarreaperPierce")){name=name,hideFlags=HideFlags.HideAndDontSave,renderQueue=3011};
        m.SetFloat("_EchoMode",mode);m.SetFloat("_DstBlend",10);m.SetTexture("_RainNoiseTex",Resources.Load<Texture2D>("StarreaperVfx/RainNoise"));return m;
    }
    public void Begin(bool left,float seconds,float interval)
    {
        age=tail=0;shotAge=-10;delay=seconds;gap=interval;running=true;dissolving=false;
        root.SetActive(true);actor.SetFacingLeft(left);actor.Play("pierce_charge",true);actor.SetPhase(0);
        aim=left?Vector3.left:Vector3.right;bow.BeginDraw(delay,aim,false);bow.Tick(0);
        bowRenderers=root.GetComponentsInChildren<MeshRenderer>();
        foreach(var r in bowRenderers)if(r!=actor.Renderer&&r!=renderer&&r.transform.parent!=null&&(r.name.Contains("armour")||r.name=="Starreaper violet bow"))r.sharedMaterial=weapon;
        PNViewCamera.Updated+=OnView;Advance(0,0);
    }
    public void Shot(Vector3 heading)
    {
        aim=heading;var camera=PNViewCamera.Current;
        actor.SetFacingLeft(Vector3.Dot(heading,camera!=null?camera.transform.right:Vector3.right)<0);
        bow.SetAim(heading);bow.SetAge(delay);bow.Tick(0);bow.Release();
        actor.Play("pierce_release",true);actor.SetPhase(0);shotAge=age;
    }
    public void Dissolve(){if(!running)return;dissolving=true;tail=0;bow.EndPose();}
    public void Advance(float seconds,float dt)
    {
        if(!running||disposed)return;age=seconds;if(dissolving)tail+=Mathf.Max(0,dt);
        float since=age-shotAge;
        if(shotAge<0){actor.SetPhase(Mathf.Clamp01(age/delay));bow.SetAge(age);}
        else if(since<gap){actor.SetPhase(Mathf.Clamp01(since/Mathf.Max(.01f,gap)));}
        actor.Tick(Mathf.Max(0,dt));bow.Tick(Mathf.Max(0,dt));OnView();
    }
    private void OnView(){Render(PNViewCamera.Current);}
    public void Render(Camera camera)
    {
        if(!running||disposed||camera==null)return;
        var facing=actor.GetComponent<PNCameraFacingTarget>();facing.ViewCamera=camera;facing.Align();bow.Tick(0);
        actor.Renderer.enabled=false;filter.sharedMesh=actor.Mesh;
        copy.transform.SetPositionAndRotation(actor.transform.position,actor.transform.rotation);copy.transform.localScale=Vector3.one;
        ink.SetTexture("_MainTex",actor.Renderer.sharedMaterial.GetTexture("_MainTex"));
        float dissolve=dissolving?Mathf.Clamp01(tail/DissolveSeconds):0;
        ink.SetFloat("_FxTime",age);ink.SetFloat("_EchoDissolve",dissolve);ink.SetFloat("_EchoHeight",Mathf.Max(.1f,actor.Mesh.bounds.max.y));
        weapon.SetFloat("_FxTime",age);weapon.SetFloat("_EchoDissolve",Mathf.SmoothStep(0,1,Mathf.InverseLerp(.65f,1,dissolve)));weapon.SetFloat("_EchoGlow",shotAge<0?Mathf.Clamp01(age/delay)*.35f:Mathf.Exp(-(age-shotAge)*18)*.65f);
        dust.Begin(age);
        var right=camera.transform.right;var up=camera.transform.up;var front=-camera.transform.forward*.004f;
        for(int i=0;i<14;i++)
        {
            float u=Mathf.Repeat(age*.5f+i*.618034f,1),a=i*2.39996f;
            float level=dissolving?dissolve*.48f:u*.45f;
            var at=root.transform.position+right*(Mathf.Sin(a)*(.11f+(dissolving?tail*.34f:0)))+Vector3.up*(level+u*.08f);
            float opacity=(dissolving?1-dissolve:.40f)*Mathf.Sin(u*Mathf.PI);
            if(i%4==0)dust.BlackStar(at,right,up,front,.025f,opacity,a);
            else dust.Quad(true,at+front,right,up,.009f,.009f,new Color(.45f,.14f,.9f,opacity*.50f),3);
        }
        dust.End();
        if(dissolve>=1)Stop();
    }
    public void Stop(){if(!running)return;running=false;PNViewCamera.Updated-=OnView;dust.Clear();root.SetActive(false);}
    public void Dispose()
    {
        if(disposed)return;Stop();disposed=true;bow?.Dispose();dust?.Dispose();
        Destroy(ink);Destroy(weapon);Destroy(root);
    }
    private static void Destroy(UnityEngine.Object o){if(o==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(o);else UnityEngine.Object.DestroyImmediate(o);}
}
#endif
