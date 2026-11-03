#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The Starreaper's selected concept-D bow. Presentation only: body remains rigid; actor clock drives the energy string. Aiming Actions drive it
// (BeginDraw, SetAim, SetAge, Release, EndPose) so the bow turns toward the target, the string is drawn back and an arrow is nocked. It never decides gameplay.
// Silhouette follows the user-approved D sheet; the upper inner shoulder anchors the string, not the ornamental tip.
[DefaultExecutionOrder(200)]
public sealed class PNStarreaperPresentation : MonoBehaviour, IDisposable
{
    private static readonly Color Violet=new Color(.62f,.30f,1.05f),Pale=new Color(1.1f,.85f,1.4f);
    [SerializeField,Min(.05f)] private float bowHeight=.34f,belly=.07f;
    private PNVisualBody body;private PNVisualActor owner,echoActor;
    private float idleClock,idleWeight=1;
    private Vector3 retreatOrigin;private float retreatAge=10,retreatDuration;
    public void BindEchoActor(PNVisualActor actor){echoActor=actor;}
    public void BeginRetreat(float duration){retreatOrigin=transform.position;retreatAge=0;retreatDuration=Mathf.Max(.01f,duration);}
    private GameObject retreatCoat;private MeshFilter retreatCoatFilter;private Material retreatCoatMaterial;
    private Transform bow;private LineRenderer bowString,stringHalo,nock;
    private PNStarreaperEnergyMesh pierceFx;private bool pierce,rain;private float fxAge;
    private Mesh mesh;private Material bowMaterial,lineMaterial;
    private readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
    private readonly Vector3[] stringPoints=new Vector3[3],nockPoints=new Vector3[2];
    private Vector3 muzzlePosition,projectilePosition;
    private float age,windup,release;private bool drawing,fired;private Vector3 aim=Vector3.forward;
    public bool Drawing=>drawing;
    public Vector3 MuzzlePosition=>bow!=null?muzzlePosition:transform.position+Vector3.up*.2f;
    public Vector3 ProjectilePosition=>bow!=null?projectilePosition:transform.position+Vector3.up*.2f;
    public Vector3 SkyDirection=>bow!=null?bow.TransformVector(Vector3.right).normalized:Vector3.up;
    public void BeginDraw(float seconds,Vector3 direction,bool pierceCharge=false,bool skyCharge=false){pierce=pierceCharge;rain=skyCharge;fxAge=0;drawing=true;fired=false;release=0;age=0;windup=Mathf.Max(.05f,seconds);aim=direction;if(isActiveAndEnabled)Tick(0);}
    public void SetAim(Vector3 direction){if(drawing&&direction.sqrMagnitude>1e-8f)aim=direction;}
    public void SetAge(float seconds){if(drawing)age=seconds;}
    public void Release(){fired=true;release=1;}
    public void EndPose(){if(drawing)release=Mathf.Max(release,.6f);drawing=false;if(isActiveAndEnabled)Tick(0);}
    private void Awake(){body=GetComponent<PNVisualBody>();}
    private void LateUpdate(){if(body==null)body=GetComponent<PNVisualBody>();if(echoActor==null&&body!=null&&body.Actor!=null)Tick(body.Actor.ClockDelta());}
    private void OnDisable(){if(retreatCoat!=null)retreatCoat.SetActive(false);retreatAge=10;owner=null;drawing=false;release=0;if(bowMaterial!=null)bowMaterial.SetFloat("_ChargeGlow",0);pierceFx?.Clear();if(bow!=null)bow.gameObject.SetActive(false);}
    private void OnDestroy(){DisposeOwned();}
    private Material Make(Color tint)
    {
        var shader=Resources.Load<Shader>("PNStarweaverEffects");if(shader==null)throw new InvalidOperationException("Missing star VFX shader.");
        var m=new Material(shader){name="Starreaper violet bow",hideFlags=HideFlags.HideAndDontSave};owned.Add(m);m.SetFloat("_Mode",0);m.SetFloat("_DstBlend",10);m.SetColor("_Tint",tint);return m;
    }
    private Vector3 Point(Vector2 p,float z=0)=>new Vector3((790-p.x)*(bowHeight/362f),(1040-p.y)*(bowHeight/362f),z);
    private Vector3 TopAnchor=>Point(new Vector2(840,872));
    private Vector3 BottomAnchor=>Point(new Vector2(840,1234));
    private void Ensure()
    {
        if(bow!=null){if(!bow.gameObject.activeSelf)bow.gameObject.SetActive(true);return;}
        var solidShader=Resources.Load<Shader>("PNStarreaperWeapon");if(solidShader==null)throw new InvalidOperationException("Missing solid Starreaper weapon shader.");
        bowMaterial=new Material(solidShader){name="Starreaper violet bow",hideFlags=HideFlags.HideAndDontSave};owned.Add(bowMaterial);lineMaterial=Make(new Color(.8f,.4f,1.3f));
        var go=new GameObject("Starreaper violet bow");go.layer=gameObject.layer;go.transform.SetParent(transform,false);bow=go.transform;
        mesh=PNStarreaperBowGeometry.Main(bowHeight);owned.Add(mesh);go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=bowMaterial;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
        lineMaterial.SetFloat("_Mode",22);lineMaterial.SetFloat("_DstBlend",1);lineMaterial.SetTexture("_NoiseTex",Resources.Load<Texture2D>("StarweaverVfx/Noise"));
        bowString=Line("Starreaper bow string",.0045f,3);stringHalo=Line("Starreaper string energy halo",.018f,3);nock=Line("Starreaper nocked arrow",.008f,2);
        var energy=Make(new Color(1.5f,.65f,2.2f));energy.SetFloat("_Mode",22);energy.SetFloat("_DstBlend",1);energy.SetTexture("_NoiseTex",Resources.Load<Texture2D>("StarweaverVfx/Noise"));bowString.sharedMaterial=energy;
        pierceFx=new PNStarreaperEnergyMesh(bow,"Xuyen Tinh charge");
        Part("Outer shoulder armour",PNStarreaperBowGeometry.Plate(bowHeight,true),new Vector3(0,0,-.024f));
        Part("Inner shoulder armour",PNStarreaperBowGeometry.Plate(bowHeight,false),new Vector3(0,0,-.018f));
    }
    private void Part(string name,Mesh partMesh,Vector3 at)
    {
        owned.Add(partMesh);var go=new GameObject(name);go.layer=gameObject.layer;go.transform.SetParent(bow,false);go.transform.localPosition=at;
        go.AddComponent<MeshFilter>().sharedMesh=partMesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=bowMaterial;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
    }
    private LineRenderer Line(string label, float width, int points)
    {
        var go = new GameObject(label); go.layer = gameObject.layer; go.transform.SetParent(bow, false);
        var l = go.AddComponent<LineRenderer>(); l.sharedMaterial = lineMaterial; l.useWorldSpace = true; l.positionCount = points; l.widthMultiplier = width;
        l.startColor = l.endColor = Pale; l.shadowCastingMode = ShadowCastingMode.Off; l.receiveShadows = false; l.enabled = false; return l;
    }

    public void Tick(float dt)
    {
        if (body == null) body = GetComponent<PNVisualBody>();
        var actor = echoActor != null ? echoActor : body != null ? body.Actor : null;
        if (actor == null) return;
        Ensure();
        if (owner != actor) { owner = actor; }

        fxAge+=Mathf.Max(0,dt);retreatAge+=Mathf.Max(0,dt);idleClock+=Mathf.Max(0,dt);
        float idleTarget=!drawing&&release<=.01f&&retreatAge>retreatDuration+.45f?1:0;
        idleWeight=Mathf.MoveTowards(idleWeight,idleTarget,Mathf.Max(0,dt)/.15f);
        float side = actor.FacingLeft ? -1 : 1;
        release = Mathf.Max(0f, release - Mathf.Max(0f, dt) * 3f);
        float lift=rain?Mathf.SmoothStep(0,1,drawing?Mathf.Clamp01(age/.22f):release):0;
        float raisedX=Mathf.Max(.58f,actor.Mesh.bounds.extents.x+.30f)*.62f*.77f*.88f;
        float raisedY=Mathf.Max(.43f,actor.Mesh.bounds.max.y*.88f);
        float reach=rain?Mathf.SmoothStep(0,1,drawing?Mathf.Clamp01((age-.10f)/.30f):release):0;
        var position=actor.transform.TransformPoint(new Vector3(side*Mathf.Lerp(.26f,raisedX,reach),Mathf.Lerp(.2f,raisedY,lift),-.025f));
        float angle = 0f;
        float engage = drawing ? Mathf.Clamp01(rain?(age-.16f)/.25f:age/.25f) : release;
        float pull = 0f;

        if (drawing)
        {
            var local = actor.transform.InverseTransformDirection(aim);
            float toward = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;                // the bow's +X turned toward the aim
            angle = Mathf.LerpAngle(0, Mathf.DeltaAngle(side<0?180:0,toward), Mathf.SmoothStep(0, 1, engage));
            pull = Mathf.SmoothStep(0, 1, Mathf.Clamp01(age / windup))*(fired?Mathf.Pow(release,3):1);
        }
        if(!drawing&&fired)
        {
            pull=Mathf.Pow(release,3);
            if(rain){var local=actor.transform.InverseTransformDirection(aim);angle=Mathf.DeltaAngle(side<0?180:0,Mathf.Atan2(local.y,local.x)*Mathf.Rad2Deg)*Mathf.SmoothStep(0,1,release)-4*Mathf.Sin((1-release)*Mathf.PI)*release;}
        }

        if(echoActor==null)
        {
            position+=actor.transform.TransformVector(new Vector3(side*.004f*Mathf.Sin(idleClock*1.70f),.012f*Mathf.Sin(idleClock*2.24f),0))*idleWeight;
            angle+=side*2.5f*Mathf.Sin(idleClock*1.90f)*idleWeight;
        }
        bow.position = position; bow.rotation = actor.transform.rotation * Quaternion.Euler(0, side*-16, angle);
        bow.localScale=new Vector3(side,1,1);
        var top=TopAnchor;var bottom=BottomAnchor;float back=Mathf.Lerp(bottom.x,top.x,-bottom.y/(top.y-bottom.y))-.09f*pull;
        stringPoints[0]=bow.TransformPoint(top);stringPoints[1]=bow.TransformPoint(new Vector3(back,0,0));stringPoints[2]=bow.TransformPoint(bottom);
        bowString.SetPositions(stringPoints); bowString.enabled = true;
        stringHalo.SetPositions(stringPoints);stringHalo.enabled=true;stringHalo.startColor=stringHalo.endColor=new Color(1,1,1,.22f+pull*.10f+idleWeight*.025f*Mathf.Sin(idleClock*1.8f));
        bowString.startColor=bowString.endColor=new Color(Pale.r,Pale.g,Pale.b,1-idleWeight*(.12f+.08f*Mathf.Sin(idleClock*1.6f)));
        bowString.sharedMaterial.SetFloat("_FxTime",idleClock);stringHalo.sharedMaterial.SetFloat("_FxTime",idleClock);
        nock.enabled = drawing && pull > .02f && !pierce;
        if (nock.enabled)
        {
            nockPoints[0] = bow.TransformPoint(new Vector3(back, 0, 0)); nockPoints[1] = bow.TransformPoint(new Vector3(belly+.09f, 0, 0));
            nock.SetPositions(nockPoints); nock.startColor = nock.endColor = Color.Lerp(Pale, Violet, 1f - pull);
        }
        float storedScale=pierce&&!rain?1.15f:1;
        muzzlePosition=bow.TransformPoint(new Vector3(back+.34f*storedScale,0,-.012f));
        projectilePosition=bow.TransformPoint(new Vector3(back+.17f*storedScale,0,-.012f));
        DrawPierce(back,pull);
        DrawRetreatCoat(actor);
    }

    private void DrawPierce(float back,float pull)
    {
        float charge= pierce&&drawing&&!fired?Mathf.Clamp01(age/windup):pierce&&fired?release*release:0;
        bowMaterial.SetFloat("_ChargeGlow",charge);
        pierceFx.Begin(fxAge);
        var x=bow.TransformVector(Vector3.right).normalized;var y=bow.up;
        var tip=MuzzlePosition;
        var tail=bow.TransformPoint(new Vector3(back,0,-.012f));
        if(pierce&&drawing&&!fired)
        {
            float q=Mathf.Clamp01(age/windup);
            var centre=ProjectilePosition;
            float absorbed=0;
            for(int ribbon=0;ribbon<3;ribbon++)
            {
                float head=IntakeHead(ribbon,q);
                absorbed+=Mathf.Exp(-Mathf.Pow((head-1.55f)*15,2));
            }
            bowMaterial.SetFloat("_ChargeGlow",q+absorbed*.18f);
            pierceFx.Body(centre,x,rain?y:y*1.25f,-bow.forward*.004f,rain?1:1.15f,rain?.36f:.44712f,fxAge,.65f+q*.35f+absorbed*.12f,rain?1:1.3f);
            // Each stream has one bounded lifespan. The head reaches the front
            // tip first, then stays there while the entire trailing window enters.
            // The latest tail finishes at 90% of windup, before release can cut it.
            for(int ribbon=0;ribbon<3;ribbon++)
            {
                float startPhase=.04f+ribbon*.10f,endPhase=.72f+ribbon*.09f;
                if(q<=startPhase||q>=endPhase)continue;
                float sign=ribbon%2==0?1:-1;
                var target=tip;
                var start=rain
                    ? target-Vector3.up*(.54f+ribbon*.07f)+y*sign*(.24f+ribbon*.06f)-x*.08f
                    : target+x*(ribbon==2?.20f:-.52f-ribbon*.09f)+y*sign*(ribbon==2?.43f:.33f+ribbon*.035f);
                float head=IntakeHead(ribbon,q);
                float born=Mathf.SmoothStep(0,1,Mathf.Clamp01((q-startPhase)/.07f));
                float tailPhase=Mathf.Clamp01(head-.62f),endHead=Mathf.Clamp01(head);
                float headPath=endHead*(.6f+.4f*endHead),tailPath=tailPhase*(.6f+.4f*tailPhase);
                float plumeLength=Vector3.Distance(Inflow(start,target,y,sign,tailPath),Inflow(start,target,y,sign,headPath));
                if(plumeLength<.0001f)continue;
                float plumeWidth=Mathf.Min(.26f*(1-endHead*.35f),plumeLength*.8f+.01f);
                float span=Mathf.Max(.0001f,endHead-tailPhase);
                for(int segment=0;segment<12;segment++)
                {
                    float u0=segment/12f,u1=(segment+1)/12f;
                    float t0=Mathf.Clamp01(head-(1-u0)*.62f),t1=Mathf.Clamp01(head-(1-u1)*.62f);
                    if(t1-t0<.00001f)continue;
                    float uv0=(t0-tailPhase)/span,uv1=(t1-tailPhase)/span;
                    float path0=t0*(.6f+.4f*t0),path1=t1*(.6f+.4f*t1);
                    var a=Inflow(start,target,y,sign,path0);var b=Inflow(start,target,y,sign,path1);
                    var acrossA=InflowAcross(start,target,y,sign,path0);var acrossB=InflowAcross(start,target,y,sign,path1);
                    float width=plumeWidth,opacity=(.65f+q*.35f)*born;
                    pierceFx.Strip(false,a,b,acrossA,acrossB,width*1.35f,new Color(.012f,.003f,.025f,opacity),12,uv0,uv1);
                    pierceFx.Strip(true,a-bow.forward*.003f,b-bow.forward*.003f,acrossA,acrossB,width*1.35f,new Color(.42f,.06f,.98f,opacity*.48f),13,uv0,uv1);
                    float packet=Mathf.Sin((uv0+uv1)*.5f*Mathf.PI)*born*(.35f+q*.65f);
                    pierceFx.Strip(true,a-bow.forward*.006f,b-bow.forward*.006f,acrossA,acrossB,.026f,new Color(1.55f,.88f,.21f,packet*.72f),1,uv0,uv1);
                }
                float starPath=(tailPath+headPath)*.5f;
                var starPosition=Inflow(start,target,y,sign,starPath)+InflowAcross(start,target,y,sign,starPath)*(sign*plumeWidth*.38f);
                float starSize=Mathf.Min(.030f+ribbon*.004f,plumeLength*.5f);
                pierceFx.BlackStar(starPosition,x,y,-bow.forward*.004f,starSize,born*Mathf.Clamp01(plumeLength/.06f),ribbon*.7f+q*.3f);
            }
            float finish=Mathf.Clamp01((q-.90f)/.10f);
            float tipPulse=Mathf.Sin(finish*Mathf.PI);
            pierceFx.Quad(true,tip-x*.045f-bow.forward*.012f,x,y,.13f,.022f,new Color(1.3f,.65f,1.9f,tipPulse*.8f),1);
            var crown=bow.TransformPoint(Point(new Vector2(900,740)));
            var foot=bow.TransformPoint(Point(new Vector2(780,1180)));
            pierceFx.Quad(true,crown,x,y,.19f,.15f,new Color(.65f,.16f,1.4f,q*.30f),2);
            pierceFx.Quad(true,foot,x,y,.12f,.10f,new Color(.65f,.16f,1.4f,q*.30f),2);
            for(int i=0;i<18;i++)
            {
                float u=Mathf.Repeat(age*(.7f+i*.013f)+i*.618034f,1);float a=i*2.39996f;
                var start=tail-x*(.05f+i%3*.035f)+y*(Mathf.Sin(a)*.19f);
                var at=Vector3.Lerp(start,tip,u*u);float opacity=q*Mathf.Sin(u*Mathf.PI)*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.84f,.94f,q)));
                var color=i%7==0?new Color(1.3f,.85f,.35f,opacity*.65f):new Color(.7f,.35f,1.6f,opacity);
                pierceFx.Quad(true,at,x,y,.008f+i%3*.003f,.008f+i%3*.003f,color,3);
            }
        }
        if(pierce&&fired&&release>.55f)
        {
            float t=(1-release)/.45f,alpha=(1-t)*(1-t);
            pierceFx.Quad(true,tip+x*.035f,x,y,.22f+t*.13f,.09f*(1-t)+.012f,new Color(2.8f,1.6f,3.4f,alpha),2);
            pierceFx.Quad(true,tip,x,y,.10f,.18f,new Color(1.5f,.6f,2.3f,alpha),3);
            for(int i=0;i<8;i++)
            {
                float a=i*2.39996f;var at=tip-x*(.025f+t*.13f)+y*(Mathf.Sin(a)*t*.08f);
                pierceFx.Quad(true,at,x,y,.018f,.008f,new Color(.9f,.45f,1.6f,alpha),3);
            }
        }
        if(retreatAge<retreatDuration+.28f)
        {
            float t=Mathf.Clamp01((retreatAge-retreatDuration)/.28f);
            float fade=1-Mathf.SmoothStep(0,1,t);
            var delta=transform.position-retreatOrigin;
            if(delta.sqrMagnitude>.0001f)
            {
                var along=delta.normalized;
                float height=Mathf.Max(.35f,owner.Mesh.bounds.size.y);
                float length=Mathf.Min(.85f,delta.magnitude)*(1-t*.55f);
                var centre=transform.position-along*(length*.5f+t*.10f)+Vector3.up*(owner.Mesh.bounds.center.y);
                pierceFx.Quad(false,centre,along,Vector3.up,length,height*1.7f,new Color(.008f,.002f,.020f,fade*.90f),0);
                pierceFx.Quad(true,centre-bow.forward*.004f,along,Vector3.up,length,height*1.5f,new Color(.55f,.09f,1.15f,fade*.32f),7);
                for(int i=0;i<8;i++)
                {
                    float u=(i+.5f)/8f;var at=transform.position-along*(.05f+u*length)+Vector3.up*(height*(.12f+(i%4)*.22f));
                    if(i%3==0)pierceFx.BlackStar(at,along,Vector3.up,-bow.forward*.004f,.04f,fade*.7f,i);
                    else pierceFx.Quad(true,at,along,Vector3.up,.012f,.012f,new Color(i==2?1.3f:.60f,i==2?.8f:.15f,i==2?.28f:1.3f,fade*.55f),3);
                }
                if(retreatAge>=retreatDuration)
                {
                    for(int puff=0;puff<3;puff++)
                    {
                        var at=transform.position-along*(.08f+t*.20f)+Vector3.up*(height*(.20f+puff*.25f));
                        float size=.28f+t*.14f;
                        pierceFx.Quad(false,at,along,Vector3.up,size,size,new Color(.012f,.003f,.028f,fade*.65f),23,frame:t*10);
                        pierceFx.Quad(true,at-bow.forward*.006f,along,Vector3.up,size,size,new Color(.50f,.08f,1.05f,fade*.18f),24,frame:t*10);
                    }
                }
            }
        }
        if(echoActor==null&&idleWeight>.01f&&!drawing&&release<=.01f&&retreatAge>retreatDuration+.45f)
        {
            for(int i=0;i<4;i++)
            {
                float u=Mathf.Repeat(idleClock/4.2f+i*.27f,1);if(u>.15f)continue;
                float p=u/.15f;var at=bow.position+bow.up*(.035f+p*.055f)+bow.right*(Mathf.Sin(i*2.4f)*.04f);
                pierceFx.Quad(true,at,bow.right,bow.up,.009f,.009f,new Color(.64f,.28f,1.0f,Mathf.Sin(p*Mathf.PI)*idleWeight*.45f),3);
            }
        }
        pierceFx.End();
    }

    private void DrawRetreatCoat(PNVisualActor actor)
    {
        bool active=echoActor==null&&retreatAge<retreatDuration+.45f;
        if(!active){if(retreatCoat!=null)retreatCoat.SetActive(false);return;}
        if(retreatCoat==null)
        {
            retreatCoat=new GameObject("Starreaper retreat coat");retreatCoat.layer=gameObject.layer;retreatCoat.transform.SetParent(actor.transform,false);retreatCoat.transform.localPosition=new Vector3(0,0,-.009f);owned.Add(retreatCoat);
            retreatCoatFilter=retreatCoat.AddComponent<MeshFilter>();
            retreatCoatMaterial=new Material(Resources.Load<Shader>("PNStarreaperPierce")){name="Starreaper retreat coat",hideFlags=HideFlags.HideAndDontSave,renderQueue=3014};owned.Add(retreatCoatMaterial);
            retreatCoatMaterial.SetFloat("_EchoMode",3);retreatCoatMaterial.SetFloat("_DstBlend",10);retreatCoatMaterial.SetTexture("_RainNoiseTex",Resources.Load<Texture2D>("StarreaperVfx/RainNoise"));
            var renderer=retreatCoat.AddComponent<MeshRenderer>();renderer.sharedMaterial=retreatCoatMaterial;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.sortingOrder=actor.Renderer.sortingOrder+1;
        }
        retreatCoat.SetActive(true);retreatCoatFilter.sharedMesh=actor.Mesh;
        retreatCoatMaterial.SetTexture("_MainTex",actor.Renderer.sharedMaterial.GetTexture("_MainTex"));retreatCoatMaterial.SetFloat("_FxTime",retreatAge);
        float fade=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(retreatDuration,retreatDuration+.45f,retreatAge));retreatCoatMaterial.SetFloat("_EchoOpacity",fade*.78f);
    }

    private static float IntakeHead(int stream,float q)
    {
        float start=.04f+stream*.10f,end=.72f+stream*.09f;
        float p=Mathf.Clamp01((q-start)/(end-start));
        return p*(.6f+.4f*p)*1.62f;
    }
    private Vector3 InflowAcross(Vector3 start,Vector3 target,Vector3 up,float sign,float u)
    {
        var d=Inflow(start,target,up,sign,Mathf.Min(1,u+.001f))-Inflow(start,target,up,sign,Mathf.Max(0,u-.001f));
        return Vector3.Cross(bow.forward,d).normalized;
    }
    private Vector3 Inflow(Vector3 start,Vector3 tip,Vector3 up,float sign,float u)
    {
        var control=(start+tip)*.5f+up*sign*(.10f+Mathf.Sin(fxAge*1.4f)*.012f);
        float v=1-u;return start*(v*v)+control*(2*v*u)+tip*(u*u);
    }

    public void Dispose(){DisposeOwned();owner=null;drawing=false;fired=false;release=0;}
    private void DisposeOwned()
    {
        pierceFx?.Dispose();pierceFx=null;
        foreach (var o in owned) if (o != null) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }
        owned.Clear();retreatCoat=null;retreatCoatFilter=null;retreatCoatMaterial=null; mesh = null; bowMaterial = lineMaterial = null;
        if (bow != null) { if (Application.isPlaying) Destroy(bow.gameObject); else DestroyImmediate(bow.gameObject); }
        bow = null; bowString = stringHalo = nock = null;
    }
}
#endif
