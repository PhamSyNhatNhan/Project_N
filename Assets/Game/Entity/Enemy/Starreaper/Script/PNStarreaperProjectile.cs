#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;

// Directional rules remain in the base implementation. Only admitted hits emit a
// visual; repeated overlaps do not flash. Each projectile owns and resets its VFX.
public sealed class PNStarreaperProjectile : PNDirectionalProjectile
{
    private PNStarreaperArrowVisual visual;
    private readonly List<PNProjectileContact> single=new List<PNProjectileContact>(1);
    public override void OnPoolCreated(){base.OnPoolCreated();visual=PNStarreaperArrowVisual.Create(this);}
    protected override void Initialize(PNProjectileSpawn spawn){base.Initialize(spawn);visual?.Begin((float)Launch.ScaleFactor,HalfExtents,Launch.Input?["echoVisual"]?.Value<bool>()??false);}
    protected override void OnTargets(IReadOnlyList<PNProjectileContact> targets)
    {
        for(int i=0;i<targets.Count&&IsRunning;i++)
        {
            int before=HitCount;single.Clear();single.Add(targets[i]);base.OnTargets(single);
            if(HitCount>before)visual?.Contact(targets[i].Point,Forward,Age);
        }
        single.Clear();
    }
    protected override void Cleanup(){try{visual?.End();single.Clear();}finally{base.Cleanup();}}
    public override void DisposePooledResources(){visual?.Dispose();visual=null;base.DisposePooledResources();}
}

public sealed class PNStarreaperArrowVisual : MonoBehaviour
{
    private PNStarreaperEnergyMesh mesh;private PNStarreaperProjectile projectile;
    private Vector3 halfExtents;
    public Vector3 VisualHalfExtents=>halfExtents;
    private bool running,disposed,subscribed,simple;
    public bool Simplified=>simple;private float scale=1;private int next;
    private struct Hit { public bool Active;public Vector3 Point,Direction;public float Age; }
    private readonly Hit[] hits=new Hit[8];
    public static PNStarreaperArrowVisual Create(Component owner)
    {
        var go=new GameObject("Starreaper arrow visual");go.layer=PNLayers.Projectile;go.transform.SetParent(owner.transform,false);
        var v=go.AddComponent<PNStarreaperArrowVisual>();v.projectile=owner as PNStarreaperProjectile;
        try{v.mesh=new PNStarreaperEnergyMesh(go.transform,"Piercing starlight");return v;}catch{v.Dispose();throw;}

    }
    public void Begin(float size,Vector3 extents,bool echo=false)
    {
        if(disposed)return;simple=echo;scale=size;halfExtents=extents;running=true;next=0;System.Array.Clear(hits,0,hits.Length);
        if(!subscribed){PNViewCamera.Updated+=OnView;subscribed=true;}OnView();
    }
    public void End(){running=false;if(subscribed){PNViewCamera.Updated-=OnView;subscribed=false;}mesh?.Clear();System.Array.Clear(hits,0,hits.Length);}
    private void OnView(){if(running&&projectile!=null)Render(projectile.Position,projectile.Forward,projectile.Age,PNViewCamera.Current);}
    public void Contact(Vector3 point,Vector3 direction,float age){hits[next]=new Hit{Active=true,Point=point,Direction=direction,Age=age};next=(next+1)%hits.Length;}
    public void Render(Vector3 position,Vector3 heading,float age,Camera camera)
    {
        if(!running||disposed||camera==null)return;
        var x=heading.normalized;var y=Vector3.Cross(camera.transform.forward,x).normalized;if(y.sqrMagnitude<.01f)y=camera.transform.up;
        var z=-camera.transform.forward*.004f;mesh.Begin(age);
        var bodySide=Vector3.Cross(Vector3.up,x).normalized;
        float coverWidth=2*(Mathf.Abs(Vector3.Dot(y,bodySide))*halfExtents.x+Mathf.Abs(Vector3.Dot(y,Vector3.up))*halfExtents.y);
        if(simple)
        {
            float echoWake=Mathf.Min(.26f,Mathf.Max(.04f,age*2))*scale;
            mesh.Quad(false,position+z,x,y,.36f*scale,coverWidth*.68f,new Color(.008f,.002f,.020f,.82f),9);
            mesh.Quad(false,position-x*(echoWake*.5f+.09f*scale)+z,x,y,echoWake,coverWidth*.65f,new Color(.012f,.003f,.026f,.72f),0);
            mesh.Quad(true,position-x*(echoWake*.5f+.09f*scale)+z*2,x,y,echoWake,coverWidth*.48f,new Color(.44f,.13f,.85f,.14f),0);
            mesh.Quad(true,position+z*3,x,y,.30f*scale,.035f*scale,new Color(.95f,.60f,1.35f,.85f),5);
            for(int i=0;i<hits.Length;i++)
            {
                var h=hits[i];float t=(age-h.Age)/.14f;if(!h.Active||t<0||t>=1)continue;
                mesh.Quad(true,h.Point+z*4,x,y,.13f*scale,.025f*scale,new Color(1.1f,.65f,1.5f,(1-t)*(1-t)),1);
            }
            mesh.End();return;
        }
        float artScale=scale*1.15f;coverWidth*=1.15f;
        // Restore the soft violet/black wake, broad enough to read alongside the
        // current volume. It joins inside the body, without a separate head effect.
        float wake=Mathf.Min(.82f,Mathf.Max(.08f,age*2))*artScale;
        var tail=position-x*(wake*.5f+.10f*artScale);
        mesh.Quad(false,tail+z,x,y,wake,coverWidth*2.43f,new Color(.012f,.003f,.026f,.94f),0);
        mesh.Quad(true,tail+z*2,x,y,wake,coverWidth*2.0925f,new Color(.40f,.06f,.90f,.28f),0);
        mesh.Quad(true,tail+z*3,x,y,wake,coverWidth*1.1475f,new Color(.48f,.09f,1.0f,.08f),6);
        // Enlarge only the normal shot picture; keep physical extents and echo unchanged.
        mesh.Body(position,x,y*1.25f,z,artScale,coverWidth*1.08f,age,1,1.3f);
        for(int i=0;i<12;i++)
        {
            float u=Mathf.Repeat(age*.9f+i*.618034f,1);var p=position-x*(.13f+u*wake)+y*(Mathf.Sin(i*2.4f+age)*.055f*u*artScale)+z*3;
            float size=(.005f+i%3*.003f)*artScale;mesh.Quad(true,p,x,y,size,size,new Color(.85f,.5f,1.5f,(1-u)*.65f),3);
        }
        for(int i=0;i<3;i++)
        {
            float u=Mathf.Repeat(age*.45f+i*.31f,1);
            var at=position-x*(.13f+u*wake)+y*(Mathf.Sin(i*2.4f+age)*.065f*artScale);
            mesh.BlackStar(at,x,y,z,(.025f+i*.004f)*artScale,(1-u)*.8f,i*.8f+age*.2f);
        }
        for(int i=0;i<hits.Length;i++)
        {
            var h=hits[i];float t=(age-h.Age)/.23f;if(!h.Active||t<0||t>=1)continue;var hx=h.Direction.normalized;var hy=Vector3.Cross(camera.transform.forward,hx).normalized;
            float fade=(1-t)*(1-t);var at=h.Point+z*6;
            mesh.Quad(true,at,hx,hy,(.16f+t*.20f)*artScale,.055f*artScale,new Color(2.3f,1.1f,2.8f,fade),1);
            mesh.Quad(false,at,hx,hy,.24f*artScale,(.10f+t*.12f)*artScale,new Color(.17f,.015f,.27f,fade*.7f),0);
            for(int j=0;j<6;j++)
            {
                float side=j%2==0?1:-1;var p=at+hy*(side*(.018f+t*(.07f+j*.012f))*artScale)+hx*((j/2-1)*t*.05f*artScale);
                mesh.Quad(true,p,hx,hy,.012f*artScale,.012f*artScale,new Color(.9f,.4f,1.8f,fade),3);
            }
        }
        mesh.End();
    }
    public void Dispose(){if(disposed)return;End();disposed=true;mesh?.Dispose();mesh=null;}
    private void OnDisable(){if(!disposed)End();}
    private void OnDestroy(){Dispose();}
}
#endif
