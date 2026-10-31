#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Cast-owned rendering only: gold spear anchor, spectral spear and timestamped motion trails.
[DefaultExecutionOrder(250)]
public sealed class PNStarkeeperPointVisual : MonoBehaviour,IPNCameraFacing,IDisposable
{
    private const int ParticleCount=64,TrailSegments=18;
    private const float RecallSeconds=.30f;
    private sealed class History
    {
        private readonly Vector3[] points=new Vector3[64];private readonly float[] times=new float[64];
        private int next,count;
        public int Count=>count;public float OldestTime=>count>0?times[(next-count+64)%64]:0;
        public void Add(float time,Vector3 point)
        {
            if(count>0){int last=(next+63)%64;if(time<times[last])return;if(time==times[last]){points[last]=point;return;}}
            points[next]=point;times[next]=time;next=(next+1)%64;count=Mathf.Min(64,count+1);
        }
        public Vector3 Sample(float time)
        {
            if(count==0)return Vector3.zero;int first=(next-count+64)%64;
            if(time<=times[first])return points[first];
            for(int i=1;i<count;i++){int a=(first+i-1)%64,b=(first+i)%64;if(time<=times[b])return Vector3.Lerp(points[a],points[b],Mathf.InverseLerp(times[a],times[b],time));}
            return points[(next+63)%64];
        }
    }
    private readonly List<Material> materials=new List<Material>();private readonly List<Mesh> meshes=new List<Mesh>();
    private readonly History goldHistory=new History(),dashHistory=new History(),bodyHistory=new History();
    private float flightBirth=-1;
    private readonly Vector3[] bodyVertices=new Vector3[(TrailSegments+1)*2];private readonly Color[] bodyColors=new Color[(TrailSegments+1)*2];
    private readonly Vector3[] bodyGlowVertices=new Vector3[(TrailSegments+1)*2];private readonly Color[] bodyGlowColors=new Color[(TrailSegments+1)*2];
    private Mesh bodyTrail,bodyGlowTrail;private Transform tipShell,tipCore;private Material tipShellMaterial,tipCoreMaterial;
    private readonly Vector3[] particleVertices=new Vector3[ParticleCount*4];private readonly Color[] particleColors=new Color[ParticleCount*4];
    private readonly Vector3[][] trailVertices={new Vector3[(TrailSegments+1)*2],new Vector3[(TrailSegments+1)*2],new Vector3[(TrailSegments+1)*2]};
    private readonly Color[][] trailColors={new Color[(TrailSegments+1)*2],new Color[(TrailSegments+1)*2],new Color[(TrailSegments+1)*2]};
    private readonly Mesh[] trails=new Mesh[3];private readonly Material[] trailMaterials=new Material[3];
    private Transform source,spear,gold,energy,ground,flash;private MeshRenderer sourceRenderer;private bool originalVisible;
    private PNStarkeeperPresentation presentation;private PNVisualBody ownerBody;private PNStarkeeperWeaponGlow energyAura;
    private Material energyMaterial,groundMaterial,flashMaterial;private Mesh particles;
    private Camera preview;private Vector3 anchor,direction,throwOrigin,plantPosition,recallOrigin;private Quaternion throwRotation,plantRotation,recallRotation;
    private float age,windup,throwSeconds,anchorDiameter,finishAge=-1;private bool flying,reached,disposed,cancelFading;
    public bool Reached=>reached;public float VisualAge=>age;public int TrailSampleCount=>dashHistory.Count;
    public static PNStarkeeperPointVisual Create(Transform source,PNStarkeeperPresentation presentation,Vector3 anchor,float markRadius,float windup,Camera preview=null)
    {
        var root=new GameObject("Starkeeper star point");SceneManager.MoveGameObjectToScene(root,source.gameObject.scene);root.layer=source.gameObject.layer;
        var v=root.AddComponent<PNStarkeeperPointVisual>();v.source=source;v.ownerBody=source.GetComponent<PNVisualBody>();v.presentation=presentation;v.spear=source.Find("Star spear");v.anchor=anchor;v.windup=windup;v.throwSeconds=windup*.16f;v.anchorDiameter=markRadius*1.15f;v.preview=preview;
        var aim=anchor-source.position;aim.y=0;v.direction=aim.sqrMagnitude>.00001f?aim.normalized:Vector3.forward;
        try
        {
            if(v.spear==null)throw new InvalidOperationException("Starkeeper spear required.");
            v.sourceRenderer=v.spear.GetComponent<MeshRenderer>();v.originalVisible=v.sourceRenderer.enabled;
            v.throwOrigin=v.spear.position;v.throwRotation=v.spear.rotation;
            var down=(Vector3.down+v.direction*.20f).normalized;v.plantRotation=Quaternion.FromToRotation(Vector3.up,down);
            v.plantPosition=anchor+Vector3.up*.015f-v.plantRotation*(Vector3.up*.28f*v.spear.lossyScale.y);
            v.Build();v.sourceRenderer.enabled=false;v.SetTime(0,false);
            var identity=source.GetComponent<EveEngine.EveEntityIdentity>();if(identity!=null)PNWorldPresentationRegistration.Register(identity.World,root);
            if(Application.isPlaying)PNCameraFacingSystem.Register(v);return v;
        }
        catch{v.Dispose();throw;}
    }
    private Material Make(int mode,Color tint,bool additive=false,string texture=null)
    {
        var shader=Resources.Load<Shader>("PNStarweaverEffects");if(shader==null)throw new InvalidOperationException("Effect shader missing.");
        var m=new Material(shader){name="Starkeeper point "+mode,hideFlags=HideFlags.HideAndDontSave};materials.Add(m);m.SetFloat("_Mode",mode);m.SetFloat("_DstBlend",additive?1:10);m.SetColor("_Tint",tint);
        m.SetTexture("_NoiseTex",Resources.Load<Texture2D>("StarweaverVfx/Noise"));if(texture!=null)m.SetTexture("_MainTex",Resources.Load<Texture2D>("StarweaverVfx/"+texture));return m;
    }
    private static void SetBodyTrailTextures(Material material,int layer)
    {
        var mask=Resources.Load<Texture2D>("StarkeeperVfx/DashTrailMask");
        var flow=Resources.Load<Texture2D>("StarkeeperVfx/SlashFlow");
        if(mask==null || flow==null)throw new InvalidOperationException("Starkeeper dash trail textures missing.");
        material.SetTexture("_MainTex",mask);material.SetTexture("_SlashFlowTex",flow);material.SetFloat("_SlashLayer",layer);
    }
    private Mesh NewMesh(string name,int count,int[] triangles,Vector2[] uv)
    {
        var m=new Mesh{name="Starkeeper point "+name,hideFlags=HideFlags.HideAndDontSave};m.MarkDynamic();m.vertices=new Vector3[count];m.uv=uv;m.triangles=triangles;
        var colors=new Color[count];for(int i=0;i<count;i++)colors[i]=Color.white;m.colors=colors;meshes.Add(m);return m;
    }
    private Transform Draw(string name,Mesh mesh,Material material)
    {
        var go=new GameObject(name);go.layer=source.gameObject.layer;go.transform.SetParent(transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;return go.transform;
    }
    private Mesh Quad(string name)
    {
        var m=NewMesh(name,4,new[]{0,1,2,0,2,3},new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});m.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)};m.RecalculateBounds();return m;
    }
    private void Build()
    {
        var shared=spear.GetComponent<MeshFilter>().sharedMesh;
        gold=Draw("Thrown gold spear",shared,sourceRenderer.sharedMaterial);gold.localScale=spear.lossyScale;gold.GetComponent<MeshRenderer>().enabled=originalVisible;
        var spectral=UnityEngine.Object.Instantiate(shared);spectral.name="Starkeeper point spectral spear";spectral.hideFlags=HideFlags.HideAndDontSave;meshes.Add(spectral);
        var vertices=spectral.vertices;var uv=new Vector2[vertices.Length];var bounds=shared.bounds;
        for(int i=0;i<vertices.Length;i++){uv[i]=new Vector2(Mathf.InverseLerp(bounds.min.x,bounds.max.x,vertices[i].x),Mathf.InverseLerp(bounds.min.y,bounds.max.y,vertices[i].y));vertices[i].x*=.65f;vertices[i].z*=.65f;}
        spectral.vertices=vertices;spectral.uv=uv;spectral.RecalculateBounds();
        energyMaterial=Make(20,new Color(.45f,.08f,1.1f,1));energy=Draw("Spectral spear",spectral,energyMaterial);
        energyAura=new PNStarkeeperWeaponGlow(transform,energy,"Starkeeper point ");
        groundMaterial=Make(5,new Color(.58f,.17f,.95f,1),true,"Glow");ground=Draw("Anchor soft energy",Quad("ground glow"),groundMaterial);
        flashMaterial=Make(5,new Color(1.6f,.95f,.3f,1),true,"Glow");flash=Draw("Spear contact glint",Quad("contact glint"),flashMaterial);
        var puv=new Vector2[ParticleCount*4];var pt=new int[ParticleCount*6];var quv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};var qt=new[]{0,1,2,0,2,3};
        for(int i=0;i<ParticleCount;i++){for(int j=0;j<4;j++)puv[i*4+j]=quv[j];for(int j=0;j<6;j++)pt[i*6+j]=i*4+qt[j];}
        particles=NewMesh("gold motes",puv.Length,pt,puv);Draw("Gold energy particles",particles,Make(1,new Color(1.55f,1.04f,.28f,1),true));
        var tuv=new Vector2[(TrailSegments+1)*2];var tt=new int[TrailSegments*6];
        for(int i=0;i<=TrailSegments;i++){float u=i/(float)TrailSegments;tuv[i*2]=new Vector2(u,0);tuv[i*2+1]=new Vector2(u,1);if(i<TrailSegments){int k=i*2,j=i*6;tt[j]=k;tt[j+1]=k+1;tt[j+2]=k+2;tt[j+3]=k+1;tt[j+4]=k+3;tt[j+5]=k+2;}}
        for(int layer=0;layer<3;layer++)
        {
            trails[layer]=NewMesh("path trail "+layer,tuv.Length,tt,tuv);trailMaterials[layer]=Make(22,layer==0?new Color(1.5f,.85f,.20f,1):new Color(.65f,.20f,1.5f,1),true);
            Draw(layer==0?"Gold throw and recall trail":layer==1?"Spectral motion trail":"Spectral faint afterimage",trails[layer],trailMaterials[layer]);
        }
        bodyTrail=NewMesh("body wake",tuv.Length,tt,tuv);
        var bodyMaterial=Make(25,new Color(.075f,.012f,.16f,1));SetBodyTrailTextures(bodyMaterial,0);bodyMaterial.renderQueue=2997;
        Draw("Starkeeper body trail",bodyTrail,bodyMaterial);
        bodyGlowTrail=NewMesh("body energy streak",tuv.Length,tt,tuv);
        var bodyGlowMaterial=Make(25,new Color(.45f,.075f,.95f,1),true);SetBodyTrailTextures(bodyGlowMaterial,1);bodyGlowMaterial.renderQueue=2998;
        Draw("Starkeeper body energy trail",bodyGlowTrail,bodyGlowMaterial);
        tipCoreMaterial=Make(5,new Color(1.55f,1.05f,2.1f,1),true,"Glow");tipCore=Draw("Tip white violet core",Quad("tip core"),tipCoreMaterial);
        tipShellMaterial=Make(24,new Color(.85f,.26f,1.7f,1),true);
        const int ringSteps=20,depthSteps=4;var cupVertices=new Vector3[(ringSteps+1)*(depthSteps+1)];var cupUv=new Vector2[cupVertices.Length];var cupTriangles=new int[ringSteps*depthSteps*6];
        for(int row=0;row<=depthSteps;row++)for(int col=0;col<=ringSteps;col++)
        {
            int k=row*(ringSteps+1)+col;float v=row/(float)depthSteps,a=col*Mathf.PI*2/ringSteps,r=.012f+.090f*v*v;
            cupVertices[k]=new Vector3(Mathf.Cos(a)*r,-v*.16f,Mathf.Sin(a)*r);cupUv[k]=new Vector2(col/(float)ringSteps,v);
            if(row<depthSteps && col<ringSteps){int t=(row*ringSteps+col)*6;cupTriangles[t]=k;cupTriangles[t+1]=k+1;cupTriangles[t+2]=k+ringSteps+1;cupTriangles[t+3]=k+1;cupTriangles[t+4]=k+ringSteps+2;cupTriangles[t+5]=k+ringSteps+1;}
        }
        var cup=NewMesh("tip curved sheath",cupVertices.Length,cupTriangles,cupUv);cup.vertices=cupVertices;cup.RecalculateBounds();tipShell=Draw("Tip pressure sheath",cup,tipShellMaterial);
    }
    public void SetTime(float seconds,bool isFlying)
    {
        if(disposed)return;age=seconds;flying=isFlying && finishAge<0;if(flying && flightBirth<0)flightBirth=age;foreach(var m in materials)m.SetFloat("_FxTime",age);Align();
    }
    public void Finish(bool arrived)
    {
        if(disposed || finishAge>=0)return;reached=arrived;finishAge=age;flying=false;recallOrigin=gold.position;recallRotation=gold.rotation;
        dashHistory.Add(age,energy.TransformPoint(new Vector3(0,.28f,0)));bodyHistory.Add(age,BodyPosition());Align();
    }
    private void UpdateTrail(int layer,History history,float window,float maximum,float width,float opacity,Camera camera)
    {
        var vertices=trailVertices[layer];var colors=trailColors[layer];var previous=history.Sample(age);float distance=0;
        for(int i=0;i<=TrailSegments;i++)
        {
            float u=i/(float)TrailSegments;var point=history.Sample(age-u*window);var delta=point-previous;float segment=delta.magnitude;
            if(distance+segment>maximum && segment>.00001f)point=previous+delta/segment*Mathf.Max(0,maximum-distance);
            distance+=Vector3.Distance(point,previous);var tangent=point-previous;if(tangent.sqrMagnitude<.000001f)tangent=-direction;
            var side=Vector3.Cross(camera.transform.forward,tangent).normalized;float w=width*Mathf.Pow(1-u,.7f);var offset=layer==2?camera.transform.up*.012f:Vector3.zero;
            vertices[i*2]=point-side*w+offset;vertices[i*2+1]=point+side*w+offset;colors[i*2]=colors[i*2+1]=new Color(1,1,1,opacity*(1-u));previous=point;
        }
        trails[layer].vertices=vertices;trails[layer].colors=colors;trails[layer].RecalculateBounds();
    }
    private Vector3 BodyPosition()
    {
        var actor=ownerBody!=null?ownerBody.Actor:null;return actor!=null?actor.transform.TransformPoint(new Vector3(0,actor.CapsuleHeight*.5f,0)):source.position+Vector3.up*.2f;
    }
    private void UpdateBodyTrail(Camera camera,float opacity)
    {
        if(flying)bodyHistory.Add(age,BodyPosition());
        var actor=ownerBody!=null?ownerBody.Actor:null;float height=actor!=null?actor.CapsuleHeight:.4f;
        var previous=bodyHistory.Sample(age);float length=0;float window=Mathf.Min(.13f,Mathf.Max(0,age-bodyHistory.OldestTime));
        for(int i=0;i<=TrailSegments;i++)
        {
            float u=i/(float)TrailSegments;var point=bodyHistory.Sample(age-u*window);var delta=point-previous;float distance=delta.magnitude;
            if(length+distance>.62f && distance>.00001f)point=previous+delta/distance*Mathf.Max(0,.62f-length);
            length+=Vector3.Distance(previous,point);var tangent=point-previous;if(tangent.sqrMagnitude<.000001f)tangent=-direction;
            var side=Vector3.Cross(camera.transform.forward,tangent).normalized;float taper=Mathf.Clamp01(1-Mathf.Max(u,length/.62f));float width=height*.70f*Mathf.Pow(taper,.65f);
            bodyVertices[i*2]=point-side*width;bodyVertices[i*2+1]=point+side*width;bodyColors[i*2]=bodyColors[i*2+1]=new Color(1,1,1,opacity*(1-u)*.75f);
            bodyGlowVertices[i*2]=point-side*width*.70f;bodyGlowVertices[i*2+1]=point+side*width*.70f;
            bodyGlowColors[i*2]=bodyGlowColors[i*2+1]=new Color(1,1,1,opacity*(1-u)*.5f);previous=point;
        }
        bodyTrail.vertices=bodyVertices;bodyTrail.colors=bodyColors;bodyTrail.RecalculateBounds();
        bodyGlowTrail.vertices=bodyGlowVertices;bodyGlowTrail.colors=bodyGlowColors;bodyGlowTrail.RecalculateBounds();
    }
    public bool Align()
    {
        if(disposed || source==null)return false;var camera=PNCameraFacing.ResolveCamera(preview,Application.isPlaying);if(camera==null)return false;
        var right=camera.transform.right;var up=camera.transform.up;float after=finishAge>=0?Mathf.Max(0,age-finishAge):-1;
        float build=Mathf.Clamp01((age-throwSeconds)/(windup*.76f));float recall=after<0?0:Mathf.Clamp01(after/RecallSeconds);
        if(after<0)
        {
            float p=Mathf.Clamp01(age/throwSeconds),ease=Mathf.SmoothStep(0,1,p);
            gold.SetPositionAndRotation(Vector3.Lerp(throwOrigin,plantPosition,p)+Vector3.up*(Mathf.Sin(p*Mathf.PI)*.48f),Quaternion.Slerp(throwRotation,plantRotation,ease));
        }
        else
        {
            float p=Mathf.SmoothStep(0,1,recall);gold.SetPositionAndRotation(Vector3.Lerp(recallOrigin,spear.position,p)+Vector3.up*(Mathf.Sin(p*Mathf.PI)*.16f),Quaternion.Slerp(recallRotation,spear.rotation,p));
        }
        gold.localScale=spear.lossyScale;gold.gameObject.SetActive(after<0 || recall<1);
        if(sourceRenderer!=null)sourceRenderer.enabled=originalVisible && after>=0 && recall>=1;
        goldHistory.Add(age,gold.TransformPoint(new Vector3(0,.28f,0)));
        float dissolve=after<0?1:1-Mathf.Clamp01(after/.16f);
        energy.SetPositionAndRotation(spear.position+direction*(flying?.07f:0),spear.rotation);energy.localScale=spear.lossyScale*(1.1f+build*.18f);
        energyMaterial.SetFloat("_Reveal",build);energyMaterial.SetFloat("_Opacity",build*dissolve*.80f);
        energyAura.Set(build*dissolve*.44f,age,camera,0);
        var tip=energy.TransformPoint(new Vector3(0,.28f,0));if(flying)dashHistory.Add(age,tip);
        float land=age>=throwSeconds?Mathf.Exp(-(age-throwSeconds)*15):0,ready=after<0?Mathf.SmoothStep(0,1,Mathf.Clamp01((age-(windup-.20f))/.20f)):0;
        ground.SetPositionAndRotation(anchor+Vector3.up*.018f,Quaternion.Euler(90,0,0));ground.localScale=Vector3.one*(anchorDiameter*(1-ready*.325f));
        groundMaterial.SetFloat("_Opacity",age<throwSeconds?0:after<0?.12f+land*.25f+ready*.15f:(1-recall)*.12f);
        flash.SetPositionAndRotation(after<0?anchor+Vector3.up*.055f:tip,camera.transform.rotation);flash.localScale=Vector3.one*(after<0?.19f:.12f);
        flashMaterial.SetFloat("_Opacity",after<0?land*.45f+ready*.16f:reached?Mathf.Exp(-after*24)*.25f:0);
        float goldTrail=(age<throwSeconds+.09f || after>=0 && recall<1)?1:0;
        float dashTrail=flying?1:after>=0?1-Mathf.Clamp01(after/.14f):0;
        UpdateTrail(0,goldHistory,.09f,.65f,.024f,goldTrail,camera);UpdateTrail(1,dashHistory,.08f,.65f,.035f,dashTrail,camera);UpdateTrail(2,dashHistory,.10f,.65f,.065f,dashTrail*.22f,camera);
        UpdateBodyTrail(camera,dashTrail);
        float pressure=flying?1+.35f*Mathf.Exp(-(age-flightBirth)*22):reached && after>=0?Mathf.Clamp01(1-after/.07f):0;
        tipShell.SetPositionAndRotation(tip,energy.rotation);tipShellMaterial.SetFloat("_Opacity",pressure*.68f);
        var projectedAxis=Vector3.ProjectOnPlane(energy.up,camera.transform.forward);if(projectedAxis.sqrMagnitude<.00001f)projectedAxis=up;
        tipCore.SetPositionAndRotation(tip+energy.up*.012f,Quaternion.LookRotation(camera.transform.forward,projectedAxis.normalized));tipCore.localScale=new Vector3(.05f,.09f,.05f);tipCoreMaterial.SetFloat("_Opacity",pressure*.65f);
        for(int i=0;i<ParticleCount;i++)
        {
            float a=i*2.399963f,q=Mathf.Repeat(age*(.85f+(i%3)*.08f)+i*.137f,1),alpha=0,size=.012f+(i%3)*.004f;Vector3 point=anchor;
            if(after<0 && !flying && age>=throwSeconds)
            {
                if(i<44)
                {
                    var target=energy.TransformPoint(new Vector3(0,Mathf.Lerp(-.24f,.28f,(i%17)/16f),0));
                    point=target+(right*Mathf.Cos(a)+up*Mathf.Sin(a))*(.16f*(1-q));alpha=Mathf.Sin(q*Mathf.PI)*build*.8f;
                }
                else {point=anchor+Vector3.up*(q*.18f)+(right*Mathf.Cos(a)+camera.transform.forward*Mathf.Sin(a))*(.13f+.05f*q);alpha=Mathf.Sin(q*Mathf.PI)*(.20f+ready*.25f);}
            }
            else if(flying && i<18)
            {
                point=tip-direction*(q*.28f)+(right*Mathf.Cos(a)+up*Mathf.Sin(a))*(.025f+q*.05f);alpha=(1-q)*.5f;
                if(i<6)
                {
                    var flank=Vector3.Cross(camera.transform.forward,energy.up).normalized;float sign=i%2==0?-1:1;
                    point=tip+flank*(sign*(.025f+q*.075f))-energy.up*(q*.06f);alpha=(1-q)*.85f;size*=.8f;
                }
            }
            else if(after>=0 && after<.16f && i<18)
            {
                point=tip+(right*Mathf.Cos(a)+up*Mathf.Sin(a))*after*.4f;alpha=(1-after/.16f)*.35f;
            }
            for(int j=0;j<4;j++){float x=j==0||j==3?-.5f:.5f,y=j<2?-.5f:.5f;particleVertices[i*4+j]=point+(right*x+up*y)*size;particleColors[i*4+j]=new Color(1,1,1,alpha);}
        }
        particles.vertices=particleVertices;particles.colors=particleColors;particles.RecalculateBounds();return true;
    }
    public void CancelFade()
    {
        if(disposed)return;
        if((!Application.isPlaying && preview==null) || ownerBody==null || ownerBody.Actor==null){Dispose();return;}
        reached=false;finishAge=age;flying=false;cancelFading=true;recallOrigin=gold.position;recallRotation=gold.rotation;gameObject.name="Starkeeper Point Recall";Align();
    }
    public void AdvanceCancelFade(float delta)
    {
        if(disposed || !cancelFading)return;age+=Mathf.Max(0,delta);foreach(var m in materials)m.SetFloat("_FxTime",age);
        if(age-finishAge>=RecallSeconds){Dispose();return;}Align();
    }
    private void LateUpdate()
    {
        if(source==null || !source.gameObject.activeInHierarchy){Dispose();return;}
        if(cancelFading){if(ownerBody==null || ownerBody.Actor==null){Dispose();return;}AdvanceCancelFade(ownerBody.Actor.ClockDelta());}else Align();
    }
    private void Release()
    {
        if(disposed)return;disposed=true;PNCameraFacingSystem.Unregister(this);if(sourceRenderer!=null)sourceRenderer.enabled=originalVisible;
        energyAura?.Dispose();energyAura=null;foreach(var m in materials)DestroyOwned(m);foreach(var m in meshes)DestroyOwned(m);materials.Clear();meshes.Clear();
    }
    public void Dispose(){if(disposed)return;Release();gameObject.SetActive(false);DestroyOwned(gameObject);}
    private void OnDestroy(){Release();}
    private static void DestroyOwned(UnityEngine.Object value){if(value==null)return;if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
}
#endif
