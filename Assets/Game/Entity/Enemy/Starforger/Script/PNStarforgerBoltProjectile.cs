#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

// All movement, prepared damage and admission rules remain in the directional implementation.
public sealed class PNStarforgerBoltProjectile : PNDirectionalProjectile
{
    private PNStarforgerBoltVisual visual;private bool wallContact;private Vector3 wallPoint;
    private readonly List<PNProjectileContact> single=new List<PNProjectileContact>(1);
    public override void OnPoolCreated(){base.OnPoolCreated();visual=PNStarforgerBoltVisual.Create(this);}
    protected override void Initialize(PNProjectileSpawn spawn){base.Initialize(spawn);wallContact=false;visual?.Begin((float)Launch.ScaleFactor);}
    protected override void OnTargets(IReadOnlyList<PNProjectileContact> targets)
    {
        for(int i=0;i<targets.Count&&IsRunning;i++)
        {
            var scene=gameObject.scene;var world=World;float size=(float)Launch.ScaleFactor;int before=HitCount;
            single.Clear();single.Add(targets[i]);base.OnTargets(single);
            if(HitCount>before)PNStarforgerBoltImpact.Create(scene,world,targets[i].Point,size);
        }
        single.Clear();
    }
    protected override void OnContacts(PNProjectileContacts contacts)
    {
        if(contacts.HasEnvironment){wallContact=true;wallPoint=contacts.Environment.Point;}
        base.OnContacts(contacts);
    }
    protected override void OnEnd(PNProjectileEndReason reason)
    {
        // End is queued by the manager; the reason becomes authoritative here,
        // before pool return clears Launch/World/EndReason.
        if(reason==PNProjectileEndReason.Environment&&wallContact)PNStarforgerBoltImpact.Create(gameObject.scene,World,wallPoint,(float)Launch.ScaleFactor);
        base.OnEnd(reason);
    }
    protected override void Cleanup(){try{visual?.End();single.Clear();}finally{base.Cleanup();}}
    public override void DisposePooledResources(){visual?.Dispose();visual=null;base.DisposePooledResources();}
}

// A small white-gold molten seed, violet sheath and textured short dust wake.
public sealed class PNStarforgerBoltVisual : MonoBehaviour
{
    private PNStarreaperEnergyMesh mesh;private PNProjectileInstance owner;private bool running,disposed;private float scale=1;
    public static PNStarforgerBoltVisual Create(Component owner)
    {
        var go=new GameObject("Starforger bolt visual");go.layer=PNLayers.Projectile;go.transform.SetParent(owner.transform,false);
        var v=go.AddComponent<PNStarforgerBoltVisual>();v.owner=owner as PNProjectileInstance;
        try{v.mesh=new PNStarreaperEnergyMesh(go.transform,"Starforger bolt");return v;}catch{v.Dispose();throw;}
    }
    public void Begin(float size=1){if(disposed)return;End();scale=size;running=true;PNViewCamera.Updated+=OnView;OnView();}
    private void OnView(){if(owner!=null&&owner.IsRunning)Render(owner.Position,owner.Forward,owner.Age,PNViewCamera.Current);}
    public void Render(Vector3 position,Vector3 heading,float age,Camera camera)
    {
        if(!running||disposed||camera==null)return;
        var x=heading.normalized;var y=Vector3.Cross(camera.transform.forward,x).normalized;if(y.sqrMagnitude<.001f)y=camera.transform.up;
        var front=-camera.transform.forward*.004f;float wake=Mathf.Min(.30f,age*4);
        mesh.Begin(age);
        mesh.Quad(false,position+front,x,y,.345f*scale,.21f*scale,new Color(.035f,.006f,.06f,.9f),9);
        mesh.Quad(true,position+front*2,x,y,.345f*scale,.21f*scale,new Color(.7f,.12f,1.2f,.45f),6);
        mesh.Quad(true,position+front*3,x,y,.208f*scale,.0845f*scale,new Color(2.2f,1.3f,.32f,1),5);
        mesh.Quad(true,position+front*4,x,y,.13f*scale,.0585f*scale,new Color(2.5f,2.1f,1.15f,1),5);
        var tail=position-x*(wake*.5f+.055f)*scale;
        mesh.Quad(false,tail,x,y,wake*scale,.18f*scale,new Color(.035f,.008f,.06f,.65f),0);
        mesh.Quad(true,tail+front,x,y,wake*scale,.165f*scale,new Color(1.45f,1.22f,.72f,.65f),6);
        for(int i=0;i<6;i++)
        {
            float u=Mathf.Repeat(age*3+i*.618034f,1),a=i*2.39996f;var at=position-x*(.04f+u*.30f)*scale+y*Mathf.Sin(a)*.035f*scale;
            float fade=(1-u)*Mathf.Clamp01(age*15);
            mesh.Quad(true,at+front*3,camera.transform.right,camera.transform.up,.014f*scale,.014f*scale,new Color(1.8f,1.05f,.3f,fade),3);
        }
        mesh.End();
    }
    public void End(){if(!running)return;running=false;PNViewCamera.Updated-=OnView;mesh?.Clear();}
    public void Dispose(){if(disposed)return;End();disposed=true;mesh?.Dispose();mesh=null;}
    private void OnDisable(){End();}
    private void OnDestroy(){Dispose();}
}

// Detached from a returned projectile; world-clock retirement survives shooter/projectile cleanup.
public sealed class PNStarforgerBoltImpact : MonoBehaviour
{
    public const float Duration=.28f;
    private PNStarreaperEnergyMesh mesh;private EveWorldContext world;private double started;private Vector3 point;private float scale;private bool disposed;
    public static PNStarforgerBoltImpact Create(Scene scene,EveWorldContext world,Vector3 at,float size=1)
    {
        var go=new GameObject("Starforger bolt contact");go.layer=PNLayers.Projectile;go.transform.position=at;SceneManager.MoveGameObjectToScene(go,scene);
        var v=go.AddComponent<PNStarforgerBoltImpact>();v.world=world;v.started=world?.GameplayTime??Time.timeAsDouble;v.point=at;v.scale=size;
        try{v.mesh=new PNStarreaperEnergyMesh(go.transform,"Starforger contact");if(world!=null)PNWorldPresentationRegistration.Register(world,go);PNViewCamera.Updated+=v.OnView;v.OnView();return v;}catch{v.Dispose();throw;}
    }
    private void LateUpdate()
    {
        if(disposed)return;
        if(world!=null&&(!world.Session.IsActive||world.State==EveWorldState.Unloading||world.State==EveWorldState.Disposed||world.State==EveWorldState.Failed)){Dispose();return;}
        if(CurrentAge>=Duration)Dispose();
    }
    private float CurrentAge=>(float)((world!=null?world.GameplayTime:Time.timeAsDouble)-started);
    private void OnView(){Render(CurrentAge,PNViewCamera.Current);}
    public void Render(float age,Camera camera)
    {
        if(disposed||camera==null)return;mesh.Begin(age);float fade=1-Mathf.Clamp01(age/Duration);var right=camera.transform.right;var up=camera.transform.up;var front=-camera.transform.forward*.004f;
        if(fade>0)
        {
            mesh.Quad(true,point+front,right,up,.30f*scale,.23f*scale,new Color(2.3f,1.6f,.7f,Mathf.Exp(-age*40)),2);
            mesh.Quad(true,point+front*2,right,up,.27f*scale,.23f*scale,new Color(.8f,.16f,1.3f,fade*.65f),21,frame:age/Duration*3);
            for(int i=0;i<9;i++)
            {
                float a=i*2.39996f;var radial=right*Mathf.Cos(a)+up*Mathf.Sin(a);var at=point+radial*(age*(.5f+i%3*.18f))*scale;
                mesh.Quad(true,at+front*2,radial,Vector3.Cross(camera.transform.forward,radial),.04f*fade*scale,.018f*scale,new Color(2.0f,1.25f,.36f,fade),1);
                if(i%3==0)mesh.Quad(true,at+front*3,right,up,.035f*scale*fade,.035f*scale*fade,new Color(2,1.4f,.45f,fade),3);
            }
        }
        mesh.End();
    }
    private void Release(){if(disposed)return;disposed=true;PNViewCamera.Updated-=OnView;mesh?.Dispose();}
    public void Dispose(){if(disposed)return;Release();if(Application.isPlaying)Destroy(gameObject);else DestroyImmediate(gameObject);}
    private void OnDestroy(){Release();}
}
#endif
