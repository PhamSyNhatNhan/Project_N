#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The Starforger's golden hammer. Presentation only: it hovers beside the puppet on the actor's own clock (a pause or time stop freezes it); while a cast runs the Action drives it
// (BeginSlam / BeginStar, SetAge, Slam, EndPose) so the hammer cocks back through the crouch, rises over the head while the body is airborne and swings down at the landing, then rests.
// A rigid chamfered gold hammer uses the project's normal-lit weapon shader; this component never decides gameplay.
[DefaultExecutionOrder(200)]
public sealed class PNStarforgerPresentation : MonoBehaviour
{
    private enum Pose { None, Slam, Star }
    [SerializeField, Min(.1f)] private float hoverPeriod = 3f;
    private PNVisualBody body; private PNVisualActor owner;
    private PNStarforgerFlightVisual flight;private PNStarforgerForgeVisual forge;
    private Transform hammer; private Mesh mesh; private Material material;
    private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
    private Pose pose; private float phase, age, crouch, air, recovery, swing, release, pulse,landPoseAt;
    // The hammer head's centre in world space (the slam's impact point on the hammer).
    private static Vector3 ForgeLocal(float side)=>new Vector3(side*.36f,.11f,-.05f);
    public Vector3 ForgePosition => body!=null&&body.Actor!=null?body.Actor.transform.TransformPoint(ForgeLocal(body.Actor.FacingLeft?-1:1)):HeadPosition;
    public Vector3 ForgeStrikePoint => hammer!=null?hammer.TransformPoint(new Vector3((body!=null&&body.Actor!=null&&body.Actor.FacingLeft?-1:1)*PNStarforgerHammerGeometry.StrikeFaceX,PNStarforgerHammerGeometry.HeadY,0)):ForgePosition;
    public Vector3 HeadPosition => hammer != null ? hammer.TransformPoint(new Vector3(0, PNStarforgerHammerGeometry.HeadY, 0)) : transform.position + Vector3.up * .3f;

    public void BeginSlam(float crouchSeconds, float airSeconds, float recoverySeconds) { forge?.Clear();flight?.Clear(); pose = Pose.Slam; age = 0; swing = 0; crouch = crouchSeconds; air = airSeconds; recovery = recoverySeconds; if (isActiveAndEnabled) Tick(0); }
    public void BeginStar(float castSeconds,float recoverySeconds) { flight?.Clear();forge?.Clear();pose = Pose.Star; age = 0; crouch = castSeconds; recovery=recoverySeconds; pulse = 0; if (isActiveAndEnabled) Tick(0); }
    public void SetAge(float seconds) { if (pose != Pose.None) age = seconds; }
    public void Slam() { if (pose == Pose.Slam) {swing = .0001f;landPoseAt=age;} }
    public void EndPose() { forge?.Clear();flight?.Clear(); if (pose != Pose.None) release = 1f; pose = Pose.None; swing = 0; if (isActiveAndEnabled) Tick(0); }

    private void Awake() { body = GetComponent<PNVisualBody>(); }
    private void LateUpdate()
    {
        if (body == null) body = GetComponent<PNVisualBody>();
        if (body != null && body.Actor != null) Tick(body.Actor.ClockDelta());
    }
    private void OnDisable() { forge?.Clear();flight?.Clear(); owner = null; phase = 0f; pose = Pose.None; release = 0f; swing = 0f; if (hammer != null) hammer.gameObject.SetActive(false); }
    private void OnDestroy() { DisposeOwned(); }

    private void Ensure()
    {
        if (hammer != null) { if (!hammer.gameObject.activeSelf) hammer.gameObject.SetActive(true); return; }
        var shader = Resources.Load<Shader>("PNStarreaperWeapon"); if (shader == null) throw new InvalidOperationException("Missing PNStarreaperWeapon shader.");
        material = new Material(shader) { name = "Starforger golden hammer", hideFlags = HideFlags.HideAndDontSave }; owned.Add(material);
        material.SetColor("_Tint",Color.white);material.SetFloat("_Specular",.12f);
        mesh=PNStarforgerHammerGeometry.Build();owned.Add(mesh);
        var go = new GameObject("Starforger golden hammer"); go.layer = gameObject.layer; go.transform.SetParent(transform, false); hammer = go.transform;
        go.AddComponent<MeshFilter>().sharedMesh = mesh; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
    }

    public void Tick(float dt)
    {
        if (body == null) body = GetComponent<PNVisualBody>();
        var actor = body != null ? body.Actor : null; if (actor == null) return;
        Ensure();
        if (owner != actor) { owner = actor; phase = 0f; }
        dt = Mathf.Max(0f, dt);
        phase = Mathf.Repeat(phase + dt * Mathf.PI * 2f / Mathf.Max(.1f, hoverPeriod), Mathf.PI * 2f);
        release = Mathf.Max(0f, release - dt * 4f); if (swing > 0f) swing += dt;
        float side = actor.FacingLeft ? -1 : 1, lift = Mathf.Sin(phase);
        var position = actor.transform.TransformPoint(new Vector3(side * .24f, .16f + .008f * lift, -.02f)); float angle = side * (-12f+1.2f*Mathf.Sin(phase*.73f));     // resting: hangs beside the body, head up
        if (pose == Pose.Slam)
        {
            float cock=Mathf.SmoothStep(0,1,Mathf.Clamp01(age/Mathf.Max(.01f,crouch)));
            float rise=Mathf.Clamp01((age-crouch)/Mathf.Max(.01f,air));
            float drop=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.72f,.99f,rise));
            if(swing>0)drop=1;
            float settle=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.20f,Mathf.Max(.21f,recovery),age-(swing>0?landPoseAt:crouch+air)));
            var rest=position;float restAngle=angle;
            angle=Mathf.Lerp(restAngle,side*70,cock);angle=Mathf.Lerp(angle,side*35,Mathf.SmoothStep(0,1,rise));angle=Mathf.Lerp(angle,side*-92,drop);
            var over=actor.transform.TransformPoint(new Vector3(side*.05f,.55f,-.045f));
            var down=actor.transform.TransformPoint(new Vector3(side*.10f,.15f,-.045f));
            position=Vector3.Lerp(position,over,Mathf.Max(cock*.55f,Mathf.SmoothStep(0,1,rise)));position=Vector3.Lerp(position,down,drop);
            position=Vector3.Lerp(position,rest,settle);angle=Mathf.Lerp(angle,restAngle,settle);

        }
        else if (pose == Pose.Star)
        {
            float q=Mathf.Clamp01(age/Mathf.Max(.01f,crouch));
            float raise=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.58f,q));
            float strike=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.60f,1,q));
            // Swing around a stable grip and land the OUTER end cap on the
            // blank. The collar/handle under the head never pass through it.
            var rest=position;float restAngle=angle;
            var contactRotation=actor.transform.rotation*Quaternion.Euler(0,0,-side*90);
            var face=new Vector3(side*PNStarforgerHammerGeometry.StrikeFaceX,PNStarforgerHammerGeometry.HeadY,0);
            var grip=ForgePosition-contactRotation*face;
            position=Vector3.Lerp(rest,grip,raise);angle=Mathf.Lerp(restAngle,side*32,raise);
            angle=Mathf.Lerp(angle,-side*90,strike);
            float hold=Mathf.Min(.05f,recovery*.25f);
            float settle=Mathf.SmoothStep(0,1,Mathf.InverseLerp(hold,Mathf.Max(hold+.001f,recovery),age-crouch));
            position=Vector3.Lerp(position,rest,settle);angle=Mathf.Lerp(angle,restAngle,settle);
            if(forge==null)forge=new PNStarforgerForgeVisual(transform);forge.Advance(actor,age,crouch,ForgeLocal(side));
        }
        else if (release > 0f) { /* drifting back to rest: the rest pose above is already the target, the blend is the position lerp below */ }
        if(pose==Pose.Slam && age>=crouch && (swing<=0 || age-landPoseAt<.20f))
        {
            if(flight==null)flight=new PNStarforgerFlightVisual(transform);
            flight.Advance(actor,age,swing>0?landPoseAt:-1);
        }
        else flight?.Clear();
        hammer.position = position; hammer.rotation = actor.transform.rotation * Quaternion.Euler(0, 0, angle);
    }

    public void RenderFlight(Camera camera){flight?.Render(camera);forge?.Render(camera);}
    public void Dispose(){DisposeOwned();}
    private void DisposeOwned()
    {
        forge?.Dispose();forge=null;flight?.Dispose();flight=null;
        foreach (var o in owned) if (o != null) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }
        owned.Clear(); mesh = null; material = null;
        if (hammer != null) { if (Application.isPlaying) Destroy(hammer.gameObject); else DestroyImmediate(hammer.gameObject); }
        hammer = null;
    }
}

// One cast owns this warning/impact. All animation uses the Action's explicit age.
public sealed class PNStarforgerTelegraph : IDisposable
{
    private readonly GameObject root;private readonly PNStarreaperEnergyMesh fx;
    private readonly Vector3 centre;private readonly float radius,total;
    private bool disposed,impacted;private float age,impactAt;private IPNCameraHandle impactShake;
    public static readonly PNShakeSpec LandingShake=new PNShakeSpec(.065f,28,.18f,.15f,0,PNShakeShape.Recoil,.35f,1,0,6,PNShakeFalloff.Quadratic);
    public static PNStarforgerTelegraph Create(UnityEngine.SceneManagement.Scene scene,Vector3 centre,float radius,float seconds)=>new PNStarforgerTelegraph(scene,centre,radius,seconds);
    private PNStarforgerTelegraph(UnityEngine.SceneManagement.Scene scene,Vector3 at,float r,float seconds)
    {
        centre=at;radius=r;total=Mathf.Max(.01f,seconds);root=new GameObject("Starforger slam warning");root.transform.position=at;UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
        fx=new PNStarreaperEnergyMesh(root.transform,"Starforger slam");PNViewCamera.Updated+=OnView;SetTime(0);
    }
    private void OnView(){Render(PNViewCamera.Current);}
    public void SetTime(float seconds){if(disposed)return;age=seconds;OnView();}
    public void Impact()
    {
        if(disposed||impacted)return;impacted=true;impactAt=age;
        if(Application.isPlaying)impactShake=PNCameraServices.Current.RequestShake(root,"starforger.slam",LandingShake);
        OnView();
    }
    public void Render(Camera camera)
    {
        if(disposed||camera==null)return;fx.Begin(age);
        var ground=centre+Vector3.up*.025f;var right=camera.transform.right;var up=camera.transform.up;var front=-camera.transform.forward*.004f;
        if(!impacted)
        {
            float p=Mathf.Clamp01(age/total);
            fx.Quad(true,ground,Vector3.right,Vector3.forward,radius*2/.82f,radius*2/.82f,new Color(1.15f,.68f,.18f,.48f+p*.25f),16);
            fx.Quad(true,ground+Vector3.up*.003f,Vector3.right,Vector3.forward,radius*1.7f*p,radius*1.7f*p,new Color(.75f,.35f,.08f,.12f+p*.12f),17);
        }
        else
        {
            float t=Mathf.Max(0,age-impactAt),q=t/.65f,fade=1-Mathf.SmoothStep(0,1,q);
            if(q<1)
            {
                float grow=1-Mathf.Exp(-t*24),blast=1-Mathf.SmoothStep(0,1,Mathf.Clamp01(t/.20f));
                fx.Quad(false,ground,Vector3.right,Vector3.forward,radius*2,radius*2,new Color(.022f,.006f,.04f,fade*.9f),17);
                // White-gold contact lasts only a few frames. The body then opens
                // horizontally into a torn violet shell, rather than rising flames.
                fx.Quad(true,centre+Vector3.up*.10f+front*3,right,up,1.35f,.78f,new Color(4.2f,3.5f,2.3f,Mathf.Exp(-Mathf.Max(0,t-.035f)*42)),2);
                fx.Quad(true,centre+Vector3.up*.10f+front*4,right,up,1.25f,.08f,new Color(3.1f,2.1f,.85f,Mathf.Exp(-t*30)),3);
                if(t<.25f)
                {
                    float a=Mathf.Clamp01(t/.25f),w=.25f+grow*1.35f,h=.18f+grow*.75f;
                    var at=centre+Vector3.up*(h*.35f);
                    fx.Quad(false,at,right,up,w,h,new Color(.028f,.005f,.055f,(1-a)*.9f),22,frame:a*3);
                    fx.Quad(true,at+front*2,right,up,w*1.06f,h*1.05f,new Color(1.6f,.34f,2.7f,(1-a)*1.3f),21,frame:a*3);
                    // Short radial eruption lobes break the shell's outline.
                    for(int i=0;i<6;i++)
                    {
                        float a0=(i+.35f)*Mathf.PI*2/6;var outwards=(right*Mathf.Cos(a0)+up*Mathf.Sin(a0)*.5f).normalized;
                        var side=Vector3.Cross(camera.transform.forward,outwards).normalized;
                        var lobe=centre+Vector3.up*.15f+outwards*(.12f+grow*.32f);
                        fx.Quad(true,lobe+front,outwards,side,.32f+grow*.32f,.38f,new Color(1.05f,.21f,1.95f,blast*.8f),20,frame:4+a*3);
                    }
                }
                // The low shock and dust travel outward first, then curl up.
                float shock=Mathf.Clamp01(t/.18f);
                fx.Quad(true,ground+Vector3.up*.008f,Vector3.right,Vector3.forward,radius*2*shock/.82f,radius*2*shock/.82f,new Color(1.5f,.55f,2.1f,(1-shock)*1.1f),16);
                for(int i=0;i<5;i++)
                {
                    float a=i*2.39996f;var radial=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                    var at=centre+radial*radius*(.08f+grow*(.38f+i%3*.18f))+Vector3.up*(.04f+Mathf.Max(0,t-.10f)*.45f);
                    float size=.45f+grow*(.25f+i%3*.08f);float theta=i*1.7f;var sx=right*Mathf.Cos(theta)+up*Mathf.Sin(theta);var sy=up*Mathf.Cos(theta)-right*Mathf.Sin(theta);
                    fx.Quad(false,at,sx,sy,size,size*.80f,new Color(.075f,.025f,.11f,fade*.82f),23,frame:Mathf.Min(11,q*10+i%3));
                    fx.Quad(true,at+front,sx,sy,size,size*.80f,new Color(.80f,.18f,1.25f,fade*.12f),24,frame:Mathf.Min(11,q*10+i%3));
                }
                for(int i=0;i<30;i++)
                {
                    float a=i*2.39996f;var radial=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                    float spread=radius*(.02f+(.70f+i%4*.13f)*(1-Mathf.Exp(-t*16)));
                    float h=Mathf.Max(0,(1.5f+i%4*.42f)*t-5.0f*t*t);
                    var at=centre+radial*spread+Vector3.up*(.04f+h);var along=(radial+Vector3.up*.35f).normalized;var across=Vector3.Cross(camera.transform.forward,along).normalized;
                    fx.Quad(true,at+front*2,along,across,(.13f+i%3*.05f)*fade,.04f,new Color(2.0f,1.25f,.40f,fade),1);
                    if(i%2==0)
                    {
                        float tilt=a+t*(i%3-1)*5,c=Mathf.Cos(tilt),sn=Mathf.Sin(tilt);var sx=right*c+up*sn;var sy=up*c-right*sn;
                        float size=(.07f+i%4*.022f)*fade;
                        fx.Quad(false,at+front*3,sx,sy,size,size,new Color(.16f,.06f,.02f,fade*.95f),14);
                        fx.Quad(true,at+front*4,sx,sy,size*1.15f,size*1.15f,new Color(2.2f,1.45f,.45f,fade),15);
                        fx.Quad(true,at+front*5,right,up,size*.9f,size*.9f,new Color(1.8f,1.15f,.4f,fade*.75f),3);
                    }
                }
            }
        }
        fx.End();
    }
    public void Dispose(){if(disposed)return;disposed=true;impactShake?.Dispose();impactShake=null;PNViewCamera.Updated-=OnView;fx.Dispose();if(root!=null){if(Application.isPlaying)UnityEngine.Object.Destroy(root);else UnityEngine.Object.DestroyImmediate(root);}}
}
#endif
