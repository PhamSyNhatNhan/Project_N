#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Cast-owned presentation: every shader/particle reads the Action clock. No Pack scripts or shaders.
public sealed class PNStarweaverTelegraph : IDisposable, IPNCameraFacing
{
    private const int Segments=40, Strands=4;
    private readonly GameObject root,curtain,shockwave,impactGlow,discharge;
    private readonly List<Mesh> meshes=new List<Mesh>();
    private readonly List<Material> materials=new List<Material>();
    private readonly GameObject[] stars=new GameObject[4],shardRoots=new GameObject[1];
    private readonly Transform[,] shardPieces=new Transform[1,4];
    private readonly Vector3[] shardCenters=new Vector3[4];
    private readonly MeshRenderer[,] crystalRenderers=new MeshRenderer[4,2];
    private readonly MaterialPropertyBlock[] crystalBlocks=new MaterialPropertyBlock[4];
    private readonly ParticleSystem.Particle[] formationBuffer=new ParticleSystem.Particle[96];
    private readonly Material[] edges=new Material[3];
    private readonly RibbonData[] ribbons=new RibbonData[3];
    private readonly Vector3[] points=new Vector3[3],anchors=new Vector3[3];
    private readonly bool[] revealed=new bool[3];
    private readonly Vector3 centre=new Vector3(0,.85f,0);
    private readonly LineRenderer[] intakeLines=new LineRenderer[4],goldPackets=new LineRenderer[3];
    private readonly Transform[] intakeHeads=new Transform[4];
    private readonly PNCameraFacingTarget[] intakeFacings=new PNCameraFacingTarget[4];
    private readonly Vector3[][] intakePoints={new Vector3[16],new Vector3[16],new Vector3[16],new Vector3[16]};
    private readonly Vector3[][] packetPoints={new Vector3[9],new Vector3[9],new Vector3[9]};
    private readonly int[] intakeCycles=new int[4],goldCycles=new int[3];
    private Transform centralHalo,absorbFlash;
    private float lastAbsorbTime=-100,absorbAngle;
    private int absorbedPulses;
    private readonly Material absorptionMaterial,detonationMaterial;
    private readonly LineRenderer[] flightTrails=new LineRenderer[3];
    private readonly Vector3[][] trailPoints={new Vector3[12],new Vector3[12],new Vector3[12]};
    private readonly Material starMaterial,haloMaterial,flashMaterial,outlineMaterial,groundMaterial,columnMaterial,shockMaterial,impactMaterial,shardMaterial,borderMaterial,dischargeMaterial;
    private readonly ParticleSystem goldDust,streaks,smoke,rays,formation;
    private readonly float windup;
    private readonly Camera previewCamera;
    private float time;
    private int emissionIndex;
    private bool disposed,burst;
    private readonly bool[] absorbedStars=new bool[3];
    private Transform pressureAura,nova;private Material pressureMaterial,novaMaterial;
    private IPNCameraHandle explosionShake;
    public int VisibleStars {get;private set;}
    public int ParticleCount => goldDust!=null?goldDust.particleCount+streaks.particleCount+smoke.particleCount+rays.particleCount+formation.particleCount:0;
    private sealed class RibbonData { public Mesh Mesh; public Vector3[] Vertices; public Color[] Colors; }

    public PNStarweaverTelegraph(Vector3 center,float radius,Scene scene,float windup,Camera previewCamera=null)
    {
        if(windup<=0)throw new ArgumentOutOfRangeException(nameof(windup));
        this.windup=windup;this.previewCamera=previewCamera;
        root=new GameObject("Starweaver constellation");SceneManager.MoveGameObjectToScene(root,scene);root.transform.position=center+Vector3.up*.025f;
        try
        {
            for(int i=0;i<3;i++){float a=i*Mathf.PI*2/3;points[i]=new Vector3(Mathf.Sin(a)*radius,0,Mathf.Cos(a)*radius);}
            starMaterial=MakeMaterial(8);haloMaterial=MakeMaterial(5,"Glow",new Color(.65f,.12f,1.2f),true);
            flashMaterial=MakeMaterial(5,"Flare",new Color(1.2f,.6f,1.4f),true);
            outlineMaterial=MakeMaterial(8);groundMaterial=MakeMaterial(4);borderMaterial=MakeMaterial(0);shardMaterial=MakeMaterial(0);
            columnMaterial=MakeMaterial(3,"Burst",Color.white,true);
            shockMaterial=MakeMaterial(6,"Noise",new Color(1.3f,.6f,1.7f),true);
            impactMaterial=MakeMaterial(5,"Glow",new Color(1.6f,.7f,1.8f),true);
            dischargeMaterial=MakeMaterial(3,"Burst",new Color(1,.65f,1.2f),true);
            var trailMaterial=MakeMaterial(10,"Ribbon",Color.white,false);
            var intakeMaterial=MakeMaterial(10,"Ribbon",Color.white,false);
            var headMaterial=MakeMaterial(5,"Glow",new Color(1.6f,.7f,2),true);
            var packetMaterial=MakeMaterial(5,"Ribbon",new Color(1.8f,1.35f,.35f),true);
            absorptionMaterial=MakeMaterial(5,"Glow",new Color(1.5f,.8f,1.8f),true);
            detonationMaterial=MakeMaterial(5,"Glow",new Color(1.3f,.4f,1.7f),true);
            var quad=Quad();var star=Star(false);var rim=Star(true);
            Draw("Ground region",MakeMesh("Triangle ground",points,new[]{0,1,2},new[]{Vector2.zero,Vector2.right,Vector2.up}),groundMaterial);
            for(int i=0;i<4;i++)
            {
                stars[i]=new GameObject(i==3?"Central star":"Star "+(i+1));stars[i].transform.SetParent(root.transform,false);
                var facingRoot=new GameObject("Upright crystal");facingRoot.transform.SetParent(stars[i].transform,false);
                var facing=facingRoot.AddComponent<PNCameraFacingTarget>();facing.ViewCamera=previewCamera;facing.Align();
                crystalRenderers[i,0]=Draw("Crystal facets",star,starMaterial,facingRoot.transform).GetComponent<MeshRenderer>();
                crystalRenderers[i,1]=Draw("Sharp contour",rim,outlineMaterial,facingRoot.transform).GetComponent<MeshRenderer>();
                crystalBlocks[i]=new MaterialPropertyBlock();
                var halo=Draw("Violet halo",quad,haloMaterial,facingRoot.transform);halo.transform.localScale=Vector3.one*1.8f;
                var flash=Draw("Charge flare",quad,flashMaterial,facingRoot.transform);flash.transform.localScale=Vector3.one*1.5f;
                if(i==3)
                {
                    centralHalo=halo.transform;
                    pressureMaterial=MakeMaterial(11,"Noise",Color.white,false);
                    pressureAura=Draw("Compressed violet-black aura",quad,pressureMaterial,facingRoot.transform).transform;pressureAura.localScale=Vector3.one*2.8f;pressureAura.localPosition=Vector3.forward*.012f;
                    absorbFlash=Draw("Absorption contact flash",quad,absorptionMaterial,facingRoot.transform).transform;
                }
                if(i<3)
                {
                    edges[i]=MakeMaterial(9,"Ribbon",new Color(1.3f,.95f,.4f),true);ribbons[i]=CreateRibbon();Draw("Golden gathering threads "+i,ribbons[i].Mesh,edges[i]);
                    Draw("Ground boundary "+i,GroundLine(points[i],points[(i+1)%3]),borderMaterial);
                    goldPackets[i]=FlowLine("Gold packet "+i,packetMaterial,9,.035f);
                    var go=new GameObject("Violet flight trail "+i);go.transform.SetParent(root.transform,false);var line=go.AddComponent<LineRenderer>();flightTrails[i]=line;
                    line.sharedMaterial=trailMaterial;line.useWorldSpace=false;line.positionCount=12;line.startWidth=.02f;line.endWidth=.19f;line.startColor=new Color(1,1,1,0);line.endColor=Color.white;
                }
            }
            for(int i=0;i<4;i++)
            {
                intakeLines[i]=FlowLine("Inward energy stream "+i,intakeMaterial,16,.25f);
                var head=Draw("Intake head "+i,quad,headMaterial);intakeHeads[i]=head.transform;
                var face=head.AddComponent<PNCameraFacingTarget>();intakeFacings[i]=face;face.ViewCamera=previewCamera;face.Align();
            }
            discharge=new GameObject("Downward discharge");discharge.transform.SetParent(root.transform,false);discharge.transform.localPosition=centre;discharge.transform.localRotation=Quaternion.Euler(180,0,0);
            var bolt=Column(.08f,centre.y);Draw("Discharge A",bolt,dischargeMaterial,discharge.transform);var cross=Draw("Discharge B",bolt,dischargeMaterial,discharge.transform);cross.transform.localRotation=Quaternion.Euler(0,90,0);
            CreateShards();
            curtain=new GameObject("Rising constellation burst");curtain.transform.SetParent(root.transform,false);curtain.transform.localPosition=centre;
            novaMaterial=MakeMaterial(12,"Noise",Color.white,false);
            var novaFace=new GameObject("Raised torn nova");novaFace.transform.SetParent(curtain.transform,false);
            var novaFacing=novaFace.AddComponent<PNCameraFacingTarget>();novaFacing.ViewCamera=previewCamera;novaFacing.Align();
            nova=Draw("Expanding torn energy",quad,novaMaterial,novaFace.transform).transform;
            shockwave=Draw("Outward shockwave",quad,shockMaterial,root.transform);shockwave.transform.localPosition=Vector3.up*.018f;shockwave.transform.localRotation=Quaternion.Euler(90,0,0);
            impactGlow=Draw("Ground impact flash",quad,impactMaterial,root.transform);impactGlow.transform.localPosition=Vector3.up*.025f;impactGlow.transform.localRotation=Quaternion.Euler(90,0,0);impactGlow.transform.localScale=Vector3.one*2.2f;
            var blastFace=new GameObject("Raised detonation glow");blastFace.transform.SetParent(curtain.transform,false);var facingBlast=blastFace.AddComponent<PNCameraFacingTarget>();facingBlast.ViewCamera=previewCamera;facingBlast.Align();Draw("Raised flash",quad,detonationMaterial,blastFace.transform).transform.localScale=Vector3.one*1.6f;
            goldDust=Particles("Gold sparks",MakeMaterial(1,null,new Color(1.7f,1.05f,.22f),true),0);
            streaks=Particles("Impact streaks",MakeMaterial(5,"Ribbon",new Color(1.3f,.4f,1.7f),true),1);
            smoke=Particles("Violet smoke",MakeMaterial(7,"Smoke",new Color(1.1f,.5f,1.4f),false),2);
            rays=Particles("Gold impact rays",MakeMaterial(5,"Ribbon",new Color(1.4f,.8f,.12f),true),3);
            formation=Particles("Condensing star dust",MakeMaterial(1,null,Color.white,true),4);
            if(Application.isPlaying)PNCameraFacingSystem.Register(this);
            SetPhase(0,false);
        }
        catch{Dispose();throw;}
    }
    private Material MakeMaterial(int mode,string mask=null,Color? tint=null,bool additive=false)
    {
        var shader=Resources.Load<Shader>("PNStarweaverEffects");if(shader==null)throw new InvalidOperationException("Starweaver effect shader missing");
        var m=new Material(shader){name="Starweaver effect "+mode,hideFlags=HideFlags.HideAndDontSave};materials.Add(m);m.SetFloat("_Mode",mode);
        m.SetColor("_Tint",tint??Color.white);m.SetFloat("_DstBlend",additive?1:10);
        if(mask!=null){var t=Resources.Load<Texture2D>("StarweaverVfx/"+mask);if(t==null)throw new InvalidOperationException("Starweaver texture missing: "+mask);m.SetTexture("_MainTex",t);}
        if(mode!=0){var t=Resources.Load<Texture2D>("StarweaverVfx/Noise");if(t==null)throw new InvalidOperationException("Starweaver noise missing");m.SetTexture("_NoiseTex",t);}
        return m;
    }
    private Mesh MakeMesh(string name,Vector3[] v,int[] tri,Vector2[] uv,Color[] colors=null)
    {
        if(colors==null){colors=new Color[v.Length];for(int i=0;i<colors.Length;i++)colors[i]=Color.white;}
        var m=new Mesh{name=name,hideFlags=HideFlags.HideAndDontSave};meshes.Add(m);m.vertices=v;m.triangles=tri;m.uv=uv;m.colors=colors;m.RecalculateBounds();return m;
    }
    private Mesh Quad()=>MakeMesh("VFX quad",new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)},new[]{0,1,2,0,2,3},new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});
    private Mesh Star(bool rim)
    {
        var edge=new[]{new Vector3(0,.52f,0),new Vector3(.12f,.12f,0),new Vector3(.40f,0,0),new Vector3(.12f,-.12f,0),new Vector3(0,-.52f,0),new Vector3(-.12f,-.12f,0),new Vector3(-.40f,0,0),new Vector3(-.12f,.12f,0)};
        var v=new Vector3[rim?48:24];var t=new int[v.Length];var c=new Color[v.Length];var uv=new Vector2[v.Length];
        var palette=new[]{new Color(.73f,.20f,1),new Color(.38f,.04f,.63f),new Color(.72f,.19f,.96f),new Color(.21f,.025f,.38f),new Color(.51f,.10f,.8f),new Color(.82f,.29f,1),new Color(.31f,.04f,.55f),new Color(.6f,.14f,.9f)};
        for(int i=0;i<8;i++)
        {
            int next=(i+1)%8;
            if(rim)
            {
                int k=i*6;v[k]=edge[i]*1.045f;v[k+1]=edge[next]*1.045f;v[k+2]=edge[next]*.93f;v[k+3]=v[k];v[k+4]=v[k+2];v[k+5]=edge[i]*.93f;
                for(int j=0;j<6;j++){t[k+j]=k+j;c[k+j]=new Color(.8f,.28f,1,.85f);}
            }
            else
            {
                int k=i*3;v[k]=Vector3.zero;v[k+1]=edge[i];v[k+2]=edge[next];t[k]=k;t[k+1]=k+1;t[k+2]=k+2;c[k]=Color.Lerp(palette[i],new Color(1,.65f,1),.35f);c[k+1]=c[k+2]=palette[i];
            }
        }
        for(int i=0;i<v.Length;i++)uv[i]=new Vector2(v[i].x/.85f+.5f,v[i].y/1.1f+.5f);
        return MakeMesh(rim?"Crystal outline":"Four point crystal facets",v,t,uv,c);
    }
    private GameObject Draw(string name,Mesh mesh,Material material,Transform parent=null)
    {
        var go=new GameObject(name);go.transform.SetParent(parent!=null?parent:root.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;return go;
    }
    private Mesh GroundLine(Vector3 a,Vector3 b)
    {
        var w=Vector3.Cross((b-a).normalized,Vector3.up)*.007f;a.y=b.y=.008f;
        var c=new Color[4];for(int i=0;i<4;i++)c[i]=new Color(.75f,.25f,1,.45f);
        return MakeMesh("Ground outline",new[]{a-w,a+w,b-w,b+w},new[]{0,1,2,1,3,2},new[]{Vector2.zero,Vector2.up,Vector2.right,Vector2.one},c);
    }
    private RibbonData CreateRibbon()
    {
        int row=(Segments+1)*2;var v=new Vector3[row*Strands];var uv=new Vector2[v.Length];var c=new Color[v.Length];var tri=new int[Segments*6*Strands];
        for(int s=0;s<Strands;s++)for(int i=0;i<=Segments;i++)
        {
            int k=s*row+i*2;float u=i/(float)Segments;uv[k]=new Vector2(u,0);uv[k+1]=new Vector2(u,1);
            c[k]=c[k+1]=new Color(1,1,1,s==0?1:s==1?.65f:.45f);
            if(i==Segments)continue;int x=(s*Segments+i)*6;tri[x]=k;tri[x+1]=k+1;tri[x+2]=k+2;tri[x+3]=k+1;tri[x+4]=k+3;tri[x+5]=k+2;
        }
        var m=MakeMesh("Gold charging filaments",v,tri,uv,c);m.MarkDynamic();return new RibbonData{Mesh=m,Vertices=v,Colors=c};
    }
    public bool Align()
    {
        if(disposed||root==null)return false;
        var camera=PNCameraFacing.ResolveCamera(previewCamera,Application.isPlaying);var forward=camera!=null?camera.transform.forward:Vector3.forward;
        for(int e=0;e<3;e++)
        {
            var a=anchors[e];var b=centre;var d=(b-a).normalized;var side=Vector3.Cross(forward,d).normalized;
            if(side.sqrMagnitude<.1f)side=Vector3.up;var depth=Vector3.Cross(d,side).normalized;
            var data=ribbons[e];if(data==null)continue;
            for(int s=0;s<Strands;s++)for(int i=0;i<=Segments;i++)
            {
                float u=i/(float)Segments,envelope=Mathf.Sin(u*Mathf.PI);
                float wave=Mathf.Sin(time*1.6f+e*1.7f+s*.9f)*.02f;
                var p=Vector3.Lerp(a,b,u)+side*(envelope*(.07f+s*.026f+wave))+depth*(envelope*(s-1.5f)*.025f);
                float width=(s==0?.008f:.0045f)*Mathf.Pow(Mathf.Max(.01f,envelope),.4f);
                int k=s*(Segments+1)*2+i*2;data.Vertices[k]=p-side*width;data.Vertices[k+1]=p+side*width;
                float delay=.12f+Mathf.Repeat(e*.19f+s*.23f,.5f);
                float flicker=s==0?.7f:Mathf.Pow(.5f+.5f*Mathf.Sin(time*(2+s*.3f)+e*2+s*1.7f),2);
                var color=new Color(1,1,1,Mathf.Clamp01((time-delay)/.2f)*flicker);data.Colors[k]=data.Colors[k+1]=color;
            }
            data.Mesh.vertices=data.Vertices;data.Mesh.colors=data.Colors;data.Mesh.RecalculateBounds();
        }
        return true;
    }
    private Mesh Column(float width,float height)
    {
        const int rows=20;var v=new Vector3[(rows+1)*2];var uv=new Vector2[v.Length];var tri=new int[rows*6];
        for(int i=0;i<=rows;i++)
        {
            float y=i/(float)rows,w=width*(.22f+.78f*Mathf.Pow(1-y,.8f));v[i*2]=new Vector3(-w,y*height,0);v[i*2+1]=new Vector3(w,y*height,0);uv[i*2]=new Vector2(0,y);uv[i*2+1]=new Vector2(1,y);
            if(i==rows)continue;int k=i*2,t=i*6;tri[t]=k;tri[t+1]=k+1;tri[t+2]=k+2;tri[t+3]=k+1;tri[t+4]=k+3;tri[t+5]=k+2;
        }
        return MakeMesh("Central upward jet",v,tri,uv);
    }
    private ParticleSystem Particles(string name,Material material,int kind)
    {
        var go=new GameObject(name);go.transform.SetParent(root.transform,false);var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;main.playOnAwake=false;main.loop=false;main.maxParticles=192;main.simulationSpace=ParticleSystemSimulationSpace.Local;main.startSpeed=0;main.gravityModifier=kind==0?.45f:0;
        var emission=ps.emission;emission.enabled=false;var shape=ps.shape;shape.enabled=false;
        var col=ps.colorOverLifetime;col.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(.8f,.3f),new GradientAlphaKey(0,1)});col.color=gradient;
        var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,kind==2?.35f:1,1,kind==2?1.5f:0));
        var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.renderMode=(kind==1||kind==3)?ParticleSystemRenderMode.Stretch:ParticleSystemRenderMode.Billboard;renderer.lengthScale=kind==3?2:kind==1?1.8f:1;renderer.velocityScale=(kind==1||kind==3)?.12f:0;
        ps.useAutoRandomSeed=false;ps.randomSeed=(uint)(47+kind*137);return ps;
    }
    private void Emit(ParticleSystem ps,Vector3 position,int count,int kind)
    {
        for(int i=0;i<count;i++)
        {
            int n=emissionIndex++;float angle=n*2.399963f,speed=.2f+(n%7)*.075f;
            if(kind==3)speed=1.1f+(n%7)*.18f;else if(kind==0&&count>16)speed*=2.3f;
            var p=new ParticleSystem.EmitParams{position=position,velocity=new Vector3(Mathf.Cos(angle)*speed,kind==1?1.1f+(n%6)*.18f:kind==2?.15f:.3f+(n%4)*.13f,Mathf.Sin(angle)*speed),startLifetime=kind==2?1.05f:kind==3?.28f+(n%4)*.04f:.45f+(n%4)*.065f,startSize=kind==2?.7f:kind==1?.035f:.035f+(n%4)*.012f,startColor=Color.white,rotation=n*47};ps.Emit(p,1);
        }
    }
    private LineRenderer FlowLine(string name,Material material,int count,float width)
    {
        var go=new GameObject(name);go.transform.SetParent(root.transform,false);var line=go.AddComponent<LineRenderer>();
        line.sharedMaterial=material;line.useWorldSpace=false;line.positionCount=count;line.startWidth=0;line.endWidth=width;line.startColor=new Color(1,1,1,0);line.endColor=Color.white;return line;
    }
    private void CreateShards()
    {
        var source=Star(false);var v=source.vertices;var edge=new Vector3[8];for(int k=0;k<8;k++)edge[k]=v[k*3+1];
        shardRoots[0]=new GameObject("Shattered star 0");shardRoots[0].transform.SetParent(root.transform,false);
        var face=new GameObject("Facet plane");face.transform.SetParent(shardRoots[0].transform,false);var facing=face.AddComponent<PNCameraFacingTarget>();facing.ViewCamera=previewCamera;facing.Align();
        for(int k=0;k<4;k++)
        {
            int tip=k*2;var shape=new[]{Vector3.zero,edge[(tip+7)%8],edge[tip],edge[(tip+1)%8]};var centroid=(shape[0]+shape[1]+shape[2]+shape[3])*.25f;shardCenters[k]=centroid;
            var vertices=new Vector3[8];var colors=new Color[8];var uv=new Vector2[8];
            for(int j=0;j<8;j++){vertices[j]=shape[j%4]-centroid+Vector3.forward*(j<4?.018f:-.018f);colors[j]=j<4?new Color(.7f,.2f,1):new Color(.25f,.04f,.45f);}
            var tri=new[]{0,1,2,0,2,3,4,6,5,4,7,6,0,4,5,0,5,1,1,5,6,1,6,2,2,6,7,2,7,3,3,7,4,3,4,0};
            var piece=MakeMesh("Thick crystal wing "+k,vertices,tri,uv,colors);shardPieces[0,k]=Draw("Broken facet "+k,piece,shardMaterial,face.transform).transform;
        }
        shardRoots[0].SetActive(false);
    }
    private Vector3 SmallStart(int i)=>points[i]+Vector3.up*.25f;
    private Vector3 SmallFlight(int i,float u)
    {
        float angle=Mathf.Atan2(points[i].z,points[i].x)+u*Mathf.PI*1.6f;
        float radius=new Vector2(points[i].x,points[i].z).magnitude*Mathf.Pow(1-u,1.2f);
        return new Vector3(Mathf.Cos(angle)*radius,Mathf.Lerp(.25f,centre.y,u)+Mathf.Sin(u*Mathf.PI)*.08f,Mathf.Sin(angle)*radius);
    }
    private Vector3 IntakePosition(int stream,float u)
    {
        float angle=stream*2.399963f+u*Mathf.PI*1.4f,radius=(1.05f+(stream%2)*.12f)*Mathf.Pow(1-u,.9f);
        return centre+new Vector3(Mathf.Cos(angle)*radius,-(.35f+(stream%3)*.12f)*(1-u),Mathf.Sin(angle)*radius);
    }
    private void Absorb(int count,int source)
    {
        absorbedPulses+=count;lastAbsorbTime=time;
        var direction=source<4?IntakePosition(source,.92f)-centre:source<7?anchors[source-4]-centre:Vector3.up;
        var view=PNCameraFacing.ResolveCamera(previewCamera,Application.isPlaying);
        absorbAngle=Mathf.Atan2(Vector3.Dot(direction,view!=null?view.transform.up:Vector3.up),Vector3.Dot(direction,view!=null?view.transform.right:Vector3.right));
    }
    private Vector3 GoldPath(int edge,float u,Vector3 forward)
    {
        var a=anchors[edge];var d=(centre-a).normalized;var side=Vector3.Cross(forward,d).normalized;if(side.sqrMagnitude<.1f)side=Vector3.up;
        var depth=Vector3.Cross(d,side).normalized;float envelope=Mathf.Sin(u*Mathf.PI),wave=Mathf.Sin(time*1.6f+edge*1.7f)*.02f;
        return Vector3.Lerp(a,centre,u)+side*(envelope*(.07f+wave))+depth*(envelope*-1.5f*.025f);
    }
    private void UpdateIntake(float progress,bool pulsed)
    {
        bool active=!pulsed&&progress<.94f;
        for(int i=0;i<4;i++)
        {
            float elapsed=Mathf.Max(0,time-.15f-i*.10f);float travel=elapsed*.85f+elapsed*elapsed*.3f;int cycle=Mathf.FloorToInt(travel);float phase=Mathf.Repeat(travel,1);
            bool shown=active&&time>.15f+i*.10f;intakeLines[i].gameObject.SetActive(shown);intakeHeads[i].gameObject.SetActive(false);
            if(shown)
            {
                if(cycle>intakeCycles[i])Absorb(cycle-intakeCycles[i],i);intakeCycles[i]=cycle;
                for(int j=0;j<16;j++){float t=Mathf.Max(0,phase-(1-j/15f)*.48f);intakePoints[i][j]=IntakePosition(i,t*t);}
                intakeLines[i].SetPositions(intakePoints[i]);float fade=Mathf.Clamp01(phase/.08f)*Mathf.Clamp01((1-phase)/.05f);
                intakeLines[i].startColor=new Color(1,1,1,0);intakeLines[i].endColor=new Color(1,1,1,fade);intakeLines[i].endWidth=.32f+progress*.18f;
                var head=intakeHeads[i];head.localPosition=IntakePosition(i,phase*phase);head.localScale=Vector3.one*(.14f-.09f*phase)*fade;
                var facing=intakeFacings[i];facing.FootAnchor=head.localPosition;facing.Align();
            }
        }
        var view=PNCameraFacing.ResolveCamera(previewCamera,Application.isPlaying);var forward=view!=null?view.transform.forward:Vector3.forward;
        for(int i=0;i<3;i++)
        {
            float travel=time*.8f+time*time*.3f+i*.31f;int cycle=Mathf.FloorToInt(travel);float phase=Mathf.Repeat(travel,1),u=phase*phase;edges[i].SetFloat("_FlowHead",u);
            bool shown=active&&time>.2f;goldPackets[i].gameObject.SetActive(shown);
            if(!shown)continue;
            if(cycle>goldCycles[i])Absorb(cycle-goldCycles[i],i+4);goldCycles[i]=cycle;
            for(int j=0;j<9;j++){float t=Mathf.Max(0,phase-(1-j/8f)*.16f);packetPoints[i][j]=GoldPath(i,t*t,forward);}
            goldPackets[i].SetPositions(packetPoints[i]);goldPackets[i].endColor=new Color(1,.9f,.4f,Mathf.Clamp01((1-phase)/.05f));
        }
    }
    private void FormationDust(bool pulsed)
    {
        int count=0;
        if(!pulsed)for(int i=0;i<4;i++)
        {
            bool main=i==3;if(!main&&time>.24f)continue;
            if(main&&time>=windup*.925f)continue;
            for(int k=0;k<20;k++)
            {
                float pull=main?Mathf.Repeat(time*1.2f+k/20f,1):Mathf.Clamp01(time/.24f);
                float angle=k*2.399963f+i*.4f,r=(1-pull)*(main?.24f:.16f);
                var target=main?centre:anchors[i];var position=target+new Vector3(Mathf.Cos(angle)*r,-(1-pull)*.16f,Mathf.Sin(angle)*r);position.y=Mathf.Max(.04f,position.y);
                formationBuffer[count++]=new ParticleSystem.Particle{position=position,startLifetime=1,remainingLifetime=1,startSize=(main?.014f:.01f)+(k%3)*.006f,startColor=k%3==0?new Color(1.4f,.95f,.25f,1):new Color(.65f,.15f,1,1),rotation=k*37};
            }
        }
        formation.SetParticles(formationBuffer,count);
    }
    public void SetPhase(float progress,bool pulsed)
    {
        if(disposed||root==null)return;
        float next=Mathf.Max(time,progress*windup),dt=next-time;time=next;
        if(dt>0){goldDust.Simulate(dt,false,false,false);streaks.Simulate(dt,false,false,false);smoke.Simulate(dt,false,false,false);rays.Simulate(dt,false,false,false);}
        float flow=time<windup?Mathf.Min(time,windup-.1f):time;
        foreach(var m in materials)m.SetFloat("_FxTime",flow);
        float post=time-windup,charge=Mathf.SmoothStep(0,1,Mathf.Clamp01(progress/.925f));
        float flight=0;
        float compression=Mathf.Clamp01((progress-.94f)/.06f);
        VisibleStars=0;starMaterial.SetFloat("_Opacity",1);outlineMaterial.SetFloat("_Opacity",.6f+charge*.4f);haloMaterial.SetFloat("_Opacity",.08f+charge*.16f);flashMaterial.SetFloat("_Opacity",charge*.35f);
        float groundFade=pulsed?Mathf.Clamp01(1-post/.38f):1;groundMaterial.SetFloat("_Opacity",groundFade);borderMaterial.SetFloat("_Opacity",groundFade);
        shardMaterial.SetFloat("_Opacity",pulsed?Mathf.Clamp01(1-Mathf.Max(0,post-.25f)/.6f):0);
        for(int i=0;i<3;i++)
        {
            float launch=.66f+i*.04f;float travel=Mathf.Clamp01((progress-launch)/.20f);flight=Mathf.Pow(travel,2.2f);
            bool visible=!pulsed&&travel<1;stars[i].SetActive(visible);anchors[i]=SmallFlight(i,flight);
            if(!pulsed&&travel>=1&&!absorbedStars[i]){absorbedStars[i]=true;Absorb(4,i+4);Emit(goldDust,centre,8,0);}
            if(visible)
            {
                VisibleStars++;float pop=Mathf.SmoothStep(0,1,Mathf.Clamp01(time/.14f));stars[i].transform.localScale=new Vector3(1,1+flight*.8f,1)*(.18f*(.5f+.5f*pop))*Mathf.Lerp(1,.65f,Mathf.Clamp01((travel-.87f)/.13f));stars[i].transform.localPosition=anchors[i];
                var block=crystalBlocks[i];float emitPhase=Mathf.Repeat(time*.8f+time*time*.3f+i*.31f,1);block.SetColor("_Tint",Color.Lerp(new Color(.55f,.55f,.7f),Color.white,Mathf.Clamp01(emitPhase/.2f)));block.SetFloat("_Formation",Mathf.Clamp01(time/.13f));block.SetFloat("_Crack",0);crystalRenderers[i,0].SetPropertyBlock(block);crystalRenderers[i,1].SetPropertyBlock(block);
                if(!revealed[i]&&time>.1f){revealed[i]=true;Emit(goldDust,anchors[i],4,0);}
            }
            edges[i].SetFloat("_Reveal",Mathf.Clamp01((time-.12f)/.3f));edges[i].SetFloat("_Opacity",visible?(.4f+charge*.6f)*Mathf.Clamp01((1-flight)*3):0);
            var trail=flightTrails[i];trail.gameObject.SetActive(visible&&flight>0);
            if(visible&&flight>0)
            {
                for(int j=0;j<12;j++)trailPoints[i][j]=SmallFlight(i,Mathf.Clamp01(flight-(1-j/11f)*.28f));
                trail.SetPositions(trailPoints[i]);trail.endWidth=.16f+flight*.12f;
            }
        }
        UpdateIntake(progress,pulsed);
        float absorption=!pulsed?Mathf.Exp(-Mathf.Max(0,time-lastAbsorbTime)*16):0;
        stars[3].SetActive(!pulsed);stars[3].transform.localPosition=centre+new Vector3(Mathf.Sin(time*85),Mathf.Cos(time*73),0)*absorption*.016f;
        stars[3].transform.localScale=Vector3.one*Mathf.Lerp(.22f,.48f,charge)*Mathf.Lerp(1,.42f,compression)*(1+absorption*.18f);
        var central=crystalBlocks[3];central.SetFloat("_Formation",Mathf.Clamp01(time/.24f));central.SetFloat("_Crack",Mathf.Clamp01((progress-.7f)/.3f)*.5f);central.SetFloat("_Charge",Mathf.Clamp01(absorbedPulses/12f));central.SetFloat("_Absorb",absorption);crystalRenderers[3,0].SetPropertyBlock(central);crystalRenderers[3,1].SetPropertyBlock(central);
        pressureMaterial.SetFloat("_Charge",charge);pressureMaterial.SetFloat("_Absorb",absorption);pressureMaterial.SetFloat("_Opacity",(1-compression)*(.55f+charge*.4f));pressureAura.localScale=Vector3.one*(2.8f+absorption*.4f);
        centralHalo.localScale=Vector3.one*(1.7f+absorption*.4f);absorptionMaterial.SetFloat("_Opacity",absorption*.9f);
        absorbFlash.localPosition=new Vector3(Mathf.Cos(absorbAngle)*.22f,Mathf.Sin(absorbAngle)*.22f,-.01f);absorbFlash.localScale=Vector3.one*(.18f+absorption*.25f);
        shardRoots[0].SetActive(pulsed&&post<.9f);shardRoots[0].transform.localPosition=centre;
        if(pulsed)for(int k=0;k<4;k++)
        {
            float a=k*Mathf.PI/2;var direction=new Vector3(Mathf.Cos(a)*.85f,.2f+Mathf.Sin(a)*.4f,((k%3)-1)*.25f);
            var part=shardPieces[0,k];part.localPosition=shardCenters[k]*.48f+direction*post+Vector3.down*(post*post*.8f);part.localScale=Vector3.one*.48f;part.localRotation=Quaternion.Euler(post*(k*27-80),post*(k*17),post*(k%2==0?145:-120));
        }
        discharge.SetActive(pulsed&&post<.22f);dischargeMaterial.SetFloat("_Rise",Mathf.Clamp01(post/.1f));dischargeMaterial.SetFloat("_Opacity",pulsed?Mathf.Clamp01(1-post/.22f)*.7f:0);
        FormationDust(pulsed);Align();curtain.SetActive(pulsed);
        novaMaterial.SetFloat("_Rise",Mathf.Clamp01(post/.32f));novaMaterial.SetFloat("_Opacity",pulsed?Mathf.Clamp01(1-Mathf.Max(0,post-.08f)/.42f):0);nova.localScale=Vector3.one*(.25f+2.8f*Mathf.Pow(Mathf.Clamp01(post/.38f),.45f));
        columnMaterial.SetFloat("_Rise",Mathf.Clamp01(post/.10f));columnMaterial.SetFloat("_Opacity",pulsed?Mathf.Clamp01(1-Mathf.Max(0,post-.10f)/.35f)*.6f:0);
        float groundPost=post-.08f;shockwave.SetActive(pulsed&&groundPost>=0);shockMaterial.SetFloat("_Opacity",pulsed?Mathf.Clamp01(1-groundPost/.28f):0);shockwave.transform.localScale=Vector3.one*(.2f+Mathf.Max(0,groundPost)*8);
        impactGlow.SetActive(pulsed&&groundPost>=0);impactMaterial.SetFloat("_Opacity",pulsed?Mathf.Clamp01(1-groundPost/.18f)*.3f:0);detonationMaterial.SetFloat("_Opacity",pulsed?Mathf.Clamp01(1-post/.2f):0);
        if(pulsed&&!burst)
        {
            burst=true;
            if(Application.isPlaying&&previewCamera==null)explosionShake=PNCameraServices.Current.RequestShake(root,"starweaver.constellation",new PNShakeSpec(.075f,25,.23f,.18f,0,PNShakeShape.Recoil,1,.5f,0,8,PNShakeFalloff.Quadratic));
            Emit(goldDust,centre,72,0);Emit(streaks,centre,18,1);Emit(rays,centre,12,3);
            Emit(goldDust,centre,24,0);
            for(int i=0;i<20;i++){float a=i*2.399963f;Emit(smoke,centre+new Vector3(Mathf.Cos(a)*.3f,0,Mathf.Sin(a)*.3f),1,2);}
        }
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;explosionShake?.Dispose();explosionShake=null;PNCameraFacingSystem.Unregister(this);
        if(root!=null){root.SetActive(false);Destroy(root);}foreach(var m in meshes)Destroy(m);foreach(var m in materials)Destroy(m);meshes.Clear();materials.Clear();
    }
    private static void Destroy(UnityEngine.Object o){if(o==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(o);else UnityEngine.Object.DestroyImmediate(o);}
}
#endif
